using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual GF90 lane plus synthetic changes; never connects to the game.</summary>
    public static int ConditionalFarPotSelfTest(JsonObject prearmSnapshot)
    {
        int checks=0;
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException("Conditional far-pot fixture: "+message);checks++;}
        CarnivalPlanner Make(bool enabled=true)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline fixture attempted I/O."),null)
                {response=prearmSnapshot.DeepClone().AsObject(),options=new(ConditionalFarPotThrows:enabled),recipes=CarnivalRecipes.All.ToArray()};
            if(p.response["state"] is null)p.response=new JsonObject{["state"]=p.response};
            p.Refresh();p.chefs[2]["heldEntityId"]=0;
            p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline fixture attempted I/O."),null);
            return p;
        }
        var observed=Make();var guard=FarPotLane.Describe(observed.state);
        int near=FarPotLane.Id(guard,"nearPot"),far=FarPotLane.Id(guard,"farPot"),board=FarPotLane.Id(guard,"board"),pass=FarPotLane.Id(guard,"fallback");
        Check(observed.Frame==90&&near==7&&far==2&&board==56&&pass==45,"actual proven GF90 anchors are identified by role and geometry");
        void Supply(CarnivalPlanner p)=>p.Supply(2,CarnivalRecipes.Frankfurter,far,true);
        var baseline=Make(false);Supply(baseline);
        Check(baseline.workers[2]!.Actions.All(a=>a["type"]?.ToString()!="throw")&&I(baseline.workers[2]!.Actions.Last()["station"])==pass,
            "default-off retains the native counter handoff");
        foreach(string condition in new[]{"occupied-board","empty-near","reserved-board","moved-far","unknown-incarnation"})
        {
            var p=Make();
            if(condition=="occupied-board")p.Entity(board)!["attachedEntityId"]=125;
            if(condition=="empty-near"){p.Entity(near)!["contents"]=new JsonArray();p.Entity(near)!["composition"]=null;}
            if(condition=="reserved-board")p.reserved.Add(board);
            if(condition=="moved-far"){p.Entity(far)!["position"]!["x"]=19;p.model=KitchenModel.Build(p.state);}
            if(condition=="unknown-incarnation")p.Entity(far)!.Remove("observedOrdinal");
            Supply(p);
            Check(p.workers[2] is {} work&&work.Actions.All(a=>a["type"]?.ToString()!="throw")&&I(work.Actions.Last()["station"])==pass,
                condition+" selects the existing addressed fallback before starting a throw job");
        }
        var direct=Make();Supply(direct);var job=direct.workers[2]!;
        int[] locks=[near,far,board,pass,I(guard["nearHome"]),I(guard["farHome"])];
        Check(job.Name=="supply-Frankfurter-conditional-far-pot"&&job.Actions.Last()["farPotLane"] is JsonObject,"valid lane creates an explicitly guarded throw");
        Check(locks.All(direct.reserved.Contains)&&locks.All(job.OwnedResources.Contains),"both vessels, stove homes, board and fallback are leased through the throw");
        Check(!direct.Start(0,"competing-near-harvest",[direct.A("take",near)],[near])&&!direct.Start(1,"competing-onion-placement",[direct.A("place",board)],[board]),
            "a concurrent chef cannot consume the near pot or refill the lane board");
        Check(direct.counterSupplies[pass]==(far,CarnivalRecipes.Frankfurter.Id),"fallback remains addressed to its exact native receiving pot");
        direct.ReleaseCompletedUnplatedResources();
        Check(locks.All(direct.reserved.Contains),"staged unplated release cannot release conditional throw leases");
        void Finish(CarnivalPlanner p,bool nativeCatch)
        {
            var work=p.workers[2]!;
            if(nativeCatch){p.Entity(far)!["contents"]=p.Entity(near)!["contents"]!.DeepClone();p.Entity(far)!["composition"]=p.Entity(near)!["composition"]!.DeepClone();}
            else p.Entity(pass)!["attachedEntityId"]=125;
            p.chefs[2]["heldEntityId"]=0;work.Actions.Clear();
            work.Active=p.runner.CreateAction(new JsonObject{["type"]=nativeCatch?"throw":"place",["player"]=2});work.Active.IsDone=true;
            p.CompleteWork();
        }
        Finish(direct,true);
        Check(!direct.counterSupplies.ContainsKey(pass)&&locks.All(i=>direct.Free(i))&&direct.workers[2] is null,"exact native catch retires only its address and releases all owned locks");
        var fallback=Make();Supply(fallback);Finish(fallback,false);
        Check(fallback.counterSupplies[pass]==(far,CarnivalRecipes.Frankfurter.Id)&&locks.All(i=>fallback.Free(i)),"ordinary native counter placement preserves the address for a central relay");
        Check(fallback.RelayRawIntoVessel(0)&&I(fallback.workers[0]!.Actions.Last()["station"])==far,"fallback relay remains bound to the far pot");
        return checks;
    }
}

