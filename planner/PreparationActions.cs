using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    private sealed class StationCycle
    {
        public int OutputEntityId,Requested,PreviousDirty,PreviousClean,DirtyRemoved,CleanAdded;
        public int InitialHeld,ExpectedIndex;
        public bool WashingInitialized;
        public int NoInteractorFrames,ReacquireAttempts;
        public int UnsuppressStage,UnsuppressStarted;
        public Active? Retreat;
    }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Active,StationCycle> stationCycles=new();

    private void Wash(Active action,JsonNode state,JsonObject input)
    {
        int player=Player(action.Spec);var cycle=stationCycles.GetOrCreateValue(action);
        if(Held(state,player)!=0)throw new InvalidOperationException("wash requires empty hands; place the dirty stack into the sink first.");
        if(action.Stage=="resolve")
        {
            action.Spec["station"]??="sink";
            if(!InitializeMotion(action,state))return;
            if(action.Motion!.Station!.Role!="sink")throw new ArgumentException("wash requires a native washing station.");
            Stage(action,"navigate");
        }
        if(action.Stage=="navigate")
        {
            if(DriveNavigation(action,state,input))Stage(action,"face");return;
        }
        if(action.Stage=="face")
        {
            Face(action,state,input);
            if(action.Frames-action.StageStarted>=(action.Spec["faceFrames"]?.GetValue<int>()??3))Stage(action,"wash-ready");return;
        }
        var station=action.Motion!.Station!;
        var sink=Entity(state,station.EntityId)??throw new InvalidOperationException("Resolved sink disappeared.");
        if(action.Stage is "wash-retreat" or "wash-clear-target")
        {
            ReacquireWashingTarget(action,cycle,state,input,station);return;
        }
        if(cycle.UnsuppressStage>0)
        {
            if(cycle.UnsuppressStage==1){cycle.UnsuppressStage=2;return;}
            if(Chef(state,player)["useSuppressed"]?.GetValue<bool>()==false)
            {
                cycle.UnsuppressStage=0;cycle.NoInteractorFrames=0;action.LastProgressFrame=action.Frames;
                trace?.Event("washingUseSuppressionCleared",new JsonObject{["player"]=player,["sinkId"]=station.EntityId,["actionFrames"]=action.Frames});
                return;
            }
            if(action.Frames-cycle.UnsuppressStarted>8)throw new InvalidOperationException("Native use suppression did not clear after a legal press and release at the verified sink.");
            return;
        }
        if(action.Stage=="wash-ready")
        {
            if(!cycle.WashingInitialized)
            {
                cycle.PreviousDirty=sink["plateCount"]?.GetValue<int>()??0;
                cycle.Requested=action.Spec["count"]?.GetValue<int>()??cycle.PreviousDirty;
                if(cycle.Requested<=0||cycle.Requested>cycle.PreviousDirty)throw new InvalidOperationException("Sink must contain at least the requested positive dirty plate count.");
                cycle.OutputEntityId=sink["washingOutputEntityId"]?.GetValue<int>()??0;
                if(cycle.OutputEntityId==0)throw new InvalidOperationException("wash requires native washingOutputEntityId telemetry; clean-output inference is insufficient.");
                var output=Entity(state,cycle.OutputEntityId)??throw new InvalidOperationException("Native drying output is absent.");
                cycle.PreviousClean=output["plateCount"]?.GetValue<int>()??0;
                cycle.WashingInitialized=true;
            }
            cycle.NoInteractorFrames=0;
            action.LastProgressFrame=action.Frames;action.LastWorkProgress=Number(sink["washingProgress"]);
            trace?.Event("washingSelected",new JsonObject{["sinkId"]=station.EntityId,["outputId"]=cycle.OutputEntityId,["dirtyCount"]=cycle.PreviousDirty,["cleanCount"]=cycle.PreviousClean,["requested"]=cycle.Requested,["duration"]=sink["washingTime"]?.DeepClone()});
            Stage(action,"washing");
        }
        int dirty=sink["plateCount"]?.GetValue<int>()??0;
        var drying=Entity(state,cycle.OutputEntityId)??throw new InvalidOperationException("Native drying output disappeared.");
        int clean=drying["plateCount"]?.GetValue<int>()??0;
        if(dirty<cycle.PreviousDirty)
        {
            cycle.DirtyRemoved+=cycle.PreviousDirty-dirty;action.LastProgressFrame=action.Frames;
            action.LastWorkProgress=-1;
        }
        if(clean>cycle.PreviousClean)
        {
            cycle.CleanAdded+=clean-cycle.PreviousClean;action.LastProgressFrame=action.Frames;
        }
        if(dirty!=cycle.PreviousDirty||clean!=cycle.PreviousClean)
            trace?.Event("washingPlateCounts",new JsonObject{["sinkId"]=station.EntityId,["outputId"]=cycle.OutputEntityId,["dirtyCount"]=dirty,["cleanCount"]=clean,["dirtyRemoved"]=cycle.DirtyRemoved,["cleanAdded"]=cycle.CleanAdded,["actionFrames"]=action.Frames});
        cycle.PreviousDirty=dirty;cycle.PreviousClean=clean;
        if(cycle.DirtyRemoved>=cycle.Requested&&cycle.CleanAdded>=cycle.Requested)
        {
            trace?.Event("preparationComplete",new JsonObject{["operation"]="wash",["sinkId"]=station.EntityId,["outputId"]=cycle.OutputEntityId,["washedCount"]=cycle.Requested,["dirtyRemoved"]=cycle.DirtyRemoved,["cleanAdded"]=cycle.CleanAdded});
            action.Done=true;return;
        }
        ObserveProgress(action,state,"wash",station.EntityId,Number(sink["washingProgress"]),Number(sink["washingTime"]));
        if(cycle.DirtyRemoved>=cycle.Requested||dirty==0)return;
        var chef=Chef(state,player);var allowed=InteractionTargets(state,station.EntityId);
        int target=chef["useTargetId"]?.GetValue<int>()??0,interacting=chef["interactingEntityId"]?.GetValue<int>()??0;
        if(target!=0&&allowed.Contains(target)||interacting!=0&&allowed.Contains(interacting))
        {
            input["use"]=true;
            if(chef["useSuppressed"]?.GetValue<bool>()==true)
            {
                cycle.UnsuppressStage=1;cycle.UnsuppressStarted=action.Frames;
                trace?.Event("washingUseSuppressionGuard",new JsonObject{["player"]=player,["sinkId"]=station.EntityId,["reason"]="native ClearEvents suppressed held use; emit a legal press/release before washing",["actionFrames"]=action.Frames});
                return;
            }
            int serverInteracting=(chef["serverInteractionId"]??chef["interactingEntityId"])?.GetValue<int>()??0;
            if(Number(sink["washingProgress"])<=.0001&&serverInteracting==0)cycle.NoInteractorFrames++;else cycle.NoInteractorFrames=0;
            if(cycle.NoInteractorFrames>=(action.Spec["reacquireAfterFrames"]?.GetValue<int>()??20))
            {
                input["use"]=false;
                BeginWashingReacquire(action,cycle,state,station);
            }
        }
        else if(action.Frames-action.LastProgressFrame<6)Face(action,state,input);
        else if(action.Frames-action.LastProgressFrame>30)throw new InvalidOperationException("Native washing target was lost before the requested clean plates appeared.");
    }

    private void BeginWashingReacquire(Active action,StationCycle cycle,JsonNode state,KitchenStation station)
    {
        if(++cycle.ReacquireAttempts>(action.Spec["maxReacquireAttempts"]?.GetValue<int>()??2))throw new InvalidOperationException("Washing target reacquisition exhausted after native interaction repeatedly failed to begin.");
        var model=KitchenModel.Build(state.AsObject());int player=Player(action.Spec);var position=ChefPosition(state,player);
        var away=new Point2(position.X-station.Position.X,position.Z-station.Position.Z);double length=away.Distance(default);
        if(length<.01)throw new InvalidOperationException("Cannot derive a legal retreat direction from the sink.");
        away=new(away.X/length,away.Z/length);var paths=new List<NavigationPath>();
        foreach(double distance in new[]{1.2,1.5,1.8})foreach(double degrees in new[]{0d,-30d,30d,-60d,60d})
        {
            double angle=degrees*Math.PI/180,dx=away.X*Math.Cos(angle)-away.Z*Math.Sin(angle),dz=away.X*Math.Sin(angle)+away.Z*Math.Cos(angle);
            var point=new Point2(position.X+dx*distance,position.Z+dz*distance);
            if(point.Distance(station.Position)<1.9)continue;
            var path=Navigation.FindPath(model,position,point,.15,OtherChefs(state,player,model,false));
            if(path.Success)paths.Add(path);
        }
        var selected=paths.OrderBy(p=>p.Length).FirstOrDefault()??throw new InvalidOperationException("No collision-free sink retreat exists to clear stale native interaction prediction.");
        var destination=selected.Points[^1];
        cycle.Retreat=new Active(new JsonObject{["type"]="navigate",["player"]=player,["target"]=new JsonObject{["x"]=destination.X,["z"]=destination.Z},["dash"]=false,["maxNoPathFrames"]=90});
        var chef=Chef(state,player);
        trace?.Event("washingReacquire",new JsonObject{["reason"]="verified use held without native washing progress or server interaction",["sinkId"]=station.EntityId,["attempt"]=cycle.ReacquireAttempts,["useTargetId"]=chef["useTargetId"]?.DeepClone(),["clientPredictedInteractionId"]=chef["clientPredictedInteractionId"]?.DeepClone(),["serverInteractionId"]=chef["serverInteractionId"]?.DeepClone(),["heldUseFrames"]=cycle.NoInteractorFrames,["retreatPath"]=JsonSerializer.SerializeToNode(selected)});
        Stage(action,"wash-retreat");
    }
    private void ReacquireWashingTarget(Active action,StationCycle cycle,JsonNode state,JsonObject input,KitchenStation station)
    {
        if(action.Stage=="wash-retreat")
        {
            var retreat=cycle.Retreat??throw new InvalidOperationException("Washing retreat state is absent.");
            if(retreat.Frames++>180)throw new TimeoutException("Legal washing retreat did not complete.");
            if(DriveNavigation(retreat,state,input))Stage(action,"wash-clear-target");
            return;
        }
        var chef=Chef(state,Player(action.Spec));var allowed=InteractionTargets(state,station.EntityId);
        int target=chef["useTargetId"]?.GetValue<int>()??0,predicted=chef["clientPredictedInteractionId"]?.GetValue<int>()??0,server=chef["serverInteractionId"]?.GetValue<int>()??0;
        if(!allowed.Contains(target)&&!allowed.Contains(predicted)&&!allowed.Contains(server))
        {
            if(++action.StableFrames>=2)
            {
                trace?.Event("washingTargetCleared",new JsonObject{["sinkId"]=station.EntityId,["attempt"]=cycle.ReacquireAttempts,["actionFrames"]=action.Frames});
                action.Motion!.Points=[];action.Motion.LastPlanFrame=action.Frames-10;cycle.Retreat=null;
                action.LastProgressFrame=action.Frames;Stage(action,"navigate");
            }
        }
        else action.StableFrames=0;
        if(action.Frames-action.StageStarted>30)throw new InvalidOperationException("Retreat did not clear the native sink target and predicted interaction.");
    }

    private void SwitchCondiment(Active action,JsonNode state,JsonObject input)
    {
        int player=Player(action.Spec);var cycle=stationCycles.GetOrCreateValue(action);
        if(action.Stage=="resolve")
        {
            cycle.InitialHeld=Held(state,player);
            if(cycle.InitialHeld!=0)
                throw new InvalidOperationException("switch-condiment requires empty hands; native controls do not scan world use targets while carrying an item.");
            var model=KitchenModel.Build(state.AsObject());
            var dispenser=model.Resolve(action.Spec["dispenser"]?.ToString()??"condiment");
            cycle.OutputEntityId=dispenser.EntityId;
            int initial=Entity(state,dispenser.EntityId)?["switchIndex"]?.GetValue<int>()??-1;
            if(initial is <0 or >1)throw new InvalidOperationException("Condiment dispenser must expose native switchIndex 0 or 1.");
            cycle.ExpectedIndex=(action.Spec["expectedIndex"]??action.Spec["index"])?.GetValue<int>()??1-initial;
            if(cycle.ExpectedIndex is <0 or >1)throw new ArgumentException("Condiment index must be 0 or 1.");
            if(initial==cycle.ExpectedIndex){action.Done=true;return;}
            action.Spec["station"]??="condiment-switch";
            if(!InitializeMotion(action,state))return;
            if(action.Motion!.Station!.Role!="condiment-switch")throw new InvalidOperationException("Selected switch is not the validated condiment switch.");
            Stage(action,"navigate");
        }
        if(Held(state,player)!=0)throw new InvalidOperationException("Chef picked up an item while switching condiment; empty hands are required.");
        if(action.Stage=="navigate")
        {
            if(DriveNavigation(action,state,input))Stage(action,"face");return;
        }
        if(action.Stage=="face")
        {
            Face(action,state,input);
            if(action.Frames-action.StageStarted>=(action.Spec["faceFrames"]?.GetValue<int>()??3))Stage(action,"switch-ready");return;
        }
        var station=action.Motion!.Station!;
        var dispenserNow=Entity(state,cycle.OutputEntityId)??throw new InvalidOperationException("Condiment dispenser disappeared.");
        int observed=dispenserNow["switchIndex"]?.GetValue<int>()??-1;
        if(observed is <0 or >1)throw new InvalidOperationException("Native condiment switch index became unavailable.");
        if(observed==cycle.ExpectedIndex)
        {
            if(++action.StableFrames>=2)
            {
                trace?.Event("condimentSwitchComplete",new JsonObject{["player"]=player,["switchId"]=station.EntityId,["dispenserId"]=cycle.OutputEntityId,["switchIndex"]=observed,["heldEntityId"]=cycle.InitialHeld});
                action.Done=true;
            }
            return;
        }
        action.StableFrames=0;
        if(action.Stage=="switch-wait")
        {
            if(action.Frames-action.StageStarted<12)return;
            if(++action.Retries>(action.Spec["maxRetries"]?.GetValue<int>()??3))throw new InvalidOperationException("Condiment switch did not change after verified use edges.");
            Stage(action,"switch-ready");return;
        }
        int target=Chef(state,player)["useTargetId"]?.GetValue<int>()??0;
        if(target!=0&&InteractionTargets(state,station.EntityId).Contains(target))
        {
            input["use"]=true;Stage(action,"switch-wait");
            trace?.Event("condimentSwitchEdge",new JsonObject{["player"]=player,["targetId"]=target,["dispenserId"]=cycle.OutputEntityId,["expectedIndex"]=cycle.ExpectedIndex,["retry"]=action.Retries});
        }
        else if(action.Frames-action.StageStarted<6)Face(action,state,input);
        else if(action.Frames-action.StageStarted>30)throw new InvalidOperationException("Native condiment-switch use target is absent.");
    }

    private void Chop(Active action,JsonNode state,JsonObject input)
    {
        int player=Player(action.Spec);
        if(action.Stage=="resolve")
        {
            if(Held(state,player)!=0)throw new InvalidOperationException("chop requires empty hands.");
            action.Spec["station"]??="chopboards";
            if(!InitializeMotion(action,state))return;
            var board=action.Motion!.Station!;
            if(board.Role!="chop")throw new InvalidOperationException("chop requires a chopping workstation.");
            int attached=Entity(state,board.EntityId)?["attachedEntityId"]?.GetValue<int>()??0;
            if(attached==0)throw new InvalidOperationException("Selected board is empty.");
            action.InitialWorkEntityId=attached;action.WorkEntityId=attached;action.LastProgressFrame=action.Frames;
            Stage(action,"navigate");
        }
        if(action.Stage=="navigate")
        {
            if(DriveNavigation(action,state,input))Stage(action,"face");return;
        }
        if(action.Stage=="face")
        {
            Face(action,state,input);
            if(action.Frames-action.StageStarted>=(action.Spec["faceFrames"]?.GetValue<int>()??3))Stage(action,"chopping");
            return;
        }
        var station=action.Motion!.Station!;
        var cycle=stationCycles.GetOrCreateValue(action);
        if(cycle.UnsuppressStage>0)
        {
            if(cycle.UnsuppressStage==1){cycle.UnsuppressStage=2;return;}
            if(Chef(state,player)["useSuppressed"]?.GetValue<bool>()==false)
            {
                cycle.UnsuppressStage=0;action.LastProgressFrame=action.Frames;
                trace?.Event("choppingUseSuppressionCleared",new JsonObject{["player"]=player,["boardId"]=station.EntityId,["actionFrames"]=action.Frames});
                return;
            }
            if(action.Frames-cycle.UnsuppressStarted>8)throw new InvalidOperationException("Native use suppression did not clear after a legal press and release at the verified chopping board.");
            return;
        }
        int itemId=Entity(state,station.EntityId)?["attachedEntityId"]?.GetValue<int>()??0;
        if(itemId==0)
        {
            // Native chopping replaces the raw entity. Allow a short empty
            // attachment interval without treating it as successful chopping.
            if(action.Frames-action.LastProgressFrame>30)throw new InvalidOperationException("Chopping board lost its item before completion.");
            return;
        }
        var item=Entity(state,itemId);
        if(item is null)return;
        action.WorkEntityId=itemId;
        var food=CarnivalRecipes.ClassifyEntity(item).Food;
        bool isRawWorkable=KitchenModel.Components(item).Any(c=>c is "WorkableItem" or "ServerWorkableItem");
        bool prepared=!isRawWorkable&&food.DescendantsAndSelf().Any(n=>n.Kind==FoodNodeKind.Ingredient&&n.Preparation==FoodPreparation.Chopped);
        if(prepared)
        {
            action.StableFrames++;
            if(action.StableFrames>=2)
            {
                trace?.Event("preparationComplete",new JsonObject{["operation"]="chop",["boardId"]=station.EntityId,["rawEntityId"]=action.InitialWorkEntityId,["preparedEntityId"]=itemId,["food"]=JsonSerializer.SerializeToNode(food)});
                action.Done=true;
            }
            return;
        }
        action.StableFrames=0;
        if(!isRawWorkable)throw new InvalidOperationException("Board item is neither workable raw food nor observably chopped food.");
        double progress=item["workProgress"] is null?-1:Number(item["workProgress"]);
        ObserveProgress(action,state,"chop",itemId,progress,1);
        var chef=Chef(state,player);int target=chef["useTargetId"]?.GetValue<int>()??0,interacting=chef["interactingEntityId"]?.GetValue<int>()??0;
        var allowed=InteractionTargets(state,station.EntityId);
        if(allowed.Contains(target)&&target!=0||allowed.Contains(interacting)&&interacting!=0)
        {
            input["use"]=true;
            if(chef["useSuppressed"]?.GetValue<bool>()==true)
            {
                cycle.UnsuppressStage=1;cycle.UnsuppressStarted=action.Frames;
                trace?.Event("choppingUseSuppressionGuard",new JsonObject{["player"]=player,["boardId"]=station.EntityId,["reason"]="native use suppression requires a legal press/release before held chopping",["actionFrames"]=action.Frames});
            }
        }
        else if(action.Frames-action.StageStarted<6)Face(action,state,input);
        else if(action.Frames-action.LastProgressFrame>30)RetryTransfer(action,"chopping interaction target is absent");
    }

    // Cooking and mixing already proceed through the native station handlers.
    // These actions observe the chosen vessel and emit neutral input only.
    private void WaitForPreparation(Active action,JsonNode state,string operation)
    {
        bool cooking=operation=="cook";
        if(action.Stage=="resolve")
        {
            var selector=action.Spec["station"]?.ToString();
            var entities=(state["entities"] as JsonArray)?.OfType<JsonObject>().ToArray()??[];
            IEnumerable<JsonObject> candidates;
            if((action.Spec["entityId"]??action.Spec["targetEntityId"]) is { } explicitId)
                candidates=[Entity(state,explicitId.GetValue<int>())??throw new InvalidOperationException("Preparation vessel is absent.")];
            else
            {
                if(selector is null)throw new ArgumentException(operation+" needs station or entityId.");
                var model=KitchenModel.Build(state.AsObject());
                var stations=model.Candidates(selector);
                candidates=stations.Select(s=>Entity(state,s.EntityId)).Where(e=>e is not null).SelectMany(e=>
                {
                    var attached=e!["attachedEntityId"]?.GetValue<int>()??0;
                    return attached==0?new[]{e!}:new[]{e!,Entity(state,attached)}.Where(x=>x is not null).Select(x=>x!);
                });
            }
            string progressField=cooking?"cookingProgress":"mixingProgress";
            var vessel=candidates.Where(e=>e[progressField] is not null&&Number(e[progressField])>=0&&(e["contents"] as JsonArray)?.Count>0).DistinctBy(e=>e["id"]!.GetValue<int>()).OrderBy(e=>KitchenModel.Position(e["position"]).Distance(ChefPosition(state,Player(action.Spec)))).FirstOrDefault()
                ??throw new InvalidOperationException("Selected preparation target has no nonempty vessel with native "+progressField+" telemetry.");
            action.WorkEntityId=vessel["id"]!.GetValue<int>();action.LastProgressFrame=action.Frames;Stage(action,"waiting-"+operation);
            trace?.Event("preparationSelected",new JsonObject{["operation"]=operation,["entityId"]=action.WorkEntityId,["contents"]=vessel["contents"]?.DeepClone()});
        }
        var entity=Entity(state,action.WorkEntityId)??throw new InvalidOperationException("Watched vessel disappeared before preparation completed.");
        if((entity["contents"] as JsonArray)?.Count is null or 0)throw new InvalidOperationException("Watched vessel emptied before preparation completed.");
        var observation=CarnivalRecipes.ClassifyEntity(entity);
        if(observation.Food.IsRuined)throw new InvalidOperationException("Watched food became burnt or overmixed.");
        var expectedKind=cooking?FoodNodeKind.Cooked:FoodNodeKind.Mixed;
        var expectedPreparation=cooking?FoodPreparation.Cooked:FoodPreparation.Mixed;
        // The native mixer bowl carries an outer raw cooking wrapper for its
        // subsequent fryer step. Its completed mixing stage is a descendant.
        var stages=(cooking?new[]{observation.Food}:observation.Food.DescendantsAndSelf()).Where(n=>n.Kind==expectedKind).ToArray();
        bool complete=stages.Length>0&&stages.All(n=>n.Preparation==expectedPreparation);
        double progress=Number(entity[cooking?"cookingProgress":"mixingProgress"]),duration=Number(entity[cooking?"cookingTime":"mixingTime"]);
        if(complete)
        {
            trace?.Event("preparationComplete",new JsonObject{["operation"]=operation,["entityId"]=action.WorkEntityId,["progress"]=progress,["duration"]=duration,["food"]=JsonSerializer.SerializeToNode(observation.Food)});
            action.Done=true;return;
        }
        ObserveProgress(action,state,operation,action.WorkEntityId,progress,duration);
    }
    private void ObserveProgress(Active action,JsonNode state,string operation,int entityId,double progress,double duration)
    {
        if(progress>action.LastWorkProgress+.0001)
        {
            action.LastWorkProgress=progress;action.LastProgressFrame=action.Frames;
            if(action.Frames%30==0)trace?.Event("preparationProgress",new JsonObject{["operation"]=operation,["entityId"]=entityId,["progress"]=progress,["duration"]=duration,["actionFrames"]=action.Frames});
        }
        if(action.Frames-action.LastProgressFrame>(action.Spec["maxStallFrames"]?.GetValue<int>()??300))
        {
            trace?.Event("preparationStalled",new JsonObject{["operation"]=operation,["entityId"]=entityId,["progress"]=progress,["applicationFocused"]=state["applicationFocused"]?.DeepClone(),["canAcceptInput"]=Chef(state,Player(action.Spec))["canAcceptInput"]?.DeepClone(),["snapshotWarnings"]=state["warnings"]?.DeepClone()});
            throw new TimeoutException(operation+" stopped making observable native progress.");
        }
    }
}
