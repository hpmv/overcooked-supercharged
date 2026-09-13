using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

// Evidence from consecutive native observations, never from requested buttons.
// The collision claim is explicitly a measured contact/blocked-motion inference;
// it is not a fabricated OnCollision callback event.
public sealed class NativeProbeCoverage
{
    public static readonly string[] Required=["heldItem","workProgress","cookedThreshold","mixedThreshold","aimReleaseWithEmptyHands","nativeChefCatch","bothCannonFlightsEnded","nativePortalTravel","nativeWashedPlate","nativeDashCompleted","solidContactBlockedMotion","nativeSuccessfulDelivery"];
    private readonly Dictionary<string,JsonObject> evidence=[];
    private Dictionary<int,JsonObject> oldChefs=[],oldEntities=[];
    private readonly Dictionary<int,(long Frame,int Thrower)> flightItems=[];
    private readonly HashSet<int> cannonIds=[],finishedCannons=[];
    private readonly Dictionary<int,(long Frame,int Passenger,Point2 Target)> cannonFlights=[];
    private readonly Dictionary<int,(long Frame,int Receiver,int Player,Point2 Origin,bool Received)> portalFlights=[];
    private readonly Dictionary<int,(long Frame,int Output,int Dirty,int Clean,double Progress)> washing=[];
    private readonly Dictionary<int,(long Frame,Point2 Start,double PeakSpeed)> dashes=[];
    private readonly Dictionary<(int Player,int Obstacle),int> blockedCounts=[];
    private long previousFrame=-1;
    private static int I(JsonNode? n,string field)=>n?[field]?.GetValue<int>()??0;
    private static double N(JsonNode? n,string field)=>n?[field] is JsonValue v&&v.TryGetValue<double>(out var x)?x:double.NaN;
    private static bool B(JsonNode? n,string field)=>n?[field]?.GetValue<bool>()==true;
    private static Point2 P(JsonNode? n,string field)=>KitchenModel.Position(n?[field]);
    private void Mark(string name,long frame,JsonObject detail)
    {
        if(evidence.ContainsKey(name))return;
        detail["gameplayFrame"]=frame;evidence[name]=detail;
    }
    public void Observe(JsonObject state)
    {
        long frame=state["gameplayFrame"]?.GetValue<long>()??-1;
        if(frame<0)throw new InvalidDataException("Probe coverage requires native gameplay frame observations.");
        if(previousFrame>=0&&frame<previousFrame)throw new InvalidDataException("Probe coverage crossed a native round restart.");
        if(frame==previousFrame)return;
        if(previousFrame>=0&&frame!=previousFrame+1)throw new InvalidDataException("Probe coverage requires every completed gameplay frame.");
        var chefs=(state["chefs"] as JsonArray??[]).OfType<JsonObject>().ToDictionary(c=>I(c,"playerId"));
        var entities=(state["entities"] as JsonArray??[]).OfType<JsonObject>().ToDictionary(e=>I(e,"id"));
        foreach(var (id,entity) in entities)
        {
            oldEntities.TryGetValue(id,out var old);
            if(KitchenModel.Components(entity).Contains("Cannon"))cannonIds.Add(id);
            if(B(entity,"throwFlying")&&I(entity,"throwerEntityId")!=0)flightItems[id]=(frame,I(entity,"throwerEntityId"));
            if(old is not null&&(I(entity,"workStage")>I(old,"workStage")||I(entity,"workSubStage")>I(old,"workSubStage")))Mark("workProgress",frame,new(){["entityId"]=id,["workStage"]=I(entity,"workStage"),["workSubStage"]=I(entity,"workSubStage")});
            foreach(var (kind,time,progress) in new[]{("cookedThreshold","cookingTime","cookingProgress"),("mixedThreshold","mixingTime","mixingProgress")})
                if(old is not null&&N(entity,time)>0&&N(entity,progress)>=N(entity,time)&&N(old,progress)<N(entity,time))
                    Mark(kind,frame,new(){["entityId"]=id,["beforeProgress"]=N(old,progress),["progress"]=N(entity,progress),["nativeDuration"]=N(entity,time)});
            if(cannonIds.Contains(id)&&B(entity,"cannonFlying")&&!cannonFlights.ContainsKey(id)
                &&chefs.Values.Any(c=>I(c,"entityId")==I(entity,"cannonLoadedEntityId")))
                cannonFlights[id]=(frame,I(entity,"cannonLoadedEntityId"),P(entity,"cannonTarget"));
            if(cannonFlights.TryGetValue(id,out var flight)&&!B(entity,"cannonFlying")&&frame>flight.Frame)
            {
                var passenger=chefs.Values.SingleOrDefault(c=>I(c,"entityId")==flight.Passenger);
                if(passenger is not null&&B(passenger,"controlsEnabled")&&P(passenger,"position").Distance(flight.Target)<1.5)
                {
                    finishedCannons.Add(id);
                    Mark("cannonFlight:"+id,frame,new(){["cannonId"]=id,["passengerEntityId"]=flight.Passenger,["startFrame"]=flight.Frame,["endPosition"]=passenger["position"]!.DeepClone()});
                }
            }
            if(I(entity,"portalSenderCount")>0&&B(entity,"portalTeleporting")&&!B(old,"portalTeleporting")
                &&entities.TryGetValue(I(entity,"portalDestinationId"),out var receiver)&&I(receiver,"portalReceiverCount")>0)
            {
                var nearby=chefs.Values.Where(c=>P(c,"position").Distance(P(entity,"position"))<1.2).ToArray();
                if(nearby.Length==1)portalFlights[id]=(frame,I(receiver,"id"),I(nearby[0],"playerId"),P(nearby[0],"position"),false);
            }
            if(N(entity,"washingTime")>0&&N(entity,"washingProgress")>0&&I(entity,"plateCount")>0
                &&entities.TryGetValue(I(entity,"washingOutputEntityId"),out var output))
            {
                if(!washing.TryGetValue(id,out var wash))washing[id]=(frame,I(output,"id"),I(entity,"plateCount"),I(output,"plateCount"),N(entity,"washingProgress"));
                else washing[id]=wash with{Progress=Math.Max(wash.Progress,N(entity,"washingProgress"))};
            }
            if(washing.TryGetValue(id,out var washingStart)&&I(entity,"plateCount")<washingStart.Dirty
                &&entities.TryGetValue(washingStart.Output,out var drying)&&I(drying,"plateCount")>washingStart.Clean&&washingStart.Progress>0)
                Mark("nativeWashedPlate",frame,new(){["sinkId"]=id,["outputId"]=washingStart.Output,["startFrame"]=washingStart.Frame,["observedProgress"]=washingStart.Progress,["dirtyBefore"]=washingStart.Dirty,["dirtyAfter"]=I(entity,"plateCount"),["cleanBefore"]=washingStart.Clean,["cleanAfter"]=I(drying,"plateCount")});
        }
        if(cannonIds.Count==2&&finishedCannons.IsSupersetOf(cannonIds))Mark("bothCannonFlightsEnded",frame,new(){["nativeCannonIds"]=new JsonArray(cannonIds.Order().Select(id=>(JsonNode)JsonValue.Create(id)!).ToArray())});
        foreach(var (senderId,trip) in portalFlights.ToArray())
        {
            if(!entities.TryGetValue(trip.Receiver,out var receiver)||!chefs.TryGetValue(trip.Player,out var chef))continue;
            bool received=trip.Received||B(receiver,"portalReceiving");portalFlights[senderId]=trip with{Received=received};
            if(received&&!B(receiver,"portalReceiving")&&B(chef,"controlsEnabled")&&P(chef,"position").Distance(P(receiver,"portalTeleportPoint"))<1.5&&P(chef,"position").Distance(trip.Origin)>3)
                Mark("nativePortalTravel",frame,new(){["senderId"]=senderId,["receiverId"]=trip.Receiver,["player"]=trip.Player,["startFrame"]=trip.Frame,["destination"]=chef["position"]!.DeepClone()});
        }
        foreach(var (player,chef) in chefs)
        {
            oldChefs.TryGetValue(player,out var old);int held=I(chef,"heldEntityId");
            if(held!=0)Mark("heldItem",frame,new(){["player"]=player,["entityId"]=held});
            if(old is not null&&B(old,"aimingThrow")&&I(old,"heldEntityId")!=0&&!B(chef,"aimingThrow")&&held==0)
                Mark("aimReleaseWithEmptyHands",frame,new(){["player"]=player,["releasedEntityId"]=I(old,"heldEntityId")});
            if(old is not null&&I(old,"heldEntityId")==0&&held!=0&&flightItems.TryGetValue(held,out var flight)&&frame-flight.Frame<=1
                &&flight.Thrower!=I(chef,"entityId")&&chefs.Values.Any(c=>I(c,"entityId")==flight.Thrower)
                &&entities.TryGetValue(held,out var item)&&!B(item,"throwFlying")&&(I(item,"throwerEntityId")==flight.Thrower||I(item,"previousThrowerEntityId")==flight.Thrower))
                Mark("nativeChefCatch",frame,new(){["player"]=player,["itemId"]=held,["throwerEntityId"]=flight.Thrower,["lastFlyingFrame"]=flight.Frame});
            double speed=P(chef,"lastVelocity").Distance(default);
            if(old is not null&&N(chef,"dashTimer")>0&&N(old,"dashTimer")<=0)dashes[player]=(frame,P(chef,"position"),speed);
            if(dashes.TryGetValue(player,out var dash))
            {
                dash=dash with{PeakSpeed=Math.Max(dash.PeakSpeed,speed)};dashes[player]=dash;
                if(N(chef,"dashTimer")<=0&&N(old,"dashTimer")>0&&dash.PeakSpeed>N(chef,"runSpeed")+1&&P(chef,"position").Distance(dash.Start)>.5)
                    Mark("nativeDashCompleted",frame,new(){["player"]=player,["startFrame"]=dash.Frame,["peakCachedSpeed"]=dash.PeakSpeed,["displacement"]=P(chef,"position").Distance(dash.Start)});
            }
            if(old is not null)ObserveCollision(state,frame,player,old,chef);
        }
        var delivery=(state["gameEvents"] as JsonArray??[]).OfType<JsonObject>().FirstOrDefault(e=>e["kind"]?.ToString()=="delivery"&&B(e,"nativeMatch")&&B(e,"scoreApplied")&&I(e,"deliveryDelta")==1);
        if(delivery is not null)Mark("nativeSuccessfulDelivery",frame,new(){["eventIndex"]=delivery["index"]?.DeepClone(),["recipeId"]=delivery["recipeId"]?.DeepClone()});
        oldChefs=chefs;oldEntities=entities;previousFrame=frame;
    }
    private void ObserveCollision(JsonObject state,long frame,int player,JsonObject old,JsonObject chef)
    {
        if(evidence.ContainsKey("solidContactBlockedMotion")||I(state,"physicsStepsThisFrame")!=1||!B(chef,"controlsEnabled")||B(chef,"inputSuppressed")||!(N(old,"dashTimer")>0))return;
        var queued=P(old,"lastVelocity");double dt=N(state,"fixedDeltaTime"),expected=queued.Distance(default)*dt;
        var from=P(old,"position");var position=P(chef,"position");
        if(!double.IsFinite(expected)||expected<.08||from.Distance(position)>.01)return;
        KitchenModel model;
        try{model=KitchenModel.Build(state);}catch(InvalidDataException){return;}catch(ArgumentException){return;}
        var projected=new Point2(from.X+queued.X*dt,from.Z+queued.Z*dt);
        foreach(var obstacle in model.Obstacles.Where(o=>o.Evidence.Contains("solid",StringComparison.Ordinal)||o.Oriented is not null))
        {
            double contact=Math.Sqrt(Navigation.ObstacleDistanceSquared(position,obstacle));
            double ahead=Math.Sqrt(Navigation.ObstacleDistanceSquared(projected,obstacle));
            if(contact<model.ChefRadius-.03||contact>model.ChefRadius+.01||ahead>contact-.01)continue;
            var key=(player,obstacle.EntityId);blockedCounts[key]=blockedCounts.GetValueOrDefault(key)+1;
            if(blockedCounts[key]>=3)Mark("solidContactBlockedMotion",frame,new(){["player"]=player,["obstacleId"]=obstacle.EntityId,["observations"]=blockedCounts[key],["queuedDisplacement"]=expected,["actualDisplacement"]=from.Distance(position),["contactDistance"]=contact,["chefRadius"]=model.ChefRadius,["classification"]="Native position and cached-velocity evidence at a validated solid collider: queued physical motion blocked. Inference; no native collision callback was recorded."});
        }
    }
    public JsonArray Json()=>new(Required.Where(evidence.ContainsKey).Select(name=>(JsonNode)JsonValue.Create(name)!).ToArray());
    public JsonObject Evidence()=>new(evidence.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>KeyValuePair.Create<string,JsonNode?>(p.Key,p.Value.DeepClone())));
    public void RequireCompleteProbe()
    {
        var missing=Required.Where(name=>!evidence.ContainsKey(name)).ToArray();
        if(missing.Length!=0)throw new InvalidDataException("Combined probe native coverage missing: "+string.Join(", ",missing));
    }
    public static JsonObject AuditTrace(string path)
    {
        var coverage=new NativeProbeCoverage();
        foreach(var call in Traces.ReadCalls(path))if(call["response"]?["state"] is JsonObject state)coverage.Observe(state);
        coverage.RequireCompleteProbe();return coverage.Evidence();
    }
}
