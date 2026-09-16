using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured V13 native geometry/state with explicitly reconstructed planner ownership; later transitions are offline fixtures.</summary>
    public static int DirectEarlyOnionSelfTest(JsonObject at4216,JsonObject at2901)
    {
        int count=0;void Check(bool value,string text){if(!value)throw new InvalidOperationException("Direct leased-onion regression: "+text);count++;}
        void Reject(Action test,string text){bool failed=false;try{test();}catch(InvalidOperationException){failed=true;}catch(TimeoutException){failed=true;}Check(failed,text);}
        JsonObject NativeFood(FoodObservation f)=>new(){["type"]=f.Kind switch{FoodNodeKind.Ingredient=>"IngredientAssembledNode",FoodNodeKind.Cooked=>"CookedCompositeAssembledNode",FoodNodeKind.Mixed=>"MixedCompositeAssembledNode",_=>"CompositeAssembledNode"},
            ["id"]=f.IngredientId,["name"]=f.Name,["state"]=f.Kind==FoodNodeKind.Cooked?f.Preparation.ToString():null,["progress"]=f.Progress,["cookingStepId"]=f.CookingStepId,["children"]=new JsonArray(f.Children.Select(x=>(JsonNode)NativeFood(x)).ToArray())};
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline direct-onion test attempted I/O."),null)
            {response=at4216.DeepClone().AsObject(),options=new(EarlyOnionHeat:true,ParallelOnionHeat:true),recipes=new[]{158500,125780,224216,228996,47642,472326,257844,130976}.Select(CarnivalRecipes.GetRecipe).ToArray()};
            if(p.response["state"]is null)p.response=new JsonObject{["state"]=p.response};p.Refresh();p.delivered=4;
            p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline direct-onion action attempted I/O."),null);
            p.mealFoods[4]=185;p.mealFoods[5]=209;
            p.earlyOnion=new(4,4,16,40,3,3,2142){Phase=EarlyOnionPhase.Combining,PhaseFrame=4117,ParkedProgress=12.13332};
            p.secondEarlyOnion=new(5,9,21,38,8,-1,3325){Phase=EarlyOnionPhase.Heating,PhaseFrame=3582};
            foreach(var l in p.EarlyOnionLeases())p.reserved.UnionWith(l.Resources);
            p.assembling.Add(4);p.Start(3,"fixture-other-onion-work",[p.OnionAction("place",43)],[185,43]);
            return p;
        }
        void Cook(CarnivalPlanner p){p.Entity(9)!["cookingProgress"]=12.016655;p.Entity(9)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood.Children.Single(f=>f.CookingStepId==CarnivalRecipes.PanCookingStepId));}
        void Consume(CarnivalPlanner p){p.Entity(9)!["composition"]=NativeFood(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PanCookingStepId,0,[]));p.Entity(9)!["cookingProgress"]=0;p.Entity(9)!["ingredientIds"]=new JsonArray();p.Entity(9)!["contents"]=new JsonArray();p.Entity(209)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood);}
        void FinishNext(CarnivalPlanner p)
        {
            var work=p.workers[0]!;var spec=work.Actions.Dequeue();spec["player"]=0;work.Active=p.runner.CreateAction(spec);work.Active.IsDone=true;
            p.CompleteWork();if(p.secondEarlyOnion is {} lease)p.AdvanceEarlyOnion(lease,false);
        }
        var actual=Make();var target=actual.secondEarlyOnion!;
        Check(actual.Frame==4216 && N(actual.Entity(9)?["cookingProgress"]) is >10.58 and <10.59 && actual.Attached(23)==209 && actual.Held(0)==0,"actual second rescue has the observed plain hotdog while native onion is still cooking");
        Check(CarnivalRecipes.MatchRecipe(actual.Entity(209),296560,false).ReadyToDeliver && actual.Free(23,209),"native completed base on board23 is free after job completion4088");
        var unrelated=actual.reserved.ToHashSet();var otherWork=actual.workers[3];
        Check(actual.TryAdmitEarlyOnionRescue(target,0),"actual eligible rescue selects a legal direct harvest");
        Check(target.Phase==EarlyOnionPhase.DirectHarvest && actual.directEarlyOnions.ContainsKey(target),"direct phase replaces only this pan's approach/park sequence");
        var plan=actual.directEarlyOnions[target];var work=actual.workers[0]!;
        Check(work.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"take","navigate","cook","combine","place"}) && work.Actions.Select(a=>I(a["station"])).SequenceEqual(new[]{23,9,9,9,23}),"exact source, native cook barrier, original pan and original return target are retained");
        Check(work.OwnedResources.SetEquals(new[]{23,209}) && target.Resources.All(actual.reserved.Contains) && actual.assembling.Contains(5),"food/source Work ownership is separate from original pan/home/counter lease");
        Check(!actual.TryDirectEarlyOnionHarvest(target,3) && ReferenceEquals(otherWork,actual.workers[3]),"busy other chef and its original onion Work are preserved");
        Check(work.Actions.All(a=>!B(a["dash"])&&!B(a["shortDash"])) && !work.Actions.Any(a=>I(a["station"])==38),"default walks and never parks or carries the whole pan");
        actual.AdvanceEarlyOnion(target,false);Check(!plan.CookedObserved&&!plan.Harvested,"native Raw pan is not fabricated as cooked or consumed");
        var waitSpec=work.Actions.ElementAt(2).DeepClone().AsObject();waitSpec["player"]=0;var wait=actual.runner.CreateAction(waitSpec);var input=actual.runner.Tick(wait,actual.response);
        Check(!wait.IsDone&&N(input["x"])==0&&N(input["y"])==0&&!B(input["pickup"])&&!B(input["use"]),"native cook barrier outputs neutral until exact cooking completes");
        Reject(()=>work.Complete!(),"callback alone cannot certify a raw/onheat full pan");
        actual.Entity(23)!["attachedEntityId"]=0;actual.chefs[0]["heldEntityId"]=209;FinishNext(actual);FinishNext(actual);
        Cook(actual);actual.AdvanceEarlyOnion(target,false);actual.runner.Tick(wait,actual.response);
        Check(plan.CookedObserved&&wait.IsDone&&!plan.Harvested,"only native Cooked state releases the combine barrier");FinishNext(actual);
        Consume(actual);FinishNext(actual);
        Check(plan.Harvested&&actual.Attached(21)==9&&actual.Attached(38)==0&&actual.Held(0)==209,"exact native onion hotdog consumes contents while original empty pan remains on home");
        actual.Entity(23)!["attachedEntityId"]=209;actual.chefs[0]["heldEntityId"]=0;FinishNext(actual);
        Check(actual.secondEarlyOnion is null&&actual.directEarlyOnions.Count==0&&actual.workers[0]is null&&!actual.assembling.Contains(5),"original-source return completes and retires only selected direct lease");
        Check(actual.Attached(21)==9&&actual.EmptyFood(9)&&actual.Attached(38)==0&&actual.Attached(23)==209&&CarnivalRecipes.MatchRecipe(actual.Entity(209),472326,false).ReadyToDeliver,"final native shapes require exact empty home pan and unplated onion hotdog");
        Check(actual.reserved.SetEquals(unrelated.Except(target.Resources))&&ReferenceEquals(otherWork,actual.workers[3])&&actual.earlyOnion is not null,"other pan/home/counter and chef Work leases survive exact completion");
        Check(actual.Start(0,"fixture-successor-onion-source",[actual.A("take",23)],[23,209])&&actual.reserved.Contains(23),"native source becomes available to a successor only after final placement");
        foreach(string defect in new[]{"missing-base","reserved-source","reserved-food","wrong-recipe","already-assembling","wrong-source","late-pan","slow-chef","missing-ordinal","foreign-park","occupied-chef","blocked-path"})
        {
            var p=Make();var l=p.secondEarlyOnion!;var before=p.reserved.ToHashSet();
            switch(defect){case "missing-base":p.mealFoods.Remove(5);break;case "reserved-source":p.reserved.Add(23);break;case "reserved-food":p.reserved.Add(209);break;case "wrong-recipe":p.Entity(209)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood);break;case "already-assembling":p.assembling.Add(5);break;case "wrong-source":p.Entity(23)!["attachedEntityId"]=0;break;case "late-pan":p.Entity(9)!["cookingProgress"]=18;break;case "slow-chef":p.chefs[0]["runSpeed"]=.01;break;case "missing-ordinal":p.Entity(209)!.Remove("observedOrdinal");break;case "foreign-park":p.Entity(38)!["attachedEntityId"]=13;break;case "occupied-chef":p.Start(0,"fixture-existing-work",[p.A("wait",23)],[]);break;case "blocked-path":p.chefs[0]["position"]=new JsonObject{["x"]=20.4,["y"]=0,["z"]=-16.8};break;}
            var heldBefore=p.reserved.ToHashSet();var existing=p.workers[0];Check(!p.TryDirectEarlyOnionHarvest(l,0)&&l.Phase==EarlyOnionPhase.Heating&&ReferenceEquals(existing,p.workers[0])&&p.reserved.SetEquals(heldBefore),"refused direct admission changes no ownership: "+defect);
        }
        foreach(string defect in new[]{"food-inactive","food-replaced","pan-replaced","source-replaced","home-replaced","park-replaced","source-lease-lost","work-replaced","recipe-reassigned","native-reattachment","food-disappears","uncooked-consumption","wrong-consumed-recipe","guard-deadline","early-pan-empty"})
        {
            var p=Make();var l=p.secondEarlyOnion!;Check(p.TryDirectEarlyOnionHarvest(l,0),"prepare observer mutation: "+defect);
            switch(defect){case "food-inactive":p.Entity(209)!["active"]=false;break;case "food-replaced":p.Entity(209)!["observedOrdinal"]=999;break;case "pan-replaced":p.Entity(9)!["observedOrdinal"]=999;break;case "source-replaced":p.Entity(23)!["observedOrdinal"]=999;break;case "home-replaced":p.Entity(21)!["observedOrdinal"]=999;break;case "park-replaced":p.Entity(38)!["observedOrdinal"]=999;break;case "source-lease-lost":p.reserved.Remove(23);break;case "work-replaced":p.workers[0]=null;break;case "recipe-reassigned":p.mealFoods[5]=185;break;case "native-reattachment":p.Entity(21)!["attachedEntityId"]=0;p.Entity(38)!["attachedEntityId"]=9;break;case "food-disappears":p.Entity(23)!["attachedEntityId"]=0;break;case "uncooked-consumption":p.Entity(23)!["attachedEntityId"]=0;p.chefs[0]["heldEntityId"]=209;Consume(p);break;case "wrong-consumed-recipe":Cook(p);p.AdvanceEarlyOnion(l,false);p.Entity(23)!["attachedEntityId"]=0;p.chefs[0]["heldEntityId"]=209;Consume(p);p.Entity(209)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(296560).ExpectedFood);break;case "guard-deadline":p.Entity(9)!["cookingProgress"]=21;break;case "early-pan-empty":p.Entity(9)!["composition"]!["children"]=new JsonArray();break;}
            Reject(()=>p.AdvanceEarlyOnion(l,false),"per-frame native/ownership guard: "+defect);
        }
        var absent=Make();absent.response=at2901.DeepClone().AsObject();if(absent.response["state"]is null)absent.response=new JsonObject{["state"]=absent.response};absent.Refresh();absent.workers[0]=null;absent.workers[3]=null;absent.mealFoods.Clear();absent.assembling.Clear();absent.reserved.Clear();absent.secondEarlyOnion=null;
        absent.earlyOnion=new(4,4,16,40,3,-1,2142){Phase=EarlyOnionPhase.Heating,PhaseFrame=2318};absent.reserved.UnionWith(absent.earlyOnion.Resources);
        Check(absent.TryAdmitEarlyOnionRescue(absent.earlyOnion,0)&&absent.earlyOnion.Phase==EarlyOnionPhase.Approach&&absent.workers[0]!.Actions.Single()["type"]?.ToString()=="navigate","actual first rescue GF2901 without any plain base preserves existing whole-pan parking fallback");
        return count;
    }
}
