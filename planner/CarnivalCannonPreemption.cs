using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class CannonInterruption(int player,Work original,int cannon,int button,int frame)
    {
        public readonly int Player=player,Cannon=cannon,Button=button,Frame=frame;
        public readonly Work Original=original;
        public readonly int[] OriginalOwned=original.OwnedResources.Order().ToArray();
        public readonly string Queue=JsonSerializer.Serialize(original.Actions);
    }
    private readonly Dictionary<int,CannonInterruption> cannonInterruptions=[];
    private const int CannonInterruptionTimeoutFrames=180;
    private const double CannonInterruptionHeatWindow=(CannonInterruptionTimeoutFrames+6)/60d;

    private bool CannonBoundaryEligible(int player,Work work)
    {
        if(work.Active is not null||work.NeutralBoundaryFrame!=Frame||work.Actions.Count<3||
            !work.Name.StartsWith("assemble-meal-",StringComparison.Ordinal)||
            work.CompletedBoundaryAction?["type"]?.ToString() is not ("place" or "switch-condiment")||
            Held(player)!=0||Region(player)!="center"||IsSauceParticipant(player)||IsEarlyOnionParticipant(player)||IsBakeryParticipant(player)||IsFryerParticipant(player)||IsPotParticipant(player))return false;
        var chef=chefs[player];
        if(!B(chef["controlsEnabled"])||!B(chef["directlyControlled"])||!B(chef["canAcceptInput"])||
            B(chef["respawning"])||B(chef["aimingThrow"])||B(chef["inputSuppressed"])||
            chef["dashTimer"] is null||N(chef["dashTimer"])>0||chef["lastVelocity"] is null||KitchenModel.Position(chef["lastVelocity"]).Distance(default)>.05)return false;
        var input=(response["inputs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(i=>I(i["player"])==player);
        return input is not null&&!B(input["use"])&&!B(input["pickup"])&&!B(input["dash"])&&N(input["x"])==0&&N(input["y"])==0;
    }

    private bool CannonDetourHeatUrgent()
    {
        // This detour receives three logical seconds plus a neutral completion
        // margin. Decline if a
        // heating/mixing item could enter its warning window during that time.
        // Parked vessels retain their progress and are explicitly off process.
        foreach(var station in Stations("pot").Concat(Stations("pan")).Concat(Stations("basket")))
        {
            int id=station.EntityId;if(EmptyFood(id)||!NativeCookingStation(Entity(AttachmentParent(id))))continue;
            double duration=N(Entity(id)?["cookingTime"]),progress=N(Entity(id)?["cookingProgress"]);
            if(duration<=0||progress<0||Food(id).IsRuined||1.3*duration-progress<=CannonInterruptionHeatWindow)return true;
        }
        foreach(var station in Stations("bowl"))
        {
            int id=station.EntityId;var home=Entity(AttachmentParent(id));
            if(EmptyFood(id)||home is null||!KitchenModel.Components(home).Contains("MixingStation"))continue;
            double duration=N(Entity(id)?["mixingTime"]),progress=N(Entity(id)?["mixingProgress"]);
            if(duration<=0||progress<0||Food(id).IsRuined||1.3*duration-progress<=CannonInterruptionHeatWindow)return true;
        }
        // Existing rescue schedulers start approaches at nine seconds. Reserve
        // their opportunity even if no owner could yet be assigned this frame.
        return EarlyOnionLeases().Any(l=>Attached(l.Home)==l.Pan&&N(Entity(l.Pan)?["cookingProgress"])>=9-CannonInterruptionHeatWindow)||
            bakeryLeases.Values.Any(l=>Attached(l.Home)==l.Bowl&&N(Entity(l.Bowl)?["mixingProgress"])>=9-CannonInterruptionHeatWindow);
    }

    private double? OptimisticRemainingQueueSeconds(int player,Work work)
    {
        double speed=N(chefs[player]["maxSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
        if(speed<=0||!double.IsFinite(speed))return null;
        var points=new[]{Position(player)};double distance=0;
        foreach(var action in work.Actions)
        {
            string? type=action["type"]?.ToString();
            if(type is not ("take" or "assemble" or "combine" or "apply" or "place" or "switch-condiment"))return null;
            string? selector=type=="switch-condiment"?"condiment-switch":action["station"]?.ToString();
            if(selector is null)return null;
            var next=model.Candidates(selector).SelectMany(s=>s.Approaches).Where(a=>a.Region=="center").Select(a=>a.Position).ToArray();
            if(next.Length==0)return null;
            // Optimistic straight travel between any observed candidate
            // approaches, at native maximum dash speed, ignoring obstacles,
            // button/settling time and dash cooldown. Subtract both tolerances.
            distance+=Math.Max(0,points.Min(p=>next.Min(p.Distance))-.2);
            points=next;
        }
        return distance/speed;
    }

    private bool TryBeginCannonBoundaryPreemption()
    {
        if(!options.CannonBoundaryPreemption||cannonInterruptions.Count!=0||trafficYield is not null||heatSafetyBlocked.Count!=0||NativeHeatObligations().Any()||CannonDetourHeatUrgent())return false;
        // An ordinarily idle central chef already prioritizes FireLoaded.
        if(new[]{0,3}.Any(p=>workers[p] is null&&!IsSauceParticipant(p)&&!IsEarlyOnionParticipant(p)&&!IsBakeryParticipant(p)&&!IsFryerParticipant(p)&&!IsPotParticipant(p)))return false;
        var candidates=new List<(int Player,string Side,int Cannon,int Button,NavigationPath Path,double Remaining)>();
        foreach(int player in new[]{0,3})
        {
            var work=workers[player];if(work is null||!CannonBoundaryEligible(player,work))continue;
            double? remaining=OptimisticRemainingQueueSeconds(player,work);if(remaining is null||remaining<=.5)continue;
            foreach(string side in new[]{"left","right"})
            {
                int cannon=CannonId(side),passenger=side=="left"?2:1,button=I(Entity(cannon)?["cannonButtonEntityId"]);
                if(I(Entity(cannon)?["cannonLoadedEntityId"])!=I(chefs[passenger]["entityId"])||
                    B(chefs[passenger]["controlsEnabled"])||Entity(cannon)?["cannonState"]?.ToString()!="Load"||
                    B(Entity(cannon)?["cannonFlying"])||button==0||!Free(cannon,button)||!CanReserveCannonFlight(cannon,passenger)||Station(button) is not {} buttonStation)continue;
                var path=Navigation.ToStation(model,Position(player),buttonStation,TrafficObstacles(player));
                double walking=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
                if(!path.Success||walking<=0||path.Length/walking>1)continue;
                candidates.Add((player,side,cannon,button,path,remaining.Value));
            }
        }
        if(candidates.Count==0)return false;
        var choice=candidates.OrderBy(c=>c.Path.Length).ThenBy(c=>c.Player).ThenBy(c=>c.Side,StringComparer.Ordinal).First();
        var original=workers[choice.Player]!;
        var interruption=new CannonInterruption(choice.Player,original,choice.Cannon,choice.Button,Frame);
        if(interruption.OriginalOwned.Any(id=>!reserved.Contains(id)))throw new InvalidOperationException("Cannon preemption found a missing original lease.");
        var action=Cannon("fire-cannon",choice.Side);action["player"]=choice.Player;action["passengerPlayer"]=choice.Side=="left"?2:1;
        action["destinationRegion"]=choice.Side=="left"?"lower-right":"lower-left";
        action["dash"]=false;action["shortDash"]=false;action["timeoutFrames"]=CannonInterruptionTimeoutFrames;action["maxNoPathFrames"]=60;
        cannonInterruptions.Add(choice.Player,interruption);workers[choice.Player]=null;
        Log("plannerJobPaused",new JsonObject{["player"]=choice.Player,["name"]=original.Name,["frame"]=Frame,
            ["resources"]=JsonSerializer.SerializeToNode(interruption.OriginalOwned),["completedBoundaryAction"]=original.CompletedBoundaryAction!.DeepClone(),
            ["cannon"]=choice.Cannon,["button"]=choice.Button,["remainingActions"]=original.Actions.Count,
            ["optimisticRemainingQueueSeconds"]=choice.Remaining,["buttonPath"]=JsonSerializer.SerializeToNode(choice.Path)});
        if(!StartCannonFire(choice.Player,"interrupt-fire-"+choice.Side,action,choice.Cannon,choice.Button,()=>ResumeCannonWork(interruption)))
            throw new InvalidOperationException("Cannon interruption could not acquire its independently checked firing resources.");
        return true;
    }

    private void ResumeCannonWork(CannonInterruption interruption)
    {
        int player=interruption.Player;var original=interruption.Original;
        if(workers[player] is not null||Held(player)!=0||!B(chefs[player]["controlsEnabled"])||Region(player)!="center"||
            original.Active is not null||!original.OwnedResources.SetEquals(interruption.OriginalOwned)||
            interruption.OriginalOwned.Any(id=>!reserved.Contains(id))||JsonSerializer.Serialize(original.Actions)!=interruption.Queue)
            throw new InvalidOperationException("Cannon interruption could not resume its exact empty-handed original work and leases.");
        original.NeutralBoundaryFrame=-1;workers[player]=original;cannonInterruptions.Remove(player);
        Log("plannerJobResumed",new JsonObject{["player"]=player,["name"]=original.Name,["frame"]=Frame,
            ["pausedAtFrame"]=interruption.Frame,["pausedFrames"]=Frame-interruption.Frame,
            ["resources"]=JsonSerializer.SerializeToNode(interruption.OriginalOwned),["remainingActions"]=original.Actions.Count,
            ["observedResumePosition"]=chefs[player]["position"]?.DeepClone()});
        // No existing action or motion is reset: Active was null at the real
        // boundary. The normal loop creates the next queued action from here.
    }

    private JsonArray CannonInterruptionStatus()=>new(cannonInterruptions.Values.OrderBy(i=>i.Player).Select(i=>(JsonNode?)new JsonObject{
        ["player"]=i.Player,["name"]=i.Original.Name,["pausedAtFrame"]=i.Frame,["resources"]=JsonSerializer.SerializeToNode(i.OriginalOwned),
        ["remainingActions"]=i.Original.Actions.Count,["cannon"]=i.Cannon,["button"]=i.Button}).ToArray());
}
