using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int CannonPreemptionSelfTest(JsonObject blocked1250,JsonObject allowed1287)
    {
        int checks=0,originalCompletions=0;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("Cannon boundary fixture: "+message);checks++;}
        CarnivalPlanner Make(JsonObject? snapshot=null,bool enabled=true,bool capturedHeat=false)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline boundary fixture attempted I/O."),null)
                {response=(snapshot??allowed1287).DeepClone().AsObject(),options=new(CannonBoundaryPreemption:enabled),recipes=CarnivalRecipes.All.ToArray()};
            if(p.response["state"] is null)p.response=new JsonObject{["state"]=p.response};p.Refresh();
            p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline boundary fixture attempted I/O."),null);
            var suffix=new List<JsonObject>();
            var boundary=new JsonObject{["type"]="switch-condiment",["index"]=1};
            if(p.Frame==1250){boundary=p.A("place",33);suffix.Add(new JsonObject{["type"]="switch-condiment",["index"]=1});}
            suffix.Add(p.A("take",33));var apply=p.A("apply",72);apply["expectedIngredientId"]=CarnivalRecipes.Ketchup.Id;suffix.Add(apply);suffix.Add(p.A("place",49));
            if(!p.Start(0,"assemble-meal-2-Hotdog_Ketchup_Mustard",new[]{boundary}.Concat(suffix),[11,153,33,49,72,79],()=>originalCompletions++))throw new InvalidOperationException("Could not reconstruct original assembly.");
            p.Start(3,"transfer-mixed-dough",[p.A("place",18)],[6,5,18]);
            var work=p.workers[0]!;var completed=work.Actions.Dequeue();completed["player"]=0;work.Active=p.runner.CreateAction(completed);work.Active.IsDone=true;
            p.CompleteWork();
            // The current common arbiter gives captured bowl3 at9.279s
            // priority over this optional cannon detour. Ownership/resume
            // tests below use an explicitly synthetic earlier mixing clock;
            // the measured chef geometry and action boundary are unchanged.
            if(!capturedHeat)p.Entity(3)!["mixingProgress"]=8;
            return p;
        }
        var native=Make(capturedHeat:true);
        Check(native.NativeHeatObligations().Any(h=>h.Vessel==3)&&!native.TryBeginCannonBoundaryPreemption(),
            "captured1287 mature native bowl now has safety priority over optional cannon preemption");
        var p=Make();var original=p.workers[0]!;var queue=JsonSerializer.Serialize(original.Actions);var originalLocks=original.OwnedResources.Order().ToArray();
        Check(p.Frame==1287&&p.Held(0)==0&&N(p.chefs[0]["dashTimer"])<0&&p.CannonBoundaryEligible(0,original),"captured post-neutral switch boundary is eligible without resetting any active action");
        var path=Navigation.ToStation(p.model,p.Position(0),p.Station(78)!,p.TrafficObstacles(0));
        Check(path.Success&&Math.Abs(path.Length-4.92774643)<.01,"actual full-clearance button path is the measured4.9277-unit walking route");
        Check(p.OptimisticRemainingQueueSeconds(0,original)>.5,"observed three-action suffix exceeds the short completion guard even at native maximum speed");
        Check(!p.CannonDetourHeatUrgent(),"actual heating and ordinary mixing leave the bounded detour window before their warning thresholds");
        Check(p.TryBeginCannonBoundaryPreemption(),"loaded native cannon admits the same boundary in the explicit earlier-mixing fixture");
        var child=p.workers[0]!;var suspended=p.cannonInterruptions[0];
        Check(ReferenceEquals(suspended.Original,original)&&original.Active is null&&JsonSerializer.Serialize(original.Actions)==queue,"suspended job and exact queue are preserved by reference");
        Check(originalLocks.All(p.reserved.Contains)&&original.OwnedResources.SetEquals(originalLocks)&&p.reserved.Contains(84)&&p.reserved.Contains(78),"original leases remain held beside disjoint child cannon and button leases");
        Check(!p.Start(1,"steal-paused-plate",[p.A("take",11)],[11]),"another worker cannot acquire a paused job's plate");
        Check(child.Actions.Single()["dash"]?.GetValue<bool>()==false&&I(child.Actions.Single()["timeoutFrames"])==180,"interrupting action walks and has a three-second logical limit");
        Check(!p.TryBeginCannonBoundaryPreemption(),"no nested or simultaneous second interruption is admitted");
        var input=p.runner.Tick(p.runner.CreateAction(child.Actions.Peek()),p.response);
        Check(!B(input["pickup"])&&!B(input["use"])&&!B(input["dash"]),"first child tick emits ordinary walking toward the actual native fire button");
        // Only this offline fixture changes snapshots to represent observed
        // child completion. Production ResumeCannonWork never changes a pose.
        p.chefs[0]["position"]=new JsonObject{["x"]=16.67,["y"]=.05,["z"]=-16.46};p.state["gameplayFrame"]=1400;
        child.Actions.Clear();child.Active=p.runner.CreateAction(new JsonObject{["type"]="fire-cannon",["player"]=0});child.Active.IsDone=true;
        p.CompleteWork();
        Check(ReferenceEquals(p.workers[0],original)&&p.cannonInterruptions.Count==0&&original.Active is null&&original.NeutralBoundaryFrame==-1,"native child completion resumes the same original job at an unconsumed action boundary");
        Check(originalLocks.All(p.reserved.Contains)&&!p.reserved.Contains(84)&&!p.reserved.Contains(78)&&JsonSerializer.Serialize(original.Actions)==queue,"child completion releases only child leases, preserving the exact pending suffix");
        var next=original.Actions.Dequeue();next["player"]=0;original.Active=p.runner.CreateAction(next);p.runner.Tick(original.Active,p.response);
        Check(original.Active.Action.Motion?.Points.FirstOrDefault().Distance(new(16.67,-16.46))<.001,"resumed navigation is newly planned from observed fire-button position");
        original.Actions.Clear();original.Active.IsDone=true;p.CompleteWork();
        Check(originalCompletions==1&&originalLocks.All(i=>p.Free(i)),"original completion callback runs exactly once and finally releases its own leases");

        var blocked=Make(blocked1250);Check(!blocked.TryBeginCannonBoundaryPreemption()&&blocked.cannonInterruptions.Count==0,"actual earlier1250 boundary has no safe button approach and is rejected");
        var disabled=Make(enabled:false);Check(!disabled.TryBeginCannonBoundaryPreemption(),"default-off preserves uninterrupted original work");
        foreach(string reason in new[]{"active-action","old-boundary","held-item","native-dash","armed-throw","held-use","short-suffix","free-other-chef","occupied-button","launched-stale-id","urgent-pot","urgent-mixer","sauce-owner","early-owner","bakery-owner"})
        {
            var deny=Make();var work=deny.workers[0]!;
            if(reason=="active-action")work.Active=deny.runner.CreateAction(deny.A("take",33));
            if(reason=="old-boundary")work.NeutralBoundaryFrame--;
            if(reason=="held-item")deny.chefs[0]["heldEntityId"]=11;
            if(reason=="native-dash")deny.chefs[0]["dashTimer"]=.1;
            if(reason=="armed-throw")deny.chefs[0]["aimingThrow"]=true;
            if(reason=="held-use")deny.response["inputs"]!.AsArray().OfType<JsonObject>().Single(i=>I(i["player"])==0)["use"]=true;
            if(reason=="short-suffix")work.Actions.Dequeue();
            if(reason=="free-other-chef")deny.workers[3]=null;
            if(reason=="occupied-button")deny.reserved.Add(78);
            if(reason=="launched-stale-id")deny.Entity(84)!["cannonState"]="Launched";
            if(reason=="urgent-pot")deny.Entity(7)!["cookingProgress"]=13;
            if(reason=="urgent-mixer")deny.Entity(3)!["mixingProgress"]=13;
            if(reason=="sauce-owner")deny.sauceLease=new SauceLease(1,CarnivalRecipes.GetRecipe(125780),0,3,11,153,33,49,72,79,deny.Frame,[]);
            if(reason=="early-owner")
            {
                deny.earlyOnion=new EarlyOnionLease(4,4,deny.AttachmentParent(4),32,I(deny.Entity(4)?["observedOrdinal"]),0,deny.Frame);
            }
            if(reason=="bakery-owner")
            {
                var lease=new BakeryLease(3,6,14,32,1,1,1,deny.Frame){Owner=0};deny.bakeryLeases.Add(3,lease);
            }
            Check(!deny.TryBeginCannonBoundaryPreemption()&&deny.cannonInterruptions.Count==0,reason+" refuses preemption without modifying original work");
        }
        var corrupted=Make();corrupted.TryBeginCannonBoundaryPreemption();var interrupted=corrupted.cannonInterruptions[0];
        corrupted.workers[0]=null;corrupted.reserved.Remove(11);bool rejected=false;
        try{corrupted.ResumeCannonWork(interrupted);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"missing paused lease fails explicitly rather than resuming damaged ownership");
        return checks;
    }
}
