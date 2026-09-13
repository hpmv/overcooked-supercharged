using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class RouteTests
{
    public static async Task Run(JsonObject fixture)
    {
        var response=fixture.DeepClone().AsObject();var state=KitchenModel.SnapshotState(response);
        var model=KitchenModel.Build(response);
        var chef=state["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==2);
        var crate=model.Resolve("HotdogBun");var board=model.Stations.Where(s=>s.Role=="chop"&&s.Regions.Contains("upper-left")).OrderBy(s=>s.Position.Z*-1).First();
        var boardEntity=state["entities"]!.AsArray().OfType<JsonObject>().Single(e=>e["id"]!.GetValue<int>()==board.EntityId);
        int frames=0,edges=0,checks=0;bool lastPickup=false;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);checks++;}
        async Task<JsonObject> FakeCall(JsonObject request)
        {
            await Task.CompletedTask;
            if(request["command"]?.ToString()=="step")
            {
                frames++;if(frames>1000)throw new Exception("Simulated route failed to terminate.");
                var input=request["inputs"]!.AsArray().OfType<JsonObject>().Single(i=>i["player"]!.GetValue<int>()==2);
                double x=KitchenModel.N(input["x"]),z=-KitchenModel.N(input["y"]),length=Math.Sqrt(x*x+z*z);
                var position=KitchenModel.Position(chef["position"]);
                if(length>1e-6)
                {
                    position=new(position.X+x*.07,position.Z+z*.07);
                    chef["position"]!["x"]=position.X;chef["position"]!["z"]=position.Z;
                    chef["forward"]!["x"]=x/length;chef["forward"]!["z"]=z/length;
                }
                int held=chef["heldEntityId"]!.GetValue<int>();
                chef["pickupTargetId"]=held==0&&position.Distance(crate.Position)<1.55?crate.EntityId:0;
                chef["placementTargetId"]=held!=0&&position.Distance(board.Position)<1.55?board.EntityId:0;
                bool pickup=input["pickup"]!.GetValue<bool>();
                if(pickup&&!lastPickup)
                {
                    edges++;
                    if(held==0&&chef["pickupTargetId"]!.GetValue<int>()==crate.EntityId)chef["heldEntityId"]=98765;
                    else if(held!=0&&chef["placementTargetId"]!.GetValue<int>()==board.EntityId)
                    {
                        boardEntity["attachedEntityId"]=held;chef["heldEntityId"]=0;
                    }
                    else throw new Exception("Pickup edge sent without a verified target.");
                }
                lastPickup=pickup;response["frame"]=frames;state["frame"]=frames;
            }
            return response.DeepClone().AsObject();
        }
        await new RouteRunner(FakeCall,null).RunAsync(Json.Object("""
        {"phases":[{"name":"take bun","actions":[{"player":2,"type":"take","station":"hotdogcrate","timeoutFrames":500}]},{"name":"place on board","actions":[{"player":2,"type":"place","station":"chopboards","timeoutFrames":500}]}]}
        """));
        Check(edges==2,"one pickup edge for take and one for placement");
        Check(chef["heldEntityId"]!.GetValue<int>()==0,"placement waits for hands empty");
        Check(boardEntity["attachedEntityId"]!.GetValue<int>()==98765,"unknown spawned id transferred to observed station");
        Check(!lastPickup,"final inputs released");
        Check(frames>20&&frames<500,"navigation FSM reaches both stations within bound");
        var rejected=fixture.DeepClone().AsObject();var rejectedState=KitchenModel.SnapshotState(rejected);
        var rejectedChef=rejectedState["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==2);
        var approach=crate.Approaches.First(a=>a.Region=="upper-left").Position;
        rejectedChef["position"]!["x"]=approach.X;rejectedChef["position"]!["z"]=approach.Z;
        rejectedChef["pickupTargetId"]=0;rejectedChef["placementTargetId"]=0;
        int invalidEdges=0;bool endedNeutral=false;
        async Task<JsonObject> WrongTarget(JsonObject request)
        {
            await Task.CompletedTask;
            if(request["command"]?.ToString()=="step")
            {
                var input=request["inputs"]!.AsArray().OfType<JsonObject>().Single(i=>i["player"]!.GetValue<int>()==2);
                if(input["pickup"]!.GetValue<bool>())invalidEdges++;
                endedNeutral=!input["pickup"]!.GetValue<bool>()&&KitchenModel.N(input["x"])==0&&KitchenModel.N(input["y"])==0;
            }
            return rejected.DeepClone().AsObject();
        }
        bool failed=false;
        try
        {
            await new RouteRunner(WrongTarget,null).RunAsync(Json.Object("""
            {"phases":[{"actions":[{"player":2,"type":"take","station":"hotdogcrate","maxRetries":0,"timeoutFrames":100}]}]}
            """));
        }
        catch(InvalidOperationException e) when(e.Message.Contains("retries exhausted",StringComparison.Ordinal)){failed=true;}
        Check(failed&&invalidEdges==0,"wrong native target never receives a pickup edge");
        Check(endedNeutral,"failed transfer releases all controls");
        Console.WriteLine($"PASS: {checks} adaptive-transfer simulation assertions in {frames} frames (no live game).");
        ObservableActionTests.Run(fixture);
    }
}
