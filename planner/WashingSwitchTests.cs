using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class WashingSwitchTests
{
    public static void Run(JsonObject fixture)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;}
        var state=KitchenModel.SnapshotState(fixture.DeepClone().AsObject());var model=KitchenModel.Build(state);
        JsonObject Entity(int id)=>state["entities"]!.AsArray().OfType<JsonObject>().Single(e=>e["id"]!.GetValue<int>()==id);
        JsonObject Chef(int player)=>state["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==player);
        void PositionAt(int player,KitchenStation station)
        {
            var p=station.Approaches.First().Position;var chef=Chef(player);
            chef["position"]!["x"]=p.X;chef["position"]!["z"]=p.Z;
            chef["lastVelocity"]=new JsonObject{["x"]=0,["z"]=0};chef["heldEntityId"]=0;chef["useTargetId"]=station.EntityId;
        }
        var runner=new RouteRunner(_=>throw new Exception("Pure primitive test called game."),null);
        var sinkStation=model.Resolve("sink");var sink=Entity(sinkStation.EntityId);
        var outputStation=model.Resolve("drying");var output=Entity(outputStation.EntityId);
        sink["washingOutputEntityId"]=outputStation.EntityId;sink["washingTime"]=2;sink["washingProgress"]=0;sink["plateCount"]=2;output["plateCount"]=0;
        PositionAt(1,sinkStation);
        var wash=runner.CreateAction(Json.Object("""{"player":1,"type":"wash","count":2}"""));
        int uses=0,afterDirtyGone=0;bool completedWithoutOutput=false;
        for(int i=0;i<100&&!wash.IsDone;i++)
        {
            var input=runner.Tick(wash,state);
            if(input["use"]!.GetValue<bool>())
            {
                uses++;sink["washingProgress"]=uses%3;
                if(uses%3==0)sink["plateCount"]=2-uses/3;
            }
            if(sink["plateCount"]!.GetValue<int>()==0)
            {
                afterDirtyGone++;completedWithoutOutput|=wash.IsDone&&output["plateCount"]!.GetValue<int>()==0;
                if(afterDirtyGone==4)output["plateCount"]=2;
            }
        }
        Check(wash.IsDone&&wash.Error is null&&uses==6,"wash holds use until requested dirty plates removed");
        Check(!completedWithoutOutput&&afterDirtyGone>=4,"washing timer reset and dirty decrement cannot replace clean output evidence");
        Check(!runner.Tick(wash,state)["use"]!.GetValue<bool>(),"completed washing releases interaction");
        sink["plateCount"]=1;sink["washingProgress"]=0;sink["washingOutputEntityId"]=0;
        var missing=runner.CreateAction(Json.Object("""{"player":1,"type":"wash"}"""));
        for(int i=0;i<40&&!missing.IsDone;i++)runner.Tick(missing,state);
        Check(missing.Error?.Contains("washingOutputEntityId",StringComparison.Ordinal)==true,"wash refuses missing native output linkage");
        sink["washingOutputEntityId"]=outputStation.EntityId;output["plateCount"]=0;Chef(1)["useTargetId"]=0;
        var wrong=runner.CreateAction(Json.Object("""{"player":1,"type":"wash"}"""));bool unsafeUse=false;
        for(int i=0;i<80&&!wrong.IsDone;i++)unsafeUse|=runner.Tick(wrong,state)["use"]!.GetValue<bool>();
        Check(!unsafeUse&&wrong.Error?.Contains("target",StringComparison.Ordinal)==true,"wash never interacts without matching native sink target");
        PositionAt(1,sinkStation);var washer=Chef(1);washer["clientPredictedInteractionId"]=sinkStation.EntityId;washer["serverInteractionId"]=0;washer["interactingEntityId"]=0;
        sink["washingProgress"]=0;sink["plateCount"]=1;output["plateCount"]=0;
        var reacquire=runner.CreateAction(Json.Object("""{"player":1,"type":"wash","count":1,"maxReacquireAttempts":1}"""));
        var position=KitchenModel.Position(washer["position"]);var cached=new Point2();bool stale=true,targetCleared=false,legalRetreat=false;int resumedUses=0;
        for(int frame=0;frame<350&&!reacquire.IsDone;frame++)
        {
            state["fixedDeltaTime"]=.02;state["unityDeltaTime"]=1.0/60;state["framesSinceNoPhysics"]=(frame+5)%6;
            var input=runner.Tick(reacquire,state);
            if(reacquire.Stage=="wash-retreat")legalRetreat|=!input["use"]!.GetValue<bool>()&&(KitchenModel.N(input["x"])!=0||KitchenModel.N(input["y"])!=0);
            if(frame%6!=0)
            {
                var next=new Point2(position.X+cached.X*.02,position.Z+cached.Z*.02);
                if(Navigation.SegmentClear(model,position,next,"lower-left"))position=next;
            }
            var stick=new Point2(KitchenModel.N(input["x"]),-KitchenModel.N(input["y"]));double length=stick.Distance(default);
            cached=length<1e-6?default:new(stick.X/length*6,stick.Z/length*6);
            washer["position"]!["x"]=position.X;washer["position"]!["z"]=position.Z;
            washer["lastVelocity"]!["x"]=cached.X;washer["lastVelocity"]!["z"]=cached.Z;
            if(length>1e-6){washer["forward"]!["x"]=stick.X/length;washer["forward"]!["z"]=stick.Z/length;}
            bool near=position.Distance(sinkStation.Position)<1.65;
            washer["useTargetId"]=near?sinkStation.EntityId:0;
            if(!near){stale=false;targetCleared=true;washer["clientPredictedInteractionId"]=0;washer["serverInteractionId"]=0;washer["interactingEntityId"]=0;}
            if(!stale&&near&&input["use"]!.GetValue<bool>())
            {
                resumedUses++;washer["clientPredictedInteractionId"]=sinkStation.EntityId;washer["serverInteractionId"]=sinkStation.EntityId;washer["interactingEntityId"]=sinkStation.EntityId;sink["washingProgress"]=resumedUses/60.0;
                if(resumedUses==7){sink["washingProgress"]=0;sink["plateCount"]=0;output["plateCount"]=1;}
            }
        }
        Check(reacquire.IsDone&&reacquire.Error is null&&resumedUses==7,"stale predicted sink interaction recovers and finishes native washing: "+reacquire.Error+", stage="+reacquire.Stage+", uses="+resumedUses+", position="+position);
        Check(targetCleared&&legalRetreat,"wash recovery clears the native target through released legal movement");
        PositionAt(1,sinkStation);washer["useSuppressed"]=true;washer["clientPredictedInteractionId"]=0;washer["serverInteractionId"]=0;washer["interactingEntityId"]=0;
        sink["plateCount"]=1;sink["washingProgress"]=0;output["plateCount"]=0;
        var unsuppress=runner.CreateAction(Json.Object("""{"player":1,"type":"wash","count":1}"""));
        bool previousUse=false,suppressionRelease=false;int suppressedPresses=0,activeUses=0;
        for(int frame=0;frame<70&&!unsuppress.IsDone;frame++)
        {
            var input=runner.Tick(unsuppress,state);bool use=input["use"]!.GetValue<bool>();
            if(washer["useSuppressed"]!.GetValue<bool>())
            {
                if(use)suppressedPresses++;
                if(previousUse&&!use){washer["useSuppressed"]=false;suppressionRelease=true;}
            }
            else if(use)
            {
                activeUses++;washer["serverInteractionId"]=sinkStation.EntityId;washer["interactingEntityId"]=sinkStation.EntityId;sink["washingProgress"]=activeUses/60.0;
                if(activeUses==7){sink["washingProgress"]=0;sink["plateCount"]=0;output["plateCount"]=1;}
            }
            previousUse=use;
        }
        Check(unsuppress.IsDone&&unsuppress.Error is null&&suppressedPresses==1&&suppressionRelease&&activeUses==7,"observed use suppression clears through one legal press/release before washing");
        Check(unsuppress.Stage=="washing","use-suppression guard avoids unnecessary retreat");
        var switchStation=model.Resolve("condiment-switch");var dispenserStation=model.Resolve("condiment");var dispenser=Entity(dispenserStation.EntityId);
        PositionAt(0,switchStation);dispenser["switchIndex"]=0;
        int plate=model.Stations.First(s=>s.Role=="plate").EntityId;Chef(0)["heldEntityId"]=plate;
        var carrying=runner.CreateAction(Json.Object("""{"player":0,"type":"switch-condiment","expectedIndex":1}"""));
        Check(!runner.Tick(carrying,state)["use"]!.GetValue<bool>()&&carrying.Error?.Contains("empty hands",StringComparison.Ordinal)==true,"native held-item world-use gate rejects condiment switching while carrying");
        Chef(0)["heldEntityId"]=0;
        var change=runner.CreateAction(Json.Object("""{"player":0,"type":"switch-condiment","expectedIndex":1}"""));int edges=0;
        for(int i=0;i<60&&!change.IsDone;i++)
        {
            if(runner.Tick(change,state)["use"]!.GetValue<bool>()){edges++;dispenser["switchIndex"]=1;}
        }
        Check(change.IsDone&&change.Error is null&&edges==1,"condiment switch verifies one use edge and resulting native index");
        Check(Chef(0)["heldEntityId"]!.GetValue<int>()==0,"condiment switching retains empty hands");
        var already=runner.CreateAction(Json.Object("""{"player":0,"type":"switch-condiment","index":1}"""));
        Check(!runner.Tick(already,state)["use"]!.GetValue<bool>()&&already.IsDone&&already.Error is null,"already-selected condiment emits no toggle");
        Chef(0)["heldEntityId"]=0;Chef(0)["useTargetId"]=0;
        var wrongSwitch=runner.CreateAction(Json.Object("""{"player":0,"type":"switch-condiment","index":0}"""));unsafeUse=false;
        for(int i=0;i<80&&!wrongSwitch.IsDone;i++)unsafeUse|=runner.Tick(wrongSwitch,state)["use"]!.GetValue<bool>();
        Check(!unsafeUse&&wrongSwitch.Error?.Contains("target",StringComparison.Ordinal)==true,"wrong native switch target cannot emit a use edge");
        Console.WriteLine($"PASS: {checks} washing/condiment assertions.");
    }
}
