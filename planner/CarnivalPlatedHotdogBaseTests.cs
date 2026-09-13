using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int PlatedHotdogBaseSelfTest(JsonObject at789,JsonObject at6377,JsonObject preview)
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Plate-first base regression: "+message);count++;}
        void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(TimeoutException){rejected=true;}Check(rejected,message);}
        CarnivalPlanner Make()
        {
            var q=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline plate-first test attempted native I/O."),null)
            {response=at789.DeepClone().AsObject(),options=new(PlatedHotdogBase:true,NearReadyPotHarvest:true,ServiceSideHead:true,BufferChoppedBuns:true)};
            q.Refresh();q.runner=new(_=>throw new InvalidOperationException("Offline plate-first route attempted I/O."),null);
            q.recipes=(preview["response"]?["preview"]??preview["preview"]??preview)["recipes"]!.AsArray().OfType<JsonObject>().Select(r=>CarnivalRecipes.GetRecipe(I(r["recipeId"]))).ToArray();
            q.CapturePotHomes();q.bufferedBun=143;q.bunBufferCounter=32;return q;
        }
        JsonObject Node(FoodObservation f)=>new(){["type"]=f.Kind switch{FoodNodeKind.Ingredient=>"IngredientAssembledNode",FoodNodeKind.Cooked=>"CookedCompositeAssembledNode",FoodNodeKind.Mixed=>"MixedCompositeAssembledNode",_=>"CompositeAssembledNode"},
            ["id"]=f.IngredientId,["state"]=f.Preparation.ToString(),["cookingStepId"]=f.CookingStepId,["children"]=new JsonArray(f.Children.Select(c=>(JsonNode?)Node(c)).ToArray())};
        bool Admit(CarnivalPlanner q)=>q.TryPlatedHotdogBase(3,0,32,143,7,true,true);
        void Tick(CarnivalPlanner q,int n=1){q.state["gameplayFrame"]=q.Frame+n;q.state["timer"]=N(q.state["timer"])-n/60d;}
        void Observe(CarnivalPlanner q)=>q.ObservePlatedHotdogBases();
        void Pickup(CarnivalPlanner q)
        {var p=q.platedBases.Values.Single();q.Entity(p.PlateSource)!["attachedEntityId"]=0;q.chefs[3]["heldEntityId"]=p.Plate;Tick(q);Observe(q);}
        void BunUnder(CarnivalPlanner q)
        {
            var p=q.platedBases.Values.Single();q.Entity(p.Plate)!["composition"]=q.Entity(143)!["composition"]!.DeepClone();
            q.Entity(143)!["active"]=false;q.Entity(32)!["attachedEntityId"]=p.Plate;q.chefs[3]["heldEntityId"]=0;Tick(q);q.Refresh();Observe(q);
        }
        void RemoveBun(CarnivalPlanner q)
        {
            var e=q.LastPlatedSourceRegistry(143)!.DeepClone().AsObject();e["kind"]="remove";e["sequence"]=99999L;
            q.state["entityRegistration"]!["events"]!.AsArray().Add(e);Tick(q);Observe(q);
        }
        void Recover(CarnivalPlanner q){var p=q.platedBases.Values.Single();q.Entity(32)!["attachedEntityId"]=0;q.chefs[3]["heldEntityId"]=p.Plate;Tick(q);Observe(q);}
        void Cook(CarnivalPlanner q)
        {
            q.Entity(7)!["cookingProgress"]=12.0167;q.Entity(7)!["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood.Children.Single(f=>f.CookingStepId==CarnivalRecipes.PotCookingStepId));
            Tick(q);Observe(q);
        }
        void Consume(CarnivalPlanner q)
        {
            var p=q.platedBases.Values.Single();q.Entity(7)!["composition"]=Node(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PotCookingStepId,0,[]));
            q.Entity(7)!["cookingProgress"]=0;q.Entity(7)!["contents"]=new JsonArray();q.Entity(7)!["ingredientIds"]=new JsonArray();
            q.Entity(p.Plate)!["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood);Tick(q);Observe(q);
        }
        string Fingerprint(CarnivalPlanner q)=>new JsonObject{["native"]=q.response.DeepClone(),["status"]=q.Status(),
            ["reserved"]=JsonSerializer.SerializeToNode(q.reserved.Order()),["foods"]=JsonSerializer.SerializeToNode(q.mealFoods),
            ["plates"]=JsonSerializer.SerializeToNode(q.mealPlates),["assembling"]=JsonSerializer.SerializeToNode(q.assembling),["plating"]=JsonSerializer.SerializeToNode(q.plating)}.ToJsonString();
        var off=Make();off.options=off.options with{PlatedHotdogBase=false};string before=Fingerprint(off);
        Check(!Admit(off)&&before==Fingerprint(off),"default-off has no ownership or native mutation");
        Check(off.BuildUnplatedHotdog(3,7,true)&&off.workers[3]!.Name=="unplated-hotdog-1","default-off preserves original exact-pot/buffer candidate and fallback");
        var q=Make();string native=q.response.ToJsonString();Check(q.Frame==789&&q.delivered==0&&q.NativeNearReadyPot(7)&&q.Attached(32)==143&&q.IsLooseChoppedBun(143),"unchanged native789 has original11s pot7 and exact buffered chopped bun143");
        Check(!q.TryPlatedHotdogBase(3,0,32,143,7,true,false),"raw11s native pot cannot enter without original explicit exact-near-ready request");
        Check(q.BuildUnplatedHotdog(3,7,true)&&q.workers[3]!.Name=="plated-hotdog-base-1","original selected candidate enters plate-first branch without searching another recipe/source/pot");
        var p=q.platedBases.Values.Single();var work=q.workers[3]!;
        Check(p.Index==0&&p.Recipe==158500&&p.Board==32&&p.Bun==143&&p.Pot==7&&p.Home==19&&p.UsesBuffer&&q.response.ToJsonString()==native,"native observations and exact original selection remain unchanged at admission");
        Check(work.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"switch-condiment","take","assemble","navigate","cook","combine","apply","place"}),"native bun-only plate preparation precedes cooked-pot transfer and one condiment");
        Check(work.OwnedResources.SetEquals(p.Resources)&&p.Resources.Contains(7)&&p.Resources.Contains(19)&&!p.Resources.Contains(p.PlateSource)&&
            !work.Actions.Any(a=>I(a["station"])==7&&a["type"]?.ToString()=="take"),"original pot stays home and exact Work keeps all proposed source/plate/pot/output resources");
        Check(q.assembling.Contains(0)&&q.plating.Contains(0)&&!q.mealFoods.ContainsKey(0)&&!q.mealPlates.ContainsKey(0)&&q.bufferedBun==143,"in-progress plate does not create synthetic unplated or completed meal");
        Check(p.Deadline-p.Started<=900&&N(p.Evidence["estimatedHeatSeconds"])<23-N(q.Entity(7)?["cookingProgress"]),"exact conservative through-pot budget respects native23s guard and fixed cap");
        Observe(q);Reject(()=>work.Complete!(),"callback alone cannot certify an unfinished native base");
        var spec=work.Actions.Single(a=>a["type"]?.ToString()=="cook").DeepClone().AsObject();spec["player"]=3;var wait=q.runner.CreateAction(spec);var input=q.runner.Tick(wait,q.response);
        Check(!wait.IsDone&&!B(input["pickup"])&&!B(input["use"])&&N(input["x"])==0&&N(input["y"])==0,"native cook wait is neutral while sausage remains raw");
        Pickup(q);BunUnder(q);Check(p.PickedUp&&p.BunAcquired&&!p.RemovalObserved&&q.NativeBunOnlyPlate(p.Plate)&&q.Held(3)==0,"native chopped-bun-only plate-under state is distinct from completed hotdog");
        RemoveBun(q);Recover(q);Check(p.RemovalObserved&&q.Held(3)==p.Plate&&q.Attached(32)==0,"matching old source removal and same-plate recovery precede pot transfer");
        Cook(q);q.runner.Tick(wait,q.response);Check(p.CookedObserved&&wait.IsDone&&!p.Harvested,"only actual Cooked sausage releases native wait");
        Consume(q);Check(p.Harvested&&q.NativeEmptyPot(7)&&q.Attached(19)==7&&CarnivalRecipes.MatchRecipe(q.Entity(p.Plate),296560).ReadyToDeliver,"exact cooked sausage produces plain held plate and resets original home pot");
        q.Entity(p.Plate)!["composition"]=Node(CarnivalRecipes.GetRecipe(p.Recipe).ExpectedFood);q.Entity(72)!["switchIndex"]=0;Tick(q);Observe(q);
        Check(p.SauceObserved&&q.bufferedBun==143&&work.OwnedResources.Contains(32),"exact Mustard observed; buffer metadata/source stay owned until final native completion");
        var successor=new Work("independent-origin-user",[],[p.PlateSource],null);q.workers[0]=successor;q.reserved.Add(p.PlateSource);
        q.chefs[3]["heldEntityId"]=0;q.Entity(p.Output)!["attachedEntityId"]=p.Plate;Tick(q);Observe(q);
        work.Actions.Clear();var last=q.A("place",p.Output);last["player"]=3;work.Active=q.runner.CreateAction(last);work.Active.IsDone=true;q.CompleteWork();
        Check(q.workers[3]is null&&q.platedBases.Count==0&&q.mealPlates.GetValueOrDefault(0)==p.Plate&&!q.mealFoods.ContainsKey(0)&&q.bufferedBun==0&&q.bunBufferCounter==0,"only native final plate callback completes recipe and retires consumed buffer");
        Check(q.reserved.SetEquals([p.PlateSource])&&ReferenceEquals(q.workers[0],successor)&&!q.assembling.Contains(0)&&!q.plating.Contains(0),"original callback preserves successor origin ownership and releases only its retained resources");
        var cooked=Make();Cook(cooked);Check(cooked.TryPlatedHotdogBase(3,0,32,143,7,true,false),"synthetic native Cooked on-home pot may enter ordinary non-wait admission");
        var earlierOnion=Make();earlierOnion.recipes[0]=CarnivalRecipes.GetRecipe(472326);earlierOnion.recipes[1]=CarnivalRecipes.GetRecipe(158500);
        Check(earlierOnion.BuildUnplatedHotdog(3,7,true)&&earlierOnion.workers[3]!.Name=="unplated-hotdog-1"&&earlierOnion.platedBases.Count==0,"unsupported earlier onion candidate retains original base production instead of being bypassed for a later supported meal");
        var parked=Make();parked.response=at6377.DeepClone().AsObject();parked.Refresh();int parkedBun=parked.Attached(23);
        Check(parked.Attached(19)==0&&parked.AttachmentParent(7)==33&&!parked.TryPlatedHotdogBase(0,12,23,parkedBun,7,false,false),"actual6377 parked pot remains excluded; persistent restoration contract stays in original fallback");
        foreach(string defect in new[]{"no-option","no-near-option","wrong-pot","wrong-bun","source-lease","plate-all-leased","home-lease","condiment-lease","source-inactive","missing-ordinal","no-registry","moved-home","parked-pot","slow-owner","busy-owner","moving-owner","suppressed-owner","missing-clock","nan-clock","negative-clock","late-pot","no-output","existing-base","two-sauces","onions","buffer-replaced"})
        {
            var z=Make();switch(defect)
            {
                case "no-option":z.options=z.options with{PlatedHotdogBase=false};break;case "no-near-option":z.options=z.options with{NearReadyPotHarvest=false};break;
                case "wrong-pot":z.Entity(7)!["composition"]=Node(CarnivalRecipes.GetRecipe(472326).ExpectedFood);break;
                case "wrong-bun":z.Entity(143)!["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood);break;
                case "source-lease":z.reserved.Add(32);break;case "plate-all-leased":z.reserved.UnionWith([10,11,12,13]);break;case "home-lease":z.reserved.Add(19);break;case "condiment-lease":z.reserved.Add(72);break;
                case "source-inactive":z.Entity(143)!["active"]=false;break;case "missing-ordinal":z.Entity(143)!.Remove("observedOrdinal");break;case "no-registry":z.state["entityRegistration"]!["installed"]=false;break;
                case "moved-home":z.Entity(19)!["observedOrdinal"]=999;break;case "parked-pot":z.Entity(19)!["attachedEntityId"]=0;z.Entity(33)!["attachedEntityId"]=7;break;
                case "slow-owner":z.chefs[3]["runSpeed"]=.01;break;case "busy-owner":z.workers[3]=new("other",[],[],null);break;case "moving-owner":z.chefs[3]["lastVelocity"]!["x"]=1;break;case "suppressed-owner":z.chefs[3]["useSuppressed"]=true;break;
                case "missing-clock":z.Entity(7)!.Remove("cookingProgress");break;case "nan-clock":z.Entity(7)!["cookingProgress"]="NaN";break;case "negative-clock":z.Entity(7)!["cookingProgress"]=-1;break;case "late-pot":z.Entity(7)!["cookingProgress"]=23;break;
                case "no-output":z.reserved.UnionWith([47,49,52]);break;case "existing-base":z.mealFoods[0]=143;break;case "two-sauces":z.recipes[0]=CarnivalRecipes.GetRecipe(125780);break;case "onions":z.recipes[0]=CarnivalRecipes.GetRecipe(47642);break;case "buffer-replaced":z.bufferedBun=147;break;
            }
            var locks=z.reserved.ToHashSet();var owner=z.workers[3];Check(!Admit(z)&&locks.SetEquals(z.reserved)&&ReferenceEquals(z.workers[3],owner),"fail-closed admission preserves original ownership: "+defect);
        }
        foreach(string defect in new[]{"plate-reuse","bun-reuse","pot-reuse","home-reuse","board-reuse","output-reuse","registry-reuse","work-replaced","lease-lost","recipe-reassigned","pantry-took-bun","pot-refilled","wrong-cooked-result","premature-empty","wrong-sauce","source-no-remove","control-loss","late-pot","fixed-deadline","nan-clock","foreign-output","wrong-holder"})
        {
            var z=Make();Check(Admit(z),"prepare observer mutation: "+defect);var selected=z.platedBases.Values.Single();
            switch(defect)
            {
                case "plate-reuse":z.Entity(selected.Plate)!["observedOrdinal"]=999;break;case "bun-reuse":z.Entity(143)!["observedOrdinal"]=999;break;case "pot-reuse":z.Entity(7)!["observedOrdinal"]=999;break;case "home-reuse":z.Entity(19)!["observedOrdinal"]=999;break;case "board-reuse":z.Entity(32)!["observedOrdinal"]=999;break;case "output-reuse":z.Entity(selected.Output)!["observedOrdinal"]=999;break;
                case "registry-reuse":z.LastPlatedSourceRegistry(143)!["entity"]!["observedRegistrationSequence"]=999L;break;case "work-replaced":z.workers[3]=new("replacement",[],[],null);break;case "lease-lost":z.reserved.Remove(7);break;case "recipe-reassigned":z.recipes[0]=CarnivalRecipes.GetRecipe(224216);break;
                case "pantry-took-bun":z.Entity(32)!["attachedEntityId"]=0;z.chefs[2]["heldEntityId"]=143;break;
                case "premature-empty":z.Entity(7)!["composition"]=Node(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PotCookingStepId,0,[]));z.Entity(7)!["cookingProgress"]=0;break;
                case "pot-refilled":Pickup(z);BunUnder(z);RemoveBun(z);Recover(z);Cook(z);Consume(z);z.Entity(7)!["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood.Children.Single(f=>f.CookingStepId==CarnivalRecipes.PotCookingStepId));break;
                case "wrong-cooked-result":Pickup(z);BunUnder(z);RemoveBun(z);Recover(z);Cook(z);z.Entity(selected.Plate)!["composition"]=Node(CarnivalRecipes.GetRecipe(472326).ExpectedFood);break;
                case "wrong-sauce":Pickup(z);BunUnder(z);RemoveBun(z);Recover(z);Cook(z);Consume(z);z.Entity(selected.Plate)!["composition"]=Node(CarnivalRecipes.GetRecipe(224216).ExpectedFood);break;
                case "source-no-remove":Pickup(z);BunUnder(z);Tick(z,4);break;case "control-loss":z.chefs[3]["controlsEnabled"]=false;break;case "late-pot":z.Entity(7)!["cookingProgress"]=23;break;
                case "fixed-deadline":z.state["gameplayFrame"]=selected.Deadline+1;break;case "nan-clock":z.Entity(7)!["cookingProgress"]="NaN";break;case "foreign-output":z.Entity(selected.Output)!["attachedEntityId"]=143;break;case "wrong-holder":z.chefs[0]["heldEntityId"]=selected.Plate;break;
            }
            Reject(()=>Observe(z),"exact native transition/ownership guard: "+defect);
        }
        foreach(int recipe in new[]{296560,224216}){var z=Make();z.recipes[0]=CarnivalRecipes.GetRecipe(recipe);Check(Admit(z)&&z.platedBases.Values.Single().Recipe==recipe,"zero or Ketchup recipe is bound exactly without onions/two-sauce scope expansion");}
        var scarce=Make();scarce.recipes[1]=CarnivalRecipes.GetRecipe(158500);scarce.recipes[2]=CarnivalRecipes.GetRecipe(296560);scarce.mealPlates[0]=888;
        var earlier=scarce.Entity(143)!.DeepClone().AsObject();earlier["id"]=990;earlier["observedOrdinal"]=990;earlier["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood);
        scarce.state["entities"]!.AsArray().Add(earlier);scarce.Entity(40)!["attachedEntityId"]=990;scarce.Refresh();scarce.mealFoods[1]=990;
        int token=scarce.AvailablePlate(3);scarce.reserved.UnionWith(scarce.AvailablePlates().Select(Id).Where(id=>id!=token));
        Check(scarce.CanAllocatePlate(2)&&scarce.EarlierOrdinaryMealNeedsPlate(3,2,token),"current head already plated still preserves sole clean token for earlier ready pending recipe");
        Check(!scarce.TryPlatedHotdogBase(3,2,32,143,7,true,true),"later exact selected base does not repeat the direct-clean scarcity bypass");
        int spare=new[]{10,11,12,13}.First(id=>id!=token);scarce.reserved.Remove(spare);
        Check(scarce.AvailablePlates().Length==2&&scarce.TryPlatedHotdogBase(3,2,32,143,7,true,true),"two usable tokens preserve parallel admission of exact later candidate");
        Check(scarce.AvailablePlates().Length==1&&scarce.platedBases.Values.Single().Index==2,"earlier ready meal retains one real unclaimed plate after later candidate starts");
        return count;
    }
}
