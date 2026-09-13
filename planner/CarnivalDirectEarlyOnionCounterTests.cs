using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int DirectEarlyOnionCounterSelfTest(JsonObject at3581)
    {
        int count=0;void Check(bool value,string message){if(!value)throw new InvalidOperationException("Direct onion ordinary-counter regression: "+message);count++;}
        JsonObject NativeFood(FoodObservation f)=>new(){["type"]=f.Kind switch{FoodNodeKind.Ingredient=>"IngredientAssembledNode",FoodNodeKind.Cooked=>"CookedCompositeAssembledNode",_=>"CompositeAssembledNode"},["id"]=f.IngredientId,["name"]=f.Name,["state"]=f.Kind==FoodNodeKind.Cooked?f.Preparation.ToString():null,["progress"]=f.Progress,["cookingStepId"]=f.CookingStepId,["children"]=new JsonArray(f.Children.Select(x=>(JsonNode)NativeFood(x)).ToArray())};
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline counter fixture attempted I/O."),null)
            {response=at3581.DeepClone().AsObject(),options=new(EarlyOnionHeat:true,ParallelOnionHeat:true),recipes=new[]{158500,125780,224216,228996,47642,472326,257844,130976}.Select(CarnivalRecipes.GetRecipe).ToArray()};
            if(p.response["state"]is null)p.response=new JsonObject{["state"]=p.response};p.Refresh();p.delivered=4;
            p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline counter action attempted I/O."),null);
            p.mealFoods[4]=170;p.earlyOnion=new(4,4,16,61,3,-1,2899){Phase=EarlyOnionPhase.Heating,PhaseFrame=3042};p.reserved.UnionWith(p.earlyOnion.Resources);return p;
        }
        var actual=Make();var lease=actual.earlyOnion!;
        Check(actual.Frame==3581&&actual.Attached(38)==170&&actual.Attached(16)==4&&N(actual.Entity(4)?["cookingProgress"]) is >9 and <9.01,"exact observed V14 ordinary-source opportunity");
        Check(actual.Station(38)!.Role=="counter"&&KitchenModel.Components(actual.Entity(38)!).Contains("AttachStation")&&!KitchenModel.Components(actual.Entity(38)!).Contains("Workstation"),"actual ordinary counter has native attachment but no Workstation component");
        Check(actual.NativeDirectOnionSource(38)&&actual.NativeDirectOnionSource(23),"ordinary counter and chopping board both satisfy native source contract");
        Check(actual.TryAdmitEarlyOnionRescue(lease,0)&&lease.Phase==EarlyOnionPhase.DirectHarvest,"actual free plain170 on ordinary38 selects direct harvest");
        var plan=actual.directEarlyOnions[lease];var work=actual.workers[0]!;
        Check(work.Actions.Select(a=>I(a["station"])).SequenceEqual(new[]{38,4,4,4,38})&&work.OwnedResources.SetEquals(new[]{38,170}),"native source and return stay the same ordinary counter with exact food/source leases");
        actual.Entity(38)!["attachedEntityId"]=0;actual.chefs[0]["heldEntityId"]=170;actual.AdvanceEarlyOnion(lease,false);
        actual.Entity(4)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood.Children.Single(n=>n.CookingStepId==CarnivalRecipes.PanCookingStepId));actual.Entity(4)!["cookingProgress"]=12.02;actual.AdvanceEarlyOnion(lease,false);
        Check(plan.CookedObserved&&!plan.Harvested,"native Cooked observation precedes ordinary-source food consumption");
        actual.Entity(4)!["composition"]=NativeFood(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PanCookingStepId,0,[]));actual.Entity(4)!["ingredientIds"]=new JsonArray();actual.Entity(4)!["contents"]=new JsonArray();actual.Entity(4)!["cookingProgress"]=0;actual.Entity(170)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood);actual.AdvanceEarlyOnion(lease,false);
        Check(plan.Harvested&&actual.Attached(16)==4&&actual.Attached(61)==0,"whole pan remains on home while cooked onion enters original held170");
        actual.Entity(38)!["attachedEntityId"]=170;actual.chefs[0]["heldEntityId"]=0;work.Actions.Clear();var final=actual.OnionAction("place",38);final["player"]=0;work.Active=actual.runner.CreateAction(final);work.Active.IsDone=true;actual.CompleteWork();
        Check(actual.earlyOnion is null&&actual.reserved.Count==0&&actual.directEarlyOnions.Count==0&&actual.Attached(38)==170&&CarnivalRecipes.MatchRecipe(actual.Entity(170),472326,false).ReadyToDeliver,"exact ordinary-counter return retires only completed native resources");
        foreach(string defect in new[]{"cooker","mixer","no-attach","reserved-source","reserved-food","inactive-source","source-replaced"})
        {
            var p=Make();var l=p.earlyOnion!;
            switch(defect){case "cooker":p.Entity(38)!["components"]!.AsArray().Add("CookingStation");break;case "mixer":p.Entity(38)!["components"]!.AsArray().Add("MixingStation");break;case "no-attach":var cs=p.Entity(38)!["components"]!.AsArray();cs.Remove(cs.Single(x=>x?.ToString()=="AttachStation"));break;case "reserved-source":p.reserved.Add(38);break;case "reserved-food":p.reserved.Add(170);break;case "inactive-source":p.Entity(38)!["active"]=false;break;case "source-replaced":p.Entity(38)!.Remove("observedOrdinal");break;}
            var before=p.reserved.ToHashSet();Check(!p.TryDirectEarlyOnionHarvest(l,0)&&p.workers[0]is null&&l.Phase==EarlyOnionPhase.Heating&&p.reserved.SetEquals(before),"unsafe source admission leaves original rescue available: "+defect);
        }
        foreach(string kind in new[]{"CookingStation","MixingStation"})
        {
            var p=Make();var l=p.earlyOnion!;Check(p.TryDirectEarlyOnionHarvest(l,0),"prepare active source mutation: "+kind);p.Entity(38)!["components"]!.AsArray().Add(kind);bool rejected=false;try{p.AdvanceEarlyOnion(l,false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"per-frame source guard rejects processing role mutation: "+kind);
        }
        return count;
    }
}
