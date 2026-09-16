using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured V14 GF7228 observations with explicit synthetic planner ownership and native-state mutations; no I/O.</summary>
    public static int DirectCleanPassSelfTest(JsonObject at7228)
    {
        int count=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("Direct clean-pass regression: "+message);count++;}
        void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline clean-pass fixture attempted I/O."),null)
            {response=at7228.DeepClone().AsObject(),options=new(DirectCleanPassAssembly:true,ServiceSideHead:true),
                recipes=Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560),96).ToArray()};
            p.Refresh();p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline clean-pass fixture attempted I/O."),null);
            p.recipes[9]=CarnivalRecipes.GetRecipe(47642);p.recipes[10]=CarnivalRecipes.GetRecipe(130976);p.recipes[11]=CarnivalRecipes.GetRecipe(158500);
            p.mealFoods[9]=236;p.mealFoods[11]=245;
            return p;
        }
        void Pickup(CarnivalPlanner p)
        {p.Entity(44)!["attachedEntityId"]=0;p.chefs[3]["heldEntityId"]=272;p.ObserveDirectCleanPasses();}
        JsonObject NativeFood(FoodObservation food)=>new()
        {
            ["type"]=food.Kind switch{FoodNodeKind.Ingredient=>"IngredientAssembledNode",FoodNodeKind.Cooked=>"CookedCompositeAssembledNode",FoodNodeKind.Mixed=>"MixedCompositeAssembledNode",_=>"CompositeAssembledNode"},
            ["id"]=food.IngredientId,["state"]=food.Preparation.ToString(),["cookingStepId"]=food.CookingStepId,
            ["children"]=new JsonArray(food.Children.Select(c=>(JsonNode?)NativeFood(c)).ToArray())
        };
        void FinishState(CarnivalPlanner p)
        {
            p.chefs[3]["heldEntityId"]=0;p.Entity(38)!["attachedEntityId"]=0;p.Entity(245)!["active"]=false;
            p.Entity(49)!["attachedEntityId"]=272;p.Entity(272)!["composition"]=NativeFood(CarnivalRecipes.GetRecipe(158500).ExpectedFood);
            p.Refresh();p.ObserveDirectCleanPasses();
        }
        var disabled=Make();disabled.options=disabled.options with{DirectCleanPassAssembly=false};
        string untouched=disabled.response.ToJsonString();
        Check(!disabled.TryDirectCleanPassAssembly(3)&&disabled.workers.All(w=>w is null)&&disabled.directCleanPasses.Count==0,"default-off emits no work or observation changes");
        Check(untouched==disabled.response.ToJsonString(),"default-off leaves the raw native snapshot unchanged");
        var p=Make();string before=p.response.ToJsonString();
        Check(!p.TryAssemble(3,true),"actual FIFO onion head still lacks its prepared onion base");
        Check(p.AvailablePlate(3)==249,"ordinary nearest-plate choice is the other native clean plate249");
        Check(p.TryDirectCleanPassAssembly(3),"captured exact clean272 on44 and prepared245 on38 admit future Mustard assembly");
        var selected=p.directCleanPasses.Values.Single();var work=p.workers[3]!;
        Check(selected.Index==11&&selected.RecipeId==158500&&selected.Plate==272&&selected.PlateOrdinal==271&&selected.Source==44&&selected.SourceOrdinal==43&&selected.Output==49,"recorded plate, source, recipe and output remain exact");
        Check(work.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"switch-condiment","take","assemble","apply","place"}),"ordinary serial assembly action order is preserved");
        Check(I(work.Actions.ElementAt(1)["station"])==272&&I(work.Actions.ElementAt(2)["station"])==38&&I(work.Actions.Last()["station"])==49,"exact handoff plate is used without an intermediate storage stop");
        Check(work.OwnedResources.Contains(272)&&!work.OwnedResources.Contains(249)&&!work.OwnedResources.Contains(44)&&p.reserved.Contains(272),"ordinary work exclusively leases exact plate while native source occupancy protects initial pickup");
        Check(before==p.response.ToJsonString(),"admission changes only planner ownership, not native observations");
        p.ObserveDirectCleanPasses();Check(!selected.PickedUp,"occupied44 cannot be mistaken for selected-chef pickup");
        Pickup(p);Check(selected.PickedUp,"exact selected chef holding clean272 and empty44 prove pickup");
        // Explicit synthetic successor: the washer reuses native-empty44 while
        // the original plate is being assembled elsewhere.
        p.Entity(41)!["attachedEntityId"]=0;p.Entity(44)!["attachedEntityId"]=249;
        var successor=new Work("successor-clean-handoff",[],[44,249],null);p.workers[1]=successor;p.reserved.UnionWith([44,249]);
        p.ObserveDirectCleanPasses();Check(ReferenceEquals(p.workers[1],successor),"a successor washer plate can reuse44 immediately after verified pickup");
        FinishState(p);
        Check(CarnivalRecipes.MatchRecipe(p.Entity(272),158500).ReadyToDeliver,"synthetic completion requires the existing native recipe classifier");
        var final=work.Actions.Last().DeepClone().AsObject();final["player"]=3;work.Actions.Clear();work.Active=p.runner.CreateAction(final);work.Active.IsDone=true;
        p.CompleteWork();
        Check(p.directCleanPasses.Count==0&&p.workers[3] is null&&p.mealPlates.GetValueOrDefault(11)==272&&!p.mealFoods.ContainsKey(11)&&!p.plating.Contains(11)&&!p.assembling.Contains(11),"ordinary completion closes exact plate selection and recipe ownership");
        Check(ReferenceEquals(p.workers[1],successor)&&p.reserved.Contains(44)&&p.reserved.Contains(249)&&!p.reserved.Contains(272),"old completion cannot release the washer successor's source or plate lease");
        foreach(string mutation in new[]{"source-lease","plate-lease","plate-nonempty","plate-ordinal-missing","source-ordinal-missing","negative-ordinal","source-empty","plate-inactive","food-lease","source-food-lease","outputs-occupied","sauce-lease","last-clean-plate","held-helper","busy-helper","suppressed-helper","heat-blocked","missing-base","two-sauces"})
        {
            var q=Make();
            switch(mutation)
            {
                case "source-lease":q.reserved.Add(44);break;
                case "plate-lease":q.reserved.Add(272);break;
                case "plate-nonempty":q.Entity(272)!["composition"]=q.Entity(245)!["composition"]!.DeepClone();break;
                case "plate-ordinal-missing":q.Entity(272)!.Remove("observedOrdinal");break;
                case "source-ordinal-missing":q.Entity(44)!.Remove("observedOrdinal");break;
                case "negative-ordinal":q.Entity(272)!["observedOrdinal"]=-1;break;
                case "source-empty":q.Entity(44)!["attachedEntityId"]=0;break;
                case "plate-inactive":q.Entity(272)!["active"]=false;q.Refresh();break;
                case "food-lease":q.reserved.Add(245);break;
                case "source-food-lease":q.reserved.Add(38);break;
                case "outputs-occupied":q.reserved.UnionWith([49,52]);break;
                case "sauce-lease":q.reserved.Add(72);break;
                case "last-clean-plate":foreach(var plate in q.AvailablePlates().Where(e=>Id(e)!=272))q.reserved.Add(Id(plate));break;
                case "held-helper":q.chefs[3]["heldEntityId"]=249;break;
                case "busy-helper":q.workers[3]=new Work("original-work",[],[],null);break;
                case "suppressed-helper":q.chefs[3]["inputSuppressed"]=true;break;
                case "heat-blocked":q.heatSafetyBlocked.Add(3);break;
                case "missing-base":q.mealFoods.Remove(11);break;
                case "two-sauces":q.recipes[11]=CarnivalRecipes.ById.Values.Single(r=>r.RequiredInputs.Contains(CarnivalRecipes.Mustard)&&r.RequiredInputs.Contains(CarnivalRecipes.Ketchup)&&!r.RequiredInputs.Contains(CarnivalRecipes.Onion));break;
            }
            Check(!q.TryDirectCleanPassAssembly(3)&&q.directCleanPasses.Count==0,"admission preserves "+mutation);
        }
        foreach(string mutation in new[]{"plate-incarnation","source-incarnation","source-inactive","plate-moved","wrong-chef","recipe","owner-lost","plate-lease-lost","second-owner"})
        {
            var q=Make();Check(q.TryDirectCleanPassAssembly(3),"observer fixture starts before "+mutation);
            switch(mutation)
            {
                case "plate-incarnation":q.Entity(272)!["observedOrdinal"]=999;break;
                case "source-incarnation":q.Entity(44)!["observedOrdinal"]=999;break;
                case "source-inactive":q.Entity(44)!["active"]=false;q.Refresh();break;
                case "plate-moved":q.Entity(44)!["attachedEntityId"]=0;break;
                case "wrong-chef":q.Entity(44)!["attachedEntityId"]=0;q.chefs[0]["heldEntityId"]=272;break;
                case "recipe":q.recipes[11]=CarnivalRecipes.GetRecipe(296560);break;
                case "owner-lost":q.workers[3]=new Work("replacement",[],[],null);break;
                case "plate-lease-lost":q.reserved.Remove(272);break;
                case "second-owner":q.workers[0]=new Work("overlap",[],[272],null);break;
            }
            Reject(q.ObserveDirectCleanPasses,"active observer rejects "+mutation);
        }
        var paused=Make();paused.TryDirectCleanPassAssembly(3);var original=paused.workers[3]!;
        paused.cannonInterruptions.Add(3,new CannonInterruption(3,original,84,78,paused.Frame));paused.workers[3]=new Work("bounded-fire",[],[78],null);
        paused.ObserveDirectCleanPasses();Check(ReferenceEquals(paused.directCleanPasses.Values.Single().Work,original),"exact original owner is retained across an existing cannon boundary interruption");
        var badFinal=Make();badFinal.TryDirectCleanPassAssembly(3);Pickup(badFinal);FinishState(badFinal);badFinal.Entity(49)!["attachedEntityId"]=249;
        Reject(()=>badFinal.CompleteDirectCleanPass(3,272,158500),"final classifier match alone cannot replace same-plate output proof");
        var exactUnavailable=Make();exactUnavailable.reserved.Add(272);
        Check(!exactUnavailable.TryAssemble(3,exactIndex:11,exactPlate:272),"explicit unavailable plate cannot silently fall back to another clean plate");
        return count;
    }
}
