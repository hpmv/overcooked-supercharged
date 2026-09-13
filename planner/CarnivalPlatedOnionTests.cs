using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Native V16 GF5730 admission plus explicitly synthetic transitions and ownership mutations. No native I/O.</summary>
    public static int PlatedOnionSelfTest(JsonObject at5730)
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("Plated onion regression: "+message);count++;}
        void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(TimeoutException){rejected=true;}Check(rejected,message);}
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline plated-onion test attempted native I/O."),null)
            {response=at5730.DeepClone().AsObject(),options=new(PlatedOnionFinish:true,ServiceSideHead:true),
                recipes=Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560),96).ToArray()};
            p.Refresh();p.runner=new(_=>throw new InvalidOperationException("Offline plated-onion route attempted native I/O."),null);
            p.recipes[9]=CarnivalRecipes.GetRecipe(47642);p.mealFoods[9]=234;
            // Native status5640/5730 binds the unchanged pan4/home16/counter61.
            p.earlyOnion=new(9,4,16,61,3,-1,5069){Phase=EarlyOnionPhase.Heating,PhaseFrame=5177};
            p.reserved.UnionWith(p.earlyOnion.Resources);return p;
        }
        JsonObject Node(FoodObservation f)=>new(){["type"]=f.Kind switch{FoodNodeKind.Ingredient=>"IngredientAssembledNode",FoodNodeKind.Cooked=>"CookedCompositeAssembledNode",FoodNodeKind.Mixed=>"MixedCompositeAssembledNode",_=>"CompositeAssembledNode"},
            ["id"]=f.IngredientId,["state"]=f.Preparation.ToString(),["cookingStepId"]=f.CookingStepId,["children"]=new JsonArray(f.Children.Select(c=>(JsonNode?)Node(c)).ToArray())};
        void Tick(CarnivalPlanner p,int frames=1){p.state["gameplayFrame"]=p.Frame+frames;p.state["timer"]=N(p.state["timer"])-frames/60d;}
        void Observe(CarnivalPlanner p)=>p.AdvanceEarlyOnion(p.earlyOnion!,false);
        void Pickup(CarnivalPlanner p){p.Entity(41)!["attachedEntityId"]=0;p.chefs[0]["heldEntityId"]=243;Tick(p);Observe(p);}
        void BaseUnder(CarnivalPlanner p)
        {
            p.Entity(32)!["attachedEntityId"]=243;p.Entity(243)!["composition"]=Node(CarnivalRecipes.GetRecipe(296560).ExpectedFood);
            p.Entity(234)!["active"]=false;p.chefs[0]["heldEntityId"]=0;Tick(p);p.Refresh();Observe(p);
        }
        void RemoveSource(CarnivalPlanner p)
        {
            var prior=p.LastPlatedSourceRegistry(234)!.DeepClone().AsObject();prior["kind"]="remove";prior["sequence"]=99999L;
            p.state["entityRegistration"]!["events"]!.AsArray().Add(prior);Tick(p);Observe(p);
        }
        void Recover(CarnivalPlanner p){p.Entity(32)!["attachedEntityId"]=0;p.chefs[0]["heldEntityId"]=243;Tick(p);Observe(p);}
        void Dress(CarnivalPlanner p)
        {
            var plan=p.platedOnions[p.earlyOnion!];p.Entity(72)!["switchIndex"]=plan.Sauce==CarnivalRecipes.Ketchup.Id?1:0;
            p.Entity(243)!["composition"]=Node(CarnivalRecipes.GetRecipe(plan.BeforeOnionRecipe).ExpectedFood);Tick(p);Observe(p);
        }
        void Cook(CarnivalPlanner p)
        {
            p.Entity(4)!["cookingProgress"]=12.0167;
            p.Entity(4)!["composition"]=Node(CarnivalRecipes.GetRecipe(472326).ExpectedFood.Children.Single(f=>f.CookingStepId==CarnivalRecipes.PanCookingStepId));
            Tick(p);Observe(p);
        }
        void Consume(CarnivalPlanner p)
        {
            p.Entity(4)!["composition"]=Node(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PanCookingStepId,0,[]));
            p.Entity(4)!["cookingProgress"]=0;p.Entity(4)!["contents"]=new JsonArray();p.Entity(4)!["ingredientIds"]=new JsonArray();
            p.Entity(243)!["composition"]=Node(CarnivalRecipes.GetRecipe(p.platedOnions[p.earlyOnion!].Recipe).ExpectedFood);Tick(p);Observe(p);
        }
        string Fingerprint(CarnivalPlanner p)=>new JsonObject{["native"]=p.response.DeepClone(),["status"]=p.Status(),
            ["reserved"]=JsonSerializer.SerializeToNode(p.reserved.Order()),["mealFoods"]=JsonSerializer.SerializeToNode(p.mealFoods),
            ["mealPlates"]=JsonSerializer.SerializeToNode(p.mealPlates),["assembling"]=JsonSerializer.SerializeToNode(p.assembling),
            ["plating"]=JsonSerializer.SerializeToNode(p.plating),["model"]=JsonSerializer.SerializeToNode(p.model)}.ToJsonString();
        var disabled=Make();disabled.options=disabled.options with{PlatedOnionFinish=false};string unchanged=Fingerprint(disabled);
        Check(!disabled.TryPlatedOnionFinish(disabled.earlyOnion!,0)&&unchanged==Fingerprint(disabled)&&disabled.workers.All(w=>w is null),"default-off changes neither native state nor planner ownership");
        Check(disabled.TryAdmitEarlyOnionRescue(disabled.earlyOnion!,0)&&disabled.earlyOnion!.Phase==EarlyOnionPhase.DirectHarvest,"default-off retains existing proven unplated direct harvest at5730");
        var p=Make();var lease=p.earlyOnion!;string raw=p.response.ToJsonString();
        Check(p.Frame==5730&&p.delivered==9&&p.Attached(32)==234&&p.Attached(41)==243&&p.AvailablePlates().Select(Id).SequenceEqual(new[]{243}),"captured native exact plain base and sole free clean plate are available for actual FIFO10");
        Check(N(p.Entity(4)?["cookingProgress"]) is >9.23 and <9.24&&p.Attached(16)==4&&p.Attached(61)==0,"actual9.233s original pan lease remains on heat with empty parking counter");
        Check(p.TryAdmitEarlyOnionRescue(lease,0)&&lease.Phase==EarlyOnionPhase.PlatedFinish,"selected existing onion obligation admits new optional path before old unplated harvest");
        var plan=p.platedOnions[lease];var work=p.workers[0]!;
        Check(raw==p.response.ToJsonString()&&plan.Plate==243&&plan.Food==234&&plan.Source==32&&plan.SourceRegistration==3820&&plan.SourceUnityInstance==-388256,"admission pins actual observed identities and never changes native state");
        Check(work.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"switch-condiment","take","assemble","apply","navigate","cook","combine","place"}),"held plate is prepared and sauced before exact native cooking and onion transfer");
        Check(I(work.Actions.ElementAt(1)["station"])==243&&I(work.Actions.ElementAt(2)["station"])==32&&I(work.Actions.ElementAt(3)["station"])==72&&I(work.Actions.Last()["station"])==47,"existing native plate-under recovery and FIFO lower-right output are used");
        Check(work.OwnedResources.SetEquals([234,32,243,47,72,79])&&!work.OwnedResources.Overlaps(lease.Resources)&&lease.Resources.All(p.reserved.Contains),"ordinary Work locks are disjoint from original persistent pan/home/counter ownership");
        Check(!work.OwnedResources.Contains(41)&&p.plating.Contains(9)&&p.assembling.Contains(9)&&!p.mealPlates.ContainsKey(9),"plate origin occupancy protects pickup; no speculative completed-plate claim");
        Check(N(plan.Evidence["estimatedHeatSeconds"])<21-N(p.Entity(4)?["cookingProgress"])&&plan.Deadline-plan.Started<=900&&B(plan.Evidence["switchNeeded"]),"conservative full native cook wait and actual switch route fit existing21s guard and fixed cap");
        Check(!work.Actions.Any(a=>a["type"]?.ToString()=="take"&&I(a["station"])==4)&&!work.Actions.Any(a=>I(a["station"])==61),"whole pan and unused parking counter are never moved by this Work");
        Observe(p);Reject(()=>work.Complete!(),"callback cannot certify unfinished native food");
        var cookSpec=work.Actions.Single(a=>a["type"]?.ToString()=="cook").DeepClone().AsObject();cookSpec["player"]=0;
        var wait=p.runner.CreateAction(cookSpec);var input=p.runner.Tick(wait,p.response);
        Check(!wait.IsDone&&!B(input["pickup"])&&!B(input["use"])&&N(input["x"])==0&&N(input["y"])==0,"native cook barrier emits neutral while original onion is raw");
        Pickup(p);Check(plan.PickedUp&&!plan.BaseAcquired,"native same empty plate pickup precedes base assembly");
        BaseUnder(p);Check(plan.BaseAcquired&&!plan.SourceRemovalObserved&&p.Held(0)==0&&p.Attached(32)==243,"native plate-under interval accepts exact plated base and inactive original source before removal receipt");
        RemoveSource(p);Check(plan.SourceRemovalObserved&&!plan.Harvested,"matching original registration removal is observed separately before exact plate recovery");
        Recover(p);Dress(p);Check(plan.SauceObserved&&CarnivalRecipes.MatchRecipe(p.Entity(243),158500).ReadyToDeliver,"same recovered plate has native Mustard plain base before onions");
        // Synthetic independent successor uses the now-empty plate origin.
        var successor=new Work("fixture-reuse-clean-origin",[],[41],null);p.workers[3]=successor;p.reserved.Add(41);
        Cook(p);p.runner.Tick(wait,p.response);Check(plan.CookedObserved&&wait.IsDone&&!plan.Harvested,"strict native Cooked barrier precedes consumption");
        Consume(p);Check(plan.Harvested&&p.Attached(16)==4&&p.EmptyFood(4)&&p.Attached(61)==0,"same held plate acquires exact native cooked onions while original empty pan remains home");
        Tick(p,plan.Started+721-p.Frame);Observe(p);
        Check(p.Frame==plan.Started+721&&p.Frame<=plan.Deadline,"post-consumption output travel honors the fixed admitted deadline beyond720 without extending it");
        p.chefs[0]["heldEntityId"]=0;p.Entity(47)!["attachedEntityId"]=243;Tick(p);Observe(p);
        work.Actions.Clear();var last=p.A("place",47);last["player"]=0;work.Active=p.runner.CreateAction(last);work.Active.IsDone=true;p.CompleteWork();
        Check(p.earlyOnion is null&&p.platedOnions.Count==0&&p.workers[0] is null&&p.mealPlates.GetValueOrDefault(9)==243&&!p.mealFoods.ContainsKey(9),"only completed exact output updates mealFoods to mealPlates and retires selected persistent lease");
        Check(!p.assembling.Contains(9)&&!p.plating.Contains(9)&&p.reserved.SetEquals([41])&&ReferenceEquals(p.workers[3],successor),"exact callback releases only own resources while successor origin owner survives");
        foreach(string defect in new[]{"missing-base","wrong-base","reserved-source","reserved-plate","washer-origin-lease","two-sauces","out-of-window","busy-owner","suppressed-owner","park-occupied","home-changed","missing-ordinal","inactive-counter","missing-registry","registration-reused","slow-chef","late-pan","missing-progress","nan-progress","negative-progress","no-output","sauce-lease","unknown-switch"})
        {
            var q=Make();var l=q.earlyOnion!;
            switch(defect)
            {
                case "missing-base":q.mealFoods.Clear();break;case "wrong-base":q.Entity(234)!["composition"]=Node(CarnivalRecipes.GetRecipe(472326).ExpectedFood);break;
                case "reserved-source":q.reserved.Add(32);break;case "reserved-plate":q.reserved.Add(243);break;case "washer-origin-lease":q.reserved.Add(41);break;
                case "two-sauces":q.recipes[9]=CarnivalRecipes.GetRecipe(125780);break;case "out-of-window":q.delivered=0;break;
                case "busy-owner":q.workers[0]=new("other",[],[],null);break;case "suppressed-owner":q.chefs[0]["useSuppressed"]=true;break;
                case "park-occupied":q.Entity(61)!["attachedEntityId"]=243;break;case "home-changed":q.Entity(16)!["attachedEntityId"]=0;break;
                case "missing-ordinal":q.Entity(32)!.Remove("observedOrdinal");break;case "inactive-counter":q.Entity(32)!["active"]=false;break;
                case "missing-registry":q.state["entityRegistration"]!["installed"]=false;break;case "registration-reused":q.LastPlatedSourceRegistry(234)!["kind"]="remove";break;
                case "slow-chef":q.chefs[0]["runSpeed"]=.01;break;case "late-pan":q.Entity(4)!["cookingProgress"]=18;break;
                case "missing-progress":q.Entity(4)!.Remove("cookingProgress");break;case "nan-progress":q.Entity(4)!["cookingProgress"]="NaN";break;case "negative-progress":q.Entity(4)!["cookingProgress"]=-1;break;
                case "no-output":q.reserved.UnionWith([47,49,52]);break;case "sauce-lease":q.reserved.Add(72);break;case "unknown-switch":q.Entity(72)!.Remove("switchIndex");break;
            }
            var before=q.reserved.ToHashSet();var owner=q.workers[0];Check(!q.TryPlatedOnionFinish(l,0)&&q.reserved.SetEquals(before)&&ReferenceEquals(q.workers[0],owner)&&l.Phase==EarlyOnionPhase.Heating,"admission refuses without changing existing ownership: "+defect);
        }
        foreach(string defect in new[]{"plate-reused","pan-reused","home-reused","park-reused","source-reused","output-reused","food-reused","registry-reused","owned-resource-lost","work-replaced","recipe-reassigned","native-reattach","source-vanished","uncooked-consume","wrong-sauce","deadline","phase-reset-deadline","nan-progress","negative-progress","plate-other-chef","foreign-output","foreign-park"})
        {
            var q=Make();var l=q.earlyOnion!;Check(q.TryPlatedOnionFinish(l,0),"prepare observer mutation: "+defect);
            switch(defect)
            {
                case "plate-reused":q.Entity(243)!["observedOrdinal"]=999;break;case "pan-reused":q.Entity(4)!["observedOrdinal"]=999;break;
                case "home-reused":q.Entity(16)!["observedOrdinal"]=999;break;case "park-reused":q.Entity(61)!["observedOrdinal"]=999;break;
                case "source-reused":q.Entity(32)!["observedOrdinal"]=999;break;case "output-reused":q.Entity(47)!["observedOrdinal"]=999;break;
                case "food-reused":q.Entity(234)!["observedOrdinal"]=999;break;case "registry-reused":q.LastPlatedSourceRegistry(234)!["entity"]!["observedRegistrationSequence"]=999L;break;
                case "owned-resource-lost":q.reserved.Remove(243);break;case "work-replaced":q.workers[0]=new("other",[],[],null);break;
                case "recipe-reassigned":q.recipes[9]=CarnivalRecipes.GetRecipe(257844);break;case "native-reattach":q.Entity(16)!["attachedEntityId"]=0;q.Entity(61)!["attachedEntityId"]=4;break;
                case "source-vanished":q.Entity(32)!["attachedEntityId"]=0;break;
                case "uncooked-consume":Pickup(q);BaseUnder(q);RemoveSource(q);Recover(q);Dress(q);q.Entity(4)!["composition"]=Node(new(FoodNodeKind.Cooked,FoodPreparation.Raw,0,"",CarnivalRecipes.PanCookingStepId,0,[]));q.Entity(4)!["cookingProgress"]=0;q.Entity(243)!["composition"]=Node(CarnivalRecipes.GetRecipe(47642).ExpectedFood);break;
                case "wrong-sauce":Pickup(q);BaseUnder(q);RemoveSource(q);Recover(q);q.Entity(243)!["composition"]=Node(CarnivalRecipes.GetRecipe(224216).ExpectedFood);break;
                case "deadline":q.Entity(4)!["cookingProgress"]=21;break;case "phase-reset-deadline":q.state["gameplayFrame"]=q.platedOnions[l].Deadline+1;l.PhaseFrame=q.Frame;break;
                case "nan-progress":q.Entity(4)!["cookingProgress"]="NaN";break;case "negative-progress":q.Entity(4)!["cookingProgress"]=-1;break;
                case "plate-other-chef":q.chefs[3]["heldEntityId"]=243;break;case "foreign-output":q.Entity(47)!["attachedEntityId"]=234;break;case "foreign-park":q.Entity(61)!["attachedEntityId"]=234;break;
            }
            Reject(()=>Observe(q),"native/ownership observer rejects: "+defect);
        }
        var lag=Make();Check(lag.TryPlatedOnionFinish(lag.earlyOnion!,0),"prepare bounded receipt lag");Pickup(lag);BaseUnder(lag);Tick(lag,4);Reject(()=>Observe(lag),"inactive source cannot bypass missing removal receipt indefinitely");
        foreach(int recipe in new[]{472326,257844})
        {
            var q=Make();q.recipes[9]=CarnivalRecipes.GetRecipe(recipe);Check(q.TryPlatedOnionFinish(q.earlyOnion!,0),"native optional zero/Ketchup path admits exact assigned variant");
            var selected=q.platedOnions[q.earlyOnion!];Check(selected.Sauce==(recipe==472326?0:CarnivalRecipes.Ketchup.Id)&&selected.BeforeOnionRecipe==(recipe==472326?296560:224216),"intermediate recipe is exact zero/one condiment plain base");
        }
        // Exact earlier-meal checks remain pure across all existing ordinary
        // serial recipe families, and preserve the sole-token FIFO allocation.
        foreach(int earlier in new[]{296560,158500,224216,125780,472326,130976,228996})
        {
            var q=Make();q.recipes[9]=CarnivalRecipes.GetRecipe(earlier);q.recipes[10]=CarnivalRecipes.GetRecipe(47642);
            q.mealFoods.Remove(9);q.mealFoods[10]=234;q.earlyOnion=new(10,4,16,61,3,-1,5069){Phase=EarlyOnionPhase.Heating,PhaseFrame=5177};
            if(IsDonut(q.recipes[9])){q.Entity(5)!["composition"]=Node(q.recipes[9].ExpectedFood);q.Entity(5)!["cookingProgress"]=11.6;}
            else
            {
                var food=q.Entity(234)!.DeepClone().AsObject();food["id"]=990;food["observedOrdinal"]=990;food["composition"]=Node(CarnivalRecipes.GetRecipe(earlier==472326?472326:296560).ExpectedFood);
                q.state["entities"]!.AsArray().Add(food);q.Entity(38)!["attachedEntityId"]=990;q.Refresh();q.mealFoods[9]=990;
            }
            string before=Fingerprint(q);var refs=q.workers.ToArray();Check(q.TryAssemble(0,exactIndex:9,exactPlate:243,inspectOnly:true),"earlier ordinary exact plate admission: "+earlier);
            Check(before==Fingerprint(q)&&q.workers.Zip(refs).All(x=>ReferenceEquals(x.First,x.Second)),"general ordinary admission is pure: "+earlier);
            Check(q.EarlierOrdinaryMealNeedsPlate(0,10,243),"sole plate fairness includes earlier recipe: "+earlier);
        }
        CarnivalPlanner Scarce()
        {
            var q=Make();q.recipes[9]=CarnivalRecipes.GetRecipe(296560);q.recipes[10]=CarnivalRecipes.GetRecipe(158500);q.recipes[11]=CarnivalRecipes.GetRecipe(47642);
            q.mealPlates[9]=777;q.mealFoods.Remove(9);q.mealFoods[11]=234;
            q.earlyOnion=new(11,4,16,61,3,-1,5069){Phase=EarlyOnionPhase.Heating,PhaseFrame=5177};
            var food=q.Entity(234)!.DeepClone().AsObject();food["id"]=990;food["observedOrdinal"]=990;
            q.state["entities"]!.AsArray().Add(food);q.Entity(38)!["attachedEntityId"]=990;q.Refresh();q.mealFoods[10]=990;
            return q;
        }
        var scarce=Scarce();Check(scarce.CanAllocatePlate(11)&&scarce.TryAssemble(0,exactIndex:10,exactPlate:243,inspectOnly:true),"synthetic current head already plated leaves earlier ready pending meal ordinarily eligible for sole token");
        string scarceBefore=Fingerprint(scarce);Check(!scarce.TryPlatedOnionFinish(scarce.earlyOnion!,0)&&scarceBefore==Fingerprint(scarce),"real new admission does not repeat later-meal sole-plate bypass");
        var parallel=Scarce();var extra=parallel.Entity(243)!.DeepClone().AsObject();extra["id"]=994;extra["observedOrdinal"]=994;
        parallel.state["entities"]!.AsArray().Add(extra);parallel.Entity(43)!["attachedEntityId"]=994;parallel.Refresh();
        Check(parallel.AvailablePlates().Length==2&&!parallel.EarlierOrdinaryMealNeedsPlate(0,11,243),"two distinct usable clean tokens preserve parallel allocation");
        Check(parallel.TryPlatedOnionFinish(parallel.earlyOnion!,0)&&parallel.platedOnions.Values.Single().Plate==243&&parallel.Free(994),"real new admission may use one plate while leaving the second for the earlier meal");
        var unavailable=Scarce();unavailable.reserved.Add(990);Check(unavailable.TryPlatedOnionFinish(unavailable.earlyOnion!,0),"unavailable earlier native source does not blanket-block a safe selected onion finish");
        return count;
    }
}
