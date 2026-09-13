using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class ObservableActionTests
{
    public static void Run(JsonObject fixture)
    {
        int checks=0,networkCalls=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);checks++;}
        var runner=new RouteRunner(_=>{networkCalls++;throw new Exception("Tick must not perform network calls.");},null);
        var response=fixture.DeepClone().AsObject();var state=KitchenModel.SnapshotState(response);var model=KitchenModel.Build(response);
        var entities=state["entities"]!.AsArray();
        JsonObject Entity(int id)=>entities.OfType<JsonObject>().Single(e=>e["id"]!.GetValue<int>()==id);
        JsonObject Chef(int player)=>state["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==player);
        JsonObject Leaf(int id,string name)=>new(){["type"]="IngredientAssembledNode",["name"]=name,["id"]=id,["progress"]=-1,["children"]=new JsonArray()};
        bool Neutral(JsonObject input)=>!input["pickup"]!.GetValue<bool>()&&!input["use"]!.GetValue<bool>()&&!input["dash"]!.GetValue<bool>()&&KitchenModel.N(input["x"])==0&&KitchenModel.N(input["y"])==0;
        var pot=Entity(model.Stations.First(s=>s.Role=="pot").EntityId);
        pot["contents"]=new JsonArray(Leaf(CarnivalRecipes.Frankfurter.Id,"Frankfurter"));pot["ingredientIds"]=new JsonArray(CarnivalRecipes.Frankfurter.Id);pot["cookingTime"]=12;pot["cookingProgress"]=0;pot["cookingTypeId"]=CarnivalRecipes.PotCookingStepId;
        var cook=runner.CreateAction(new JsonObject{["player"]=0,["type"]="cook",["entityId"]=pot["id"]!.DeepClone()});
        Check(Neutral(runner.Tick(cook,state))&&!cook.IsDone,"cook waits neutrally at zero progress");
        pot["cookingProgress"]=11.99;runner.Tick(cook,state);Check(!cook.IsDone,"cook does not complete early");
        pot["cookingProgress"]=12;Check(Neutral(runner.Tick(cook,state))&&cook.IsDone&&cook.Error is null,"cook completes at native duration and releases");
        pot["cookingProgress"]=25;
        var burnt=runner.CreateAction(new JsonObject{["player"]=0,["type"]="cook",["entityId"]=pot["id"]!.DeepClone()});
        Check(Neutral(runner.Tick(burnt,state))&&burnt.Error?.Contains("burnt",StringComparison.Ordinal)==true,"burnt vessel rejected");
        pot["cookingProgress"]=0;
        var stall=runner.CreateAction(new JsonObject{["player"]=0,["type"]="cook",["entityId"]=pot["id"]!.DeepClone(),["maxStallFrames"]=2});
        for(int i=0;i<5&&!stall.IsDone;i++)runner.Tick(stall,state);
        Check(stall.Error?.Contains("progress",StringComparison.Ordinal)==true,"stalled native cooking fails with neutral output");
        var bowl=Entity(model.Stations.First(s=>s.Role=="bowl").EntityId);
        bowl["contents"]=new JsonArray(Leaf(CarnivalRecipes.Flour.Id,"Flour"),Leaf(CarnivalRecipes.Egg.Id,"Egg"),Leaf(CarnivalRecipes.Chocolate.Id,"Chocolate"));bowl["mixingProgress"]=0;bowl["mixingTime"]=12;bowl["cookingProgress"]=0;
        var mix=runner.CreateAction(new JsonObject{["player"]=0,["type"]="mix",["station"]="mixers"});
        runner.Tick(mix,state);Check(!mix.IsDone&&mix.Error is null,"mix alias resolves a nonempty bowl");
        bowl["mixingProgress"]=12;Check(Neutral(runner.Tick(mix,state))&&mix.IsDone&&mix.Error is null,"mix waits for native mixed state");
        bowl["composition"]=new JsonObject{["type"]="CookedCompositeAssembledNode",["state"]="Raw",["cookingStepId"]=32660,["progress"]=0,["children"]=new JsonArray(new JsonObject{["type"]="MixedCompositeAssembledNode",["state"]="Mixed",["progress"]=1.56144893,["children"]=bowl["contents"]!.DeepClone()})};
        var wrappedMix=runner.CreateAction(new JsonObject{["player"]=0,["type"]="mix",["entityId"]=bowl["id"]!.DeepClone()});
        Check(Neutral(runner.Tick(wrappedMix,state))&&wrappedMix.IsDone&&wrappedMix.Error is null,"mixed dough below native raw cooking wrapper completes mixing");

        var board=model.Stations.First(s=>s.Role=="chop"&&s.Regions.Contains("upper-left"));
        var approach=board.Approaches.First(a=>a.Region=="upper-left").Position;
        var chef=Chef(2);chef["position"]!["x"]=approach.X;chef["position"]!["z"]=approach.Z;chef["heldEntityId"]=0;chef["useTargetId"]=board.EntityId;chef["pickupTargetId"]=board.EntityId;chef["placementTargetId"]=board.EntityId;
        var raw=new JsonObject{["id"]=98760,["name"]="HotdogBun",["position"]=Entity(board.EntityId)["position"]!.DeepClone(),["components"]=new JsonArray("WorkableItem","CarryableItem"),["contents"]=new JsonArray(),["workProgress"]=0,["active"]=true};
        entities.Add(raw);Entity(board.EntityId)["attachedEntityId"]=98760;
        var chop=runner.CreateAction(new JsonObject{["player"]=2,["type"]="chop",["station"]=board.Key});int workTicks=0;
        JsonObject? prepared=null;
        for(int i=0;i<80&&!chop.IsDone;i++)
        {
            var input=runner.Tick(chop,state);
            if(input["use"]!.GetValue<bool>())
            {
                workTicks++;raw["workProgress"]=workTicks/7.0;
                if(workTicks==7)
                {
                    raw["active"]=false;
                    prepared=new JsonObject{["id"]=98761,["name"]="HotdogBun (Clone)",["position"]=raw["position"]!.DeepClone(),["components"]=new JsonArray("IngredientPropertiesComponent","CarryableItem"),["contents"]=new JsonArray(Leaf(CarnivalRecipes.Bun.Id,"HotdogBun")),["ingredientIds"]=new JsonArray(CarnivalRecipes.Bun.Id),["active"]=true};
                    entities.Add(prepared);Entity(board.EntityId)["attachedEntityId"]=98761;
                }
            }
        }
        Check(chop.IsDone&&chop.Error is null&&workTicks==7,"chop holds use until native raw-item replacement");
        Check(Neutral(runner.Tick(chop,state)),"completed action handle remains released");
        raw["active"]=true;raw["workProgress"]=0;Entity(board.EntityId)["attachedEntityId"]=raw["id"]!.DeepClone();chef["useSuppressed"]=true;
        var suppressedChop=runner.CreateAction(new JsonObject{["player"]=2,["type"]="chop",["station"]=board.Key});
        int suppressedChopPresses=0,resumedChopUses=0;bool previousChopUse=false;
        for(int frame=0;frame<80&&!suppressedChop.IsDone;frame++)
        {
            var input=runner.Tick(suppressedChop,state);bool use=input["use"]!.GetValue<bool>();
            if(chef["useSuppressed"]!.GetValue<bool>())
            {
                if(use)suppressedChopPresses++;
                if(previousChopUse&&!use)chef["useSuppressed"]=false;
            }
            else if(use)
            {
                resumedChopUses++;raw["workProgress"]=resumedChopUses/7.0;
                if(resumedChopUses==7){raw["active"]=false;Entity(board.EntityId)["attachedEntityId"]=prepared!["id"]!.DeepClone();}
            }
            previousChopUse=use;
        }
        Check(suppressedChop.IsDone&&suppressedChop.Error is null&&suppressedChopPresses==1&&resumedChopUses==7,"chop clears observed native use suppression through one verified press/release");
        var plate=Entity(model.Stations.First(s=>s.Role=="plate").EntityId);chef["heldEntityId"]=plate["id"]!.DeepClone();plate["contents"]=new JsonArray();plate["ingredientIds"]=new JsonArray();
        var combine=runner.CreateAction(new JsonObject{["player"]=2,["type"]="combine",["station"]=board.Key,["expectedIngredient"]="HotdogBun"});int edges=0;
        for(int i=0;i<60&&!combine.IsDone;i++)
        {
            var input=runner.Tick(combine,state);
            if(input["pickup"]!.GetValue<bool>())
            {
                edges++;plate["contents"]=prepared!["contents"]!.DeepClone();plate["ingredientIds"]=new JsonArray(CarnivalRecipes.Bun.Id);Entity(board.EntityId)["attachedEntityId"]=0;
            }
        }
        Check(combine.IsDone&&combine.Error is null&&edges==1,"combine verifies content addition with one pickup edge");
        Check(chef["heldEntityId"]!.GetValue<int>()==plate["id"]!.GetValue<int>(),"combine retains the same plate entity");
        plate["contents"]=new JsonArray();plate["composition"]=null;plate["ingredientIds"]=new JsonArray();
        Entity(board.EntityId)["attachedEntityId"]=prepared!["id"]!.DeepClone();
        double now=KitchenModel.N(state["clientTime"]),eligibleAt=now+.25;chef["lastPickupTimestamp"]=eligibleAt;
        var assemble=runner.CreateAction(new JsonObject{["player"]=2,["type"]="assemble",["station"]=board.Key,["expectedIngredient"]="HotdogBun"});
        int assemblyEdges=0,lastEdgeFrame=-10;bool cooldownRespected=true,releasedBetweenEdges=true;
        for(int i=0;i<100&&!assemble.IsDone;i++)
        {
            var input=runner.Tick(assemble,state);
            if(input["pickup"]!.GetValue<bool>())
            {
                assemblyEdges++;releasedBetweenEdges&=i-lastEdgeFrame>=3;lastEdgeFrame=i;
                if(assemblyEdges==1)
                {
                    // Native preparation-container behavior puts the plate
                    // beneath the food on the board, temporarily emptying hands.
                    chef["heldEntityId"]=0;Entity(board.EntityId)["attachedEntityId"]=plate["id"]!.DeepClone();
                    plate["contents"]=prepared["contents"]!.DeepClone();plate["ingredientIds"]=new JsonArray(CarnivalRecipes.Bun.Id);
                    plate["composition"]=new JsonObject{["type"]="CompositeAssembledNode",["children"]=prepared["contents"]!.DeepClone()};
                }
                else
                {
                    cooldownRespected&=now>=eligibleAt;chef["heldEntityId"]=plate["id"]!.DeepClone();Entity(board.EntityId)["attachedEntityId"]=0;
                }
            }
            now+=1.0/60;state["clientTime"]=now;
        }
        Check(assemble.IsDone&&assemble.Error is null&&assemblyEdges==2,"assemble recovers original plate after verified plate-under placement");
        Check(cooldownRespected&&releasedBetweenEdges,"assembly recovery respects next-eligible timestamp and fresh pickup edge");
        Check(chef["heldEntityId"]!.GetValue<int>()==plate["id"]!.GetValue<int>(),"recovered assembly retains original plate identity");
        chef["heldEntityId"]=0;
        var centerChef=Chef(0);centerChef["heldEntityId"]=plate["id"]!.DeepClone();
        var potStation=model.Stations.Single(s=>s.EntityId==pot["id"]!.GetValue<int>());
        var potApproach=potStation.Approaches.First(a=>a.Region=="center").Position;
        centerChef["position"]!["x"]=potApproach.X;centerChef["position"]!["z"]=potApproach.Z;
        centerChef["placementTargetId"]=potStation.EntityId;centerChef["pickupTargetId"]=potStation.EntityId;
        var noTransfer=runner.CreateAction(new JsonObject{["player"]=0,["type"]="combine",["station"]=potStation.Key,["maxRetries"]=0});
        for(int i=0;i<80&&!noTransfer.IsDone;i++)
        {
            runner.Tick(noTransfer,state);
            pot["cookingProgress"]=i*.01;pot["contents"]![0]!["progress"]=i*.01;pot["contents"]![0]!["state"]=i%2==0?"Raw":"Cooked";
        }
        Check(noTransfer.IsDone&&noTransfer.Error?.Contains("retries exhausted",StringComparison.Ordinal)==true,"timer and state-label changes alone cannot validate combine");
        Check(networkCalls==0,"independent per-chef ticks never advance or call the game");
        Console.WriteLine($"PASS: {checks} observable-preparation/continuous-action assertions.");
        WashingSwitchTests.Run(fixture);
    }
}