public sealed partial class RouteRunner
{
    public static int ConditionalFarPotSelfTest(JsonObject prearmSnapshot)
    {
        int checks=0;
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException("Conditional throw barrier: "+message);checks++;}
        var observed=KitchenModel.SnapshotState(prearmSnapshot).DeepClone().AsObject();
        var guard=FarPotLane.Describe(observed);int far=FarPotLane.Id(guard,"farPot"),near=FarPotLane.Id(guard,"nearPot"),board=FarPotLane.Id(guard,"board");
        Check(FarPotLane.Invalid(observed,guard,true) is null,"actual prearm native state satisfies the demonstrated lane");
        var runner=new RouteRunner(_=>throw new InvalidOperationException("Offline fixture attempted I/O."),null);
        RouteActionHandle Action()=>runner.CreateAction(new JsonObject{["type"]="throw",["player"]=2,["targetEntityId"]=far,["farPotLane"]=guard.DeepClone()});
        foreach(string change in new[]{"occupied-board","empty-near","held-far","changed-incarnation","wrong-position","wrong-profile","missing-contents","empty-near-home","counter-home","changed-home-incarnation","moved-home"})
        {
            var state=observed.DeepClone().AsObject();var action=Action();
            if(change=="occupied-board")Entity(state,board)!["attachedEntityId"]=125;
            if(change=="empty-near"){Entity(state,near)!["contents"]=new JsonArray();Entity(state,near)!["composition"]=null;}
            if(change=="held-far")Chef(state,0)["heldEntityId"]=far;
            if(change=="changed-incarnation")Entity(state,far)!["observedOrdinal"]=99999;
            if(change=="wrong-position")Chef(state,2)["position"]!["x"]=14.2;
            if(change=="wrong-profile")Chef(state,2)["throwForce"]=19;
            if(change=="missing-contents")Entity(state,far)!.Remove("contents");
            int nearHome=guard["nearHome"]!.GetValue<int>();
            if(change=="empty-near-home")Entity(state,nearHome)!["attachedEntityId"]=0;
            if(change=="counter-home")Entity(state,nearHome)!["components"]=new JsonArray("PhysicalAttachment");
            if(change=="changed-home-incarnation")Entity(state,nearHome)!["observedOrdinal"]=99999;
            if(change=="moved-home")Entity(state,nearHome)!["position"]!["x"]=20;
            var input=runner.Tick(action,state);
            Check(!Flag(input,"use")&&!action.IsDone&&action.Specification["type"]?.ToString()=="place"&&action.Stage=="resolve",change+" transitions before arming to ordinary counter placement");
        }
        var blocked=observed.DeepClone().AsObject();Entity(blocked,board)!["attachedEntityId"]=125;
        Entity(blocked,FarPotLane.Id(guard,"fallback"))!["attachedEntityId"]=999;
        var noFallback=Action();Check(!Flag(runner.Tick(noFallback,blocked),"use")&&noFallback.Error?.Contains("reserved fallback unavailable")==true,
            "occupied fallback causes a neutral prearm failure instead of an unvalidated placement");
        foreach(string stage in new[]{"throw-arm","throw-aim","throw-release"})
        {
            var state=observed.DeepClone().AsObject();var action=Action();
            Check(Flag(runner.Tick(action,state),"use")&&action.Stage=="throw-arm","valid actual lane emits the ordinary first arming edge");
            runner.Stage(action.Action,stage);Chef(state,2)["aimingThrow"]=true;Chef(state,2)["inputSuppressed"]=true;
            Entity(state,board)!["attachedEntityId"]=125;
            var input=runner.Tick(action,state);
            Check(action.IsDone&&action.Error?.Contains("candidate failed; neutral cleanup may release ingredient")==true&&!Flag(input,"use"),
                stage+" invalidation fails explicitly and never claims retained food or safe completion");
        }
        foreach(bool suppressionStillActive in new[]{true,false})
        {
            var state=observed.DeepClone().AsObject();Chef(state,2)["useSuppressed"]=true;var action=Action();
            Check(Flag(runner.Tick(action,state),"use")&&action.Stage=="throw-clear-use-press","native suppressed press is emitted by the existing throw preparation");
            Chef(state,2)["useSuppressed"]=suppressionStillActive;Entity(state,board)!["attachedEntityId"]=125;
            var input=runner.Tick(action,state);
            Check(!Flag(input,"use")&&(suppressionStillActive?
                !action.IsDone&&action.Specification["type"]?.ToString()=="place":
                action.IsDone&&action.Error?.Contains("neutral cleanup may release ingredient")==true),
                "suppressed-press invalidation distinguishes a still-suppressed release from a potentially armed cleared-suppression edge");
        }
        var releaseState=observed.DeepClone().AsObject();var release=Action();runner.Tick(release,releaseState);
        runner.Stage(release.Action,"throw-release");Chef(releaseState,2)["aimingThrow"]=true;Chef(releaseState,2)["inputSuppressed"]=true;
        Check(!Flag(runner.Tick(release,releaseState),"use")&&!release.IsDone&&release.Stage=="throw-await-flight","valid release still uses the native neutral use-release edge and waits for evidence");
        Entity(releaseState,far)!["contents"]=Entity(releaseState,near)!["contents"]!.DeepClone();
        Entity(releaseState,far)!["composition"]=Entity(releaseState,near)!["composition"]!.DeepClone();
        Entity(releaseState,125)!["active"]=false;Chef(releaseState,2)["heldEntityId"]=0;Chef(releaseState,2)["aimingThrow"]=false;Chef(releaseState,2)["inputSuppressed"]=false;
        Entity(releaseState,board)!["attachedEntityId"]=900; // Landing changes must be judged by the existing exact vessel catch observer.
        runner.Tick(release,releaseState);runner.Tick(release,releaseState);
        Check(release.IsDone&&release.Error is null,"source consumption plus exact native target delta remains the completion authority after release");
        return checks;
    }
}
