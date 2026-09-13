using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    internal sealed class RouteMotion
    {
        public KitchenModel? Model;
        public KitchenStation? Station;
        public Point2 Target;
        public Point2[] Points=[];
        public int Waypoint;
        public double LastDistance=double.PositiveInfinity;
        public int StalledFrames;
        public int LastPlanFrame=-100;
        public bool Braking;
        public int BrakeFrames;
        public int BrakeStillFrames;
        public Point2 LastBrakePosition;
        public Point2 BrakeStartPosition;
        public int FailedBrakes;
        public int NoPathSince=-1;
        public RouteDash? Dash;
        public bool DashDisabled;
        public string? LastDashDecision;
    }
    private static JsonObject Chef(JsonNode state,int player)=>(state["chefs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c=>(c["playerId"]??c["player"])?.GetValue<int>()==player)??throw new InvalidOperationException($"Chef {player} is absent.");
    private static JsonObject? Entity(JsonNode state,int id)=>(state["entities"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(e=>e["id"]?.GetValue<int>()==id);
    private static Point2 ChefPosition(JsonNode state,int player)=>KitchenModel.Position(Chef(state,player)["position"]);
    private static int Held(JsonNode state,int player)=>Chef(state,player)["heldEntityId"]?.GetValue<int>()??0;
    private static KitchenObstacle[] OtherChefs(JsonNode state,int player,KitchenModel model,bool ignore)
    {
        if(ignore)return [];
        return (state["chefs"] as JsonArray)?.OfType<JsonObject>().Where(c=>c["playerId"]?.GetValue<int>()!=player).Select(c=>
        {
            var p=KitchenModel.Position(c["position"]);double r=model.ChefRadius;
            return new KitchenObstacle("chef:"+c["playerId"],c["entityId"]?.GetValue<int>()??0,new(p.X-r,p.X+r,p.Z-r,p.Z+r),"Live chef capsule",true,CircleRadius:r);
        }).ToArray()??[];
    }
    private bool InitializeMotion(Active action,JsonNode state)
    {
        var spec=action.Spec;int player=Player(spec);var position=ChefPosition(state,player);
        var motion=action.Motion??=new RouteMotion();
        if(motion.Points.Length==0&&action.Frames-motion.LastPlanFrame<10)return false;
        bool measured=state["scene"]?.ToString()=="s_Day_3_4";
        if(measured)
        {
            motion.Model=KitchenModel.Build(state.AsObject());
            trace?.Event("navigationGeometry",new JsonObject{["player"]=player,["actionFrames"]=action.Frames,["stations"]=motion.Model.Stations.Length,["obstacles"]=motion.Model.Obstacles.Length,["modelWarnings"]=JsonSerializer.SerializeToNode(motion.Model.Warnings),["snapshotWarnings"]=state["warnings"]?.DeepClone()});
        }
        if(spec["station"] is { } selector)
        {
            if(motion.Model is null)throw new ArgumentException("Station aliases require a measured Carnival kitchen snapshot.");
            var candidates=motion.Model.Candidates(selector.ToString());
            if(candidates.Length==0)throw new ArgumentException("No measured station matches "+selector);
            if(spec["type"]?.ToString()=="chop")
            {
                candidates=candidates.Where(s=>Entity(state,s.EntityId)?["attachedEntityId"]?.GetValue<int>() is { } attached&&attached!=0).ToArray();
                if(candidates.Length==0)throw new InvalidOperationException("No selected chopping board holds an item.");
            }
            var heldIds=(state["chefs"] as JsonArray)?.OfType<JsonObject>().Select(c=>c["heldEntityId"]?.GetValue<int>()??0).ToHashSet()??[];
            var obstacles=OtherChefs(state,player,motion.Model,spec["ignoreChefs"]?.GetValue<bool>()==true);
            var attempts=candidates.Where(s=>!heldIds.Contains(s.EntityId)).SelectMany(station=>station.Approaches.Where(a=>a.Region==motion.Model.RegionAt(position)).Select(approach=>
                (Station:station,Path:Navigation.FindPath(motion.Model,position,approach.Position,.15,obstacles)))).ToArray();
            var choices=attempts.Where(c=>c.Path.Success).OrderBy(c=>c.Path.Length).ThenBy(c=>c.Station.Key,StringComparer.Ordinal).ToArray();
            if(choices.Length==0)
            {
                var reasons=attempts.Select(c=>c.Path.Error??"unknown").Distinct().ToArray();
                if(reasons.Length==0)reasons=["No approach points exist on the chef's measured platform."];
                if(motion.NoPathSince<0)motion.NoPathSince=action.Frames;
                trace?.Event("navigationWaiting",new JsonObject{["station"]=selector.ToString(),["reason"]=string.Join("; ",reasons),["actionFrames"]=action.Frames,["waitFrames"]=action.Frames-motion.NoPathSince});
                if(action.Frames-motion.NoPathSince>=(spec["maxNoPathFrames"]?.GetValue<int>()??180))throw new TimeoutException("No reachable station approach: "+string.Join("; ",reasons));
                motion.LastPlanFrame=action.Frames;return false;
            }
            // Each legal retry tries another measured approach before repeating.
            var choice=choices[action.Retries%choices.Length];
            motion.Station=choice.Station;motion.Target=choice.Path.Points[^1];motion.Points=choice.Path.Points;
            motion.Waypoint=1;motion.LastPlanFrame=action.Frames;motion.StalledFrames=0;
            motion.NoPathSince=-1;
            AuditPath(action,choice.Path);return true;
        }
        JsonNode target=spec["target"]??(spec["targetEntityId"] is { } id?Entity(state,id.GetValue<int>())?["position"]:null)??throw new ArgumentException("Movement requires station, target, or targetEntityId.");
        var p=KitchenModel.Position(target);motion.Target=new(p.X+Number(spec["offset"]?["x"]),p.Z+Number(spec["offset"]?["z"]));
        if(motion.Model is null){motion.Points=[position,motion.Target];motion.Waypoint=1;return true;}
        var path=Navigation.FindPath(motion.Model,position,motion.Target,.15,OtherChefs(state,player,motion.Model,spec["ignoreChefs"]?.GetValue<bool>()==true));
        motion.LastPlanFrame=action.Frames;
        if(!path.Success)
        {
            if(motion.NoPathSince<0)motion.NoPathSince=action.Frames;
            trace?.Event("navigationWaiting",JsonSerializer.SerializeToNode(path));
            if(action.Frames-motion.NoPathSince>=(spec["maxNoPathFrames"]?.GetValue<int>()??180))throw new TimeoutException("No reachable point path: "+path.Error);
            return false;
        }
        motion.NoPathSince=-1;
        motion.Points=path.Points;motion.Waypoint=1;motion.StalledFrames=0;AuditPath(action,path);return true;
    }
    private void AuditPath(Active action,NavigationPath path)=>trace?.Event("pathPlanned",new JsonObject{["player"]=Player(action.Spec),["runtimeEntityId"]=action.Motion?.Station?.EntityId,["stationKey"]=action.Motion?.Station?.Key,["actionFrames"]=action.Frames,["path"]=JsonSerializer.SerializeToNode(path)});

    private bool DriveNavigation(Active action,JsonNode state,JsonObject input)
    {
        if(action.Motion is null||action.Motion.Points.Length==0)
        {
            if(action.Motion is not null&&action.Frames-action.Motion.LastPlanFrame<10)return false;
            if(!InitializeMotion(action,state))return false;
        }
        var motion=action.Motion!;var position=ChefPosition(state,Player(action.Spec));
        double tolerance=action.Spec["tolerance"]?.GetValue<double>()??.10;
        var target=motion.Points[Math.Min(motion.Waypoint,motion.Points.Length-1)];
        var distance=position.Distance(target);
        if(ContinueDash(action,state,input,position,target,distance))return false;
        var cachedVelocity=KitchenModel.Position(Chef(state,Player(action.Spec))["lastVelocity"]);
        double fixedDelta=state["fixedDeltaTime"] is null?.02:Number(state["fixedDeltaTime"]);
        var projected=new Point2(position.X+cachedVelocity.X*fixedDelta,position.Z+cachedVelocity.Z*fixedDelta);
        bool nextFrameHasPhysics=NextFrameHasPhysics(state);
        if(motion.Braking)
        {
            motion.BrakeFrames++;
            if(position.Distance(motion.LastBrakePosition)<.002&&cachedVelocity.Distance(default)<.05)motion.BrakeStillFrames++;else motion.BrakeStillFrames=0;
            motion.LastBrakePosition=position;
            if(motion.BrakeFrames<2||motion.BrakeStillFrames<(action.Spec["stableFrames"]?.GetValue<int>()??2))return false;
            if(distance<=tolerance)
            {
                motion.FailedBrakes=0;
                if(motion.Waypoint>=motion.Points.Length-1)return true;
                motion.Waypoint++;motion.LastDistance=double.PositiveInfinity;target=motion.Points[motion.Waypoint];distance=position.Distance(target);
            }
            else if(position.Distance(motion.BrakeStartPosition)<.01)
            {
                motion.FailedBrakes++;
                trace?.Event("navigationBrakeBlocked",new JsonObject{["player"]=Player(action.Spec),["position"]=JsonSerializer.SerializeToNode(position),["target"]=JsonSerializer.SerializeToNode(target),["failedBrakes"]=motion.FailedBrakes,["distance"]=distance,["actionFrames"]=action.Frames});
                if(motion.FailedBrakes>=(action.Spec["maxFailedBrakes"]?.GetValue<int>()??6))throw new InvalidOperationException("Native collision blocked repeated queued braking displacements; measured navigation geometry needs correction.");
                if(motion.FailedBrakes>=2)
                {
                    motion.Braking=false;motion.Points=[];motion.LastPlanFrame=action.Frames-10;
                    InitializeMotion(action,state);return false;
                }
            }
            else motion.FailedBrakes=0;
            motion.Braking=false;motion.BrakeFrames=0;motion.BrakeStillFrames=0;
        }
        if(distance<=tolerance||nextFrameHasPhysics&&projected.Distance(target)<=tolerance)
        {
            if (TryContinueIntermediateWaypoint(action, state, position, projected))
            {
                target = motion.Points[motion.Waypoint]; distance = position.Distance(target);
            }
            else
            {
            // Native movement normalizes every nonzero stick vector. Its
            // FixedUpdate consumes the previous Update's cached velocity, so
            // analog scaling cannot brake. Release before the queued physics
            // displacement reaches the waypoint, then observe a settled pose.
            motion.Braking=true;motion.BrakeFrames=0;motion.BrakeStillFrames=0;motion.LastBrakePosition=position;motion.BrakeStartPosition=position;
            trace?.Event("navigationBrake",new JsonObject{["player"]=Player(action.Spec),["distance"]=distance,["predictedDistance"]=projected.Distance(target),["cachedVelocity"]=JsonSerializer.SerializeToNode(cachedVelocity),["nextFrameHasPhysics"]=nextFrameHasPhysics,["actionFrames"]=action.Frames});
            return false;
            }
        }
        action.StableFrames=0;
        bool blocked=false;
        if(motion.Model is not null)
        {
            var region=motion.Model.RegionAt(position);
            blocked=region is null||!Navigation.SegmentClear(motion.Model,position,target,region,OtherChefs(state,Player(action.Spec),motion.Model,action.Spec["ignoreChefs"]?.GetValue<bool>()==true));
            if(motion.Station is { } station&&Entity(state,station.EntityId) is { } entity&&KitchenModel.Position(entity["position"]).Distance(station.Position)>.2)blocked=true;
        }
        if(distance>=motion.LastDistance-.001)motion.StalledFrames++;else motion.StalledFrames=0;
        motion.LastDistance=distance;
        if(blocked||motion.StalledFrames>45)
        {
            if(action.Frames-motion.LastPlanFrame>=10)
            {
                trace?.Event("pathReplan",new JsonObject{["player"]=Player(action.Spec),["reason"]=blocked?"live segment blocked or target moved":"movement stalled",["actionFrames"]=action.Frames,["applicationFocused"]=state["applicationFocused"]?.DeepClone(),["canAcceptInput"]=Chef(state,Player(action.Spec))["canAcceptInput"]?.DeepClone(),["inputSuppressed"]=Chef(state,Player(action.Spec))["inputSuppressed"]?.DeepClone()});
                motion.Points=[];InitializeMotion(action,state);
            }
            return false;
        }
        SetMovement(action,input,position,target,1);
        MaybeStartDash(action,state,input,position,target,distance);
        return false;
    }
    private static bool NextFrameHasPhysics(JsonNode state)
    {
        // This phase rule is only established for the instrumented native
        // 60 Hz / 50 Hz schedule. Missing diagnostics retain the conservative
        // queued-step predictor used by older recordings and pure fixtures.
        double logicalDelta=Number(state["unityDeltaTime"]??state["clientDeltaTime"]);
        if(Math.Abs(logicalDelta-1.0/60)>.00001||Math.Abs(Number(state["fixedDeltaTime"])-.02)>.00001)return true;
        return state["framesSinceNoPhysics"]?.GetValue<int>()!=5;
    }
    private static void SetMovement(Active action,JsonObject input,Point2 position,Point2 target,double magnitude)
    {
        double dx=target.X-position.X,dz=target.Z-position.Z,distance=Math.Sqrt(dx*dx+dz*dz);
        if(distance<.000001)return;
        input["x"]=dx/distance*magnitude*(action.Spec["invertX"]?.GetValue<bool>()==true?-1:1);
        input["y"]=dz/distance*magnitude*(action.Spec["invertY"]?.GetValue<bool>()==false?1:-1);
    }
    private void Face(Active action,JsonNode state,JsonObject input)
    {
        Point2 target;
        if(action.Motion?.Station is { } station)target=KitchenModel.Position(Entity(state,station.EntityId)?["position"]??throw new InvalidOperationException("Resolved station disappeared while facing."));
        else if(action.Spec["station"] is { } selector)
        {
            var model=KitchenModel.Build(state.AsObject());var position=ChefPosition(state,Player(action.Spec));
            var nearest=model.Candidates(selector.ToString()).OrderBy(s=>s.Position.Distance(position)).FirstOrDefault()??throw new ArgumentException("No station matches "+selector);
            action.Motion=new RouteMotion{Model=model,Station=nearest};target=nearest.Position;
        }
        else
        {
            var source=action.Spec["target"]??(action.Spec["targetEntityId"] is { } id?Entity(state,id.GetValue<int>())?["position"]:null)??throw new ArgumentException("Face needs a station or target.");
            target=KitchenModel.Position(source);
        }
        var chef=Chef(state,Player(action.Spec));var current=ChefPosition(state,Player(action.Spec));
        var direction=new Point2(target.X-current.X,target.Z-current.Z);double distance=direction.Distance(default);
        var forward=KitchenModel.Position(chef["forward"]);
        if(distance>1e-6&&forward.Distance(default)>.5&&(direction.X*forward.X+direction.Z*forward.Z)/distance>.995)return;
        SetMovement(action,input,current,target,action.Spec["faceMagnitude"]?.GetValue<double>()??.15);
    }
    private void Stage(Active action,string stage)
    {
        action.Stage=stage;action.StageStarted=action.Frames;action.StableFrames=0;
        trace?.Event("actionStage",new JsonObject{["player"]=Player(action.Spec),["type"]=action.Spec["type"]?.DeepClone(),["stage"]=stage,["actionFrames"]=action.Frames,["runtimeEntityId"]=action.Motion?.Station?.EntityId});
    }
    private void Transfer(Active action,JsonNode state,JsonObject input,bool take)
    {
        bool combine=action.Spec["type"]?.ToString() is "combine" or "apply" or "assemble";
        bool recoverPlaced=action.Spec["type"]?.ToString()=="assemble"||action.Spec["recoverPlaced"]?.GetValue<bool>()==true;
        int player=Player(action.Spec),held=Held(state,player);
        if(action.Stage=="resolve")
        {
            if(action.Spec["station"] is null)throw new ArgumentException("take/place requires a station selector.");
            if(take&&held!=0)throw new InvalidOperationException("take requires empty hands; held entity "+held);
            if(!take&&held==0)throw new InvalidOperationException((combine?"combine":"place")+" requires a held entity.");
            action.InitialHeld=held;
            if(!InitializeMotion(action,state))return;
            CaptureStationaryTransfer(action,state);
            Stage(action,"navigate");
        }
        if(TryStationaryTransfer(action,state,input,take,combine))return;
        if(action.Stage=="navigate")
        {
            if(DriveNavigation(action,state,input))Stage(action,"face");
            return;
        }
        if(action.Stage=="face")
        {
            Face(action,state,input);
            if(action.Frames-action.StageStarted>=(action.Spec["faceFrames"]?.GetValue<int>()??3))Stage(action,"verify");
            return;
        }
        var station=action.Motion?.Station??throw new InvalidOperationException("Transfer lost its resolved station.");
        if(action.Stage is "recover-plate-ready" or "recover-plate-wait")
        {
            ValidateStationaryTransferResult(action,state,take);
            RecoverPlacedPlate(action,state,input,station);return;
        }
        if(action.Stage=="verify")
        {
            if(NativeTransferTarget(state,station.EntityId,player,take,combine,out string field,out int observed,out var allowed))
            {
                if(take&&!PickupEligible(state,player))return;
                EmitTransferEdge(action,state,input,station,combine,field,observed,allowed);
            }
            else
            {
                if(action.Frames-action.StageStarted<3)Face(action,state,input);
                if(action.Frames-action.StageStarted>=12)RetryTransfer(action,"native "+field+" is "+observed);
            }
            return;
        }
        if(action.Stage=="await-transfer")
        {
            ValidateStationaryTransferResult(action,state,take);
            var allowed=InteractionTargets(state,station.EntityId);
            if(combine)allowed.Add(action.InitialHeld);
            bool changed=action.BeforeAttachment!=(combine?MaterialEvidence(state,allowed):AttachmentEvidence(state,allowed));
            bool delivered=station.Role=="delivery"&&(state["score"]?.GetValue<int>()??0)>action.BeforeScore;
            if(combine&&held!=action.InitialHeld)
            {
                var placed=Entity(state,action.InitialHeld);
                bool isPlate=placed is not null&&KitchenModel.Components(placed).Any(c=>c is "Plate" or "ServerPlate");
                var targetIds=InteractionTargets(state,station.EntityId);
                if(recoverPlaced&&held==0&&isPlate&&targetIds.Contains(action.InitialHeld)&&changed&&CombineExpectation(action,state,allowed))
                {
                    trace?.Event("plateUnderAssemblyObserved",new JsonObject{["player"]=player,["plateId"]=action.InitialHeld,["stationId"]=station.EntityId,["composition"]=placed?["composition"]?.DeepClone(),["contents"]=placed?["contents"]?.DeepClone()});
                    Stage(action,"recover-plate-ready");return;
                }
                throw new InvalidOperationException("Combine did not retain the held entity; expected "+action.InitialHeld+", observed "+held);
            }
            bool combined=combine&&held==action.InitialHeld&&changed&&CombineExpectation(action,state,allowed);
            if(take&&held!=0||!take&&!combine&&held==0&&(changed||delivered)||combined)
            {
                trace?.Event("transferComplete",new JsonObject{["player"]=player,["operation"]=action.Spec["type"]?.DeepClone(),["heldBefore"]=action.InitialHeld,["heldAfter"]=held,["stationId"]=station.EntityId,["stationChanged"]=changed,["delivered"]=delivered,["materialExpectationMatched"]=combined});
                action.Done=true;return;
            }
            if(!take&&held!=0&&held!=action.InitialHeld)throw new InvalidOperationException("Transfer exchanged the held item unexpectedly; stopping with observed entity "+held);
            if(action.Frames-action.StageStarted>=20)
            {
                if(!take&&held==0)throw new InvalidOperationException("Hands emptied without target attachment/content change; transfer is unverified.");
                RetryTransfer(action,"no completed transfer after pickup edge");
            }
        }
    }
    private static bool NativeTransferTarget(JsonNode state,int stationId,int player,bool take,bool combine,
        out string field,out int observed,out HashSet<int> allowed)
    {
        var chef=Chef(state,player);field=take?"pickupTargetId":"placementTargetId";
        observed=chef[field]?.GetValue<int>()??0;allowed=InteractionTargets(state,stationId);
        if(combine&&(!allowed.Contains(observed)||observed==0)){field="pickupTargetId";observed=chef[field]?.GetValue<int>()??0;}
        return allowed.Contains(observed)&&observed!=0;
    }
    private void EmitTransferEdge(Active action,JsonNode state,JsonObject input,KitchenStation station,bool combine,
        string field,int observed,HashSet<int> allowed)
    {
        int player=Player(action.Spec),held=Held(state,player);
        if(combine)allowed.Add(held);
        action.BeforeAttachment=combine?MaterialEvidence(state,allowed):AttachmentEvidence(state,allowed);
        action.BeforeScore=state["score"]?.GetValue<int>()??0;input["pickup"]=true;
        trace?.Event("interactionTargetVerified",new JsonObject{["player"]=player,["field"]=field,["targetId"]=observed,["resolvedStationId"]=station.EntityId,["heldBefore"]=held});
        Stage(action,"await-transfer");
    }
    private void RetryTransfer(Active action,string reason)
    {
        action.Retries++;
        trace?.Event("interactionRetry",new JsonObject{["player"]=Player(action.Spec),["retry"]=action.Retries,["reason"]=reason,["actionFrames"]=action.Frames});
        if(action.Retries>(action.Spec["maxRetries"]?.GetValue<int>()??6))throw new InvalidOperationException("Interaction retries exhausted: "+reason);
        action.Motion=null;Stage(action,"resolve");
    }
    private static bool PickupEligible(JsonNode state,int player)
    {
        var chef=Chef(state,player);
        var now=state["clientTime"]??state["logicalTime"];
        // Despite its name, the native field stores NEXT eligible ClientTime.
        return now is null||chef["lastPickupTimestamp"] is null||Number(now)>=Number(chef["lastPickupTimestamp"]);
    }
    private void RecoverPlacedPlate(Active action,JsonNode state,JsonObject input,KitchenStation station)
    {
        int player=Player(action.Spec),held=Held(state,player);
        var ids=InteractionTargets(state,station.EntityId);ids.Add(action.InitialHeld);
        if(held==action.InitialHeld)
        {
            if(!CombineExpectation(action,state,ids))throw new InvalidOperationException("Recovered plate does not match the requested assembly result.");
            trace?.Event("assemblyRecovered",new JsonObject{["player"]=player,["plateId"]=held,["stationId"]=station.EntityId,["retries"]=action.Retries,["composition"]=Entity(state,held)?["composition"]?.DeepClone()});
            action.Done=true;return;
        }
        if(held!=0)throw new InvalidOperationException("Assembly recovery picked up another entity instead of the original plate.");
        if(!InteractionTargets(state,station.EntityId).Contains(action.InitialHeld))throw new InvalidOperationException("Placed assembly plate left the verified station before recovery.");
        if(action.Stage=="recover-plate-wait")
        {
            if(action.Frames-action.StageStarted<8)return;
            action.Retries++;
            if(action.Retries>(action.Spec["maxRetries"]?.GetValue<int>()??6))throw new InvalidOperationException("Original assembly plate could not be recovered after legal pickup retries.");
            trace?.Event("assemblyRecoveryRetry",new JsonObject{["player"]=player,["plateId"]=action.InitialHeld,["retry"]=action.Retries});
            Stage(action,"recover-plate-ready");return;
        }
        // This stage supplies at least two released frames after placement or a
        // missed edge, and additionally respects the native cooldown deadline.
        if(action.Frames-action.StageStarted<2||!PickupEligible(state,player))return;
        int observed=Chef(state,player)["pickupTargetId"]?.GetValue<int>()??0;
        if(observed!=0&&InteractionTargets(state,station.EntityId).Contains(observed))
        {
            input["pickup"]=true;
            trace?.Event("assemblyRecoveryPickup",new JsonObject{["player"]=player,["plateId"]=action.InitialHeld,["nativeTargetId"]=observed,["nextEligibleClientTime"]=Chef(state,player)["lastPickupTimestamp"]?.DeepClone()});
            Stage(action,"recover-plate-wait");
        }
        else
        {
            if(action.Frames-action.StageStarted<5)Face(action,state,input);
            if(action.Frames-action.StageStarted>30)throw new InvalidOperationException("Original assembly plate has no valid native pickup target.");
        }
    }
    private static HashSet<int> InteractionTargets(JsonNode state,int stationId)
    {
        var entities=(state["entities"] as JsonArray)?.OfType<JsonObject>().ToArray()??[];
        var ids=new HashSet<int>{stationId};
        for(int pass=0;pass<2;pass++)foreach(var e in entities)
        {
            int id=e["id"]?.GetValue<int>()??0,attached=e["attachedEntityId"]?.GetValue<int>()??0;
            if(ids.Contains(id)&&attached!=0)ids.Add(attached);
            if(attached!=0&&ids.Contains(attached))ids.Add(id);
        }
        return ids;
    }
    private static string AttachmentEvidence(JsonNode state,HashSet<int> ids)
    {
        var evidence=new JsonArray();
        foreach(int id in ids.Order())
        {
            var entity=Entity(state,id);if(entity is null)continue;
            evidence.Add(new JsonObject{["id"]=id,["attached"]=entity["attachedEntityId"]?.DeepClone(),["contents"]=entity["contents"]?.DeepClone(),["composition"]=entity["composition"]?.DeepClone(),["ingredients"]=entity["ingredientIds"]?.DeepClone(),["plateCount"]=entity["plateCount"]?.DeepClone()});
        }
        return evidence.ToJsonString();
    }
    private static string MaterialEvidence(JsonNode state,HashSet<int> ids)
    {
        JsonNode? Shape(JsonNode? node)
        {
            if(node is JsonObject obj)
            {
                var result=new JsonObject();
                foreach(var property in obj.Where(p=>p.Key is not("progress" or "state")).OrderBy(p=>p.Key,StringComparer.Ordinal))result[property.Key]=Shape(property.Value);
                return result;
            }
            if(node is JsonArray array)return new JsonArray(array.Select(Shape).ToArray());
            return node?.DeepClone();
        }
        var evidence=new JsonArray();
        foreach(int id in ids.Order())
        {
            var entity=Entity(state,id);if(entity is null)continue;
            evidence.Add(new JsonObject{["id"]=id,["attached"]=entity["attachedEntityId"]?.DeepClone(),["ingredients"]=entity["ingredientIds"]?.DeepClone(),["contents"]=Shape(entity["contents"]),["composition"]=Shape(entity["composition"])});
        }
        return evidence.ToJsonString();
    }
    private static bool CombineExpectation(Active action,JsonNode state,HashSet<int> ids)
    {
        if(action.Spec["expectedRecipeId"] is { } recipe)
            if(!CarnivalRecipes.MatchRecipe(Entity(state,action.InitialHeld),recipe.GetValue<int>()).ObservableMatch)return false;
        var expected=new List<int>();
        if(action.Spec["expectedIngredientId"] is { } single)expected.Add(single.GetValue<int>());
        if(action.Spec["expectedIngredientIds"] is JsonArray many)expected.AddRange(many.Select(n=>n!.GetValue<int>()));
        if(action.Spec["expectedIngredient"] is { } named)
        {
            var ingredient=CarnivalRecipes.Ingredients.Values.SingleOrDefault(i=>i.Name.Equals(named.ToString(),StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Unknown expected ingredient "+named);
            expected.Add(ingredient.Id);
        }
        var observed=ids.Select(id=>CarnivalRecipes.ClassifyEntity(Entity(state,id)).Food).SelectMany(f=>f.IngredientIds).ToHashSet();
        return expected.All(observed.Contains);
    }
}
