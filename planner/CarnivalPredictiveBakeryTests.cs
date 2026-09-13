using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int PredictiveBakerySelfTest(JsonObject at12373,JsonObject at11069,JsonObject at11445,JsonObject at14401,JsonObject at15625,JsonObject preview)
    {
        int count=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("Predictive bakery regression: "+message);count++;}
        void Reject(Action action,string message){bool rejected=false;try{action();}catch(Exception e)when(e is InvalidOperationException or TimeoutException or ArgumentException){rejected=true;}Check(rejected,message);}
        CarnivalPlanner Make(JsonObject? fixture=null)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline predictive bakery fixture attempted native I/O."),null)
                {response=(fixture??at12373).DeepClone().AsObject(),options=new(PredictiveBakeryReturn:true,PantryChopping:true,DirectPreparedFlavorThrows:true)};
            if(p.response["state"] is null)p.response=new(){["state"]=p.response};
            p.recipes=(preview["response"]?["preview"]??preview["preview"]??preview)["recipes"]!.AsArray().OfType<JsonObject>().Select(r=>CarnivalRecipes.GetRecipe(I(r["recipeId"]))).ToArray();
            p.Refresh();p.runner=new(_=>throw new InvalidOperationException("Offline predictive bakery fixture attempted native I/O."),null);
            foreach(var bowl in p.Stations("bowl"))p.bowlHomes[bowl.EntityId]=p.AttachmentParent(bowl.EntityId);
            p.bowlAssignments[6]=24;p.bowlFlavors[6]=CarnivalRecipes.Chocolate.Id;
            p.fryerHomes[5]=15;p.fryerHomes[8]=20;p.basketAssignments[5]=20;
            if(!p.EmptyFood(8))p.basketAssignments[8]=19;
            p.CaptureSupplyTopology(false);p.bakeryPortalArrival=new(new(28.8,-11.2999992),91,I(p.Entity(91)?["observedOrdinal"]),7771);
            if(p.Entity(375) is {} choc&&CarnivalRecipes.MatchRecipe(choc,228996).ReadyToDeliver)p.mealPlates[19]=375;
            if(p.Entity(395) is {} head&&CarnivalRecipes.MatchRecipe(head,158500).ReadyToDeliver)p.mealPlates[17]=395;
            if(p.Held(0)==408)
            {
                p.assembling.Add(18);p.plating.Add(18);
                p.Start(0,"assemble-meal-19-"+p.recipes[18].Name,[p.A("place",47)],[408,47]);
            }
            return p;
        }
        void Arrive(CarnivalPlanner p)
        {
            // Explicit synthetic continuation from the separately observed
            // same-round portal endpoint; never a native execution claim.
            p.workers[1]=null;p.chefs[1]["position"]=new JsonObject{["x"]=28.8,["y"]=.5,["z"]=-11.2999992};
            p.state["gameplayFrame"]=p.Frame+140;p.ObservePredictiveBakeryVisit();
        }
        JsonObject Mix(params int[] ids)=>new(){["type"]="MixedCompositeAssembledNode",["state"]="Unmixed",["progress"]=.1,
            ["children"]=new JsonArray(ids.Select(i=>(JsonNode?)new JsonObject{["type"]="IngredientAssembledNode",["id"]=i,["children"]=new JsonArray()}).ToArray())};
        var disabled=Make();disabled.options=disabled.options with{PredictiveBakeryReturn=false};string original=disabled.response.ToJsonString();
        Check(!disabled.TryPredictiveBakeryReturn()&&disabled.predictiveBakeryVisit is null&&disabled.workers[1] is null,"default off preserves dispatch");
        Check(original==disabled.response.ToJsonString(),"default off preserves all native observations");
        disabled.BakeryAndWash("lower-left");Check(disabled.workers[1] is null,"actual default dispatcher retains its original one-free-plate idle decision");
        var p=Make();original=p.response.ToJsonString();
        var tokens=p.PredictivePlatePipeline();
        Check(tokens.Select(t=>t.Plate).SequenceEqual(new[]{375,395,408,412}),"actual four native plates are distinguished from the single free plate");
        Check(p.AvailablePlates().Select(Id).SequenceEqual(new[]{412})&&tokens.Single(t=>t.Plate==408).Index==18,"held clean assembly plate counts only with exact ordinary owner/index");
        var estimate=p.PredictiveBakeryEvidence();
        Check(estimate is not null,"captured GF12373 permits the bounded early return: "+p.lastPredictiveBakeryGate);
        Check(I(estimate!["index"])==24&&I(estimate["bowl"])==6&&I(estimate["home"])==18,"exact earliest missing ordinary-window Chocolate bowl/home selected");
        Check(N(estimate["kitBudgetSeconds"]) is >12.7 and <13&&N(estimate["productionBudgetSeconds"])<N(estimate["remainingNativeSeconds"])&&N(estimate["productionBudgetSeconds"])>34.79,"full supplier/native waits plus measured center/service route fits native remaining round");
        Check(p.TryPredictiveBakeryReturn()&&p.workers[1]?.Name=="predictive-one-kit-return-to-bakery","only ordinary portal Work starts");
        var visit=p.predictiveBakeryVisit!;
        Check(visit.Index==24&&visit.Deadline>p.Frame&&p.workers[1]!.OwnedResources.Count==0,"visit owns no duplicate resource lease");
        Check(original==p.response.ToJsonString(),"admission changes no native ingredient, timer, plate or location");
        Arrive(p);p.Entity(74)!["plateCount"]=2;p.Entity(75)!["plateCount"]=1;
        p.BakeryAndWash("upper-right");
        Check(p.workers[1]?.Name=="supply-Flour"&&p.predictiveBakeryVisit?.Issued==true,"dirty arrival before first ingredient cannot reverse the bound empty-bowl visit");
        Check(p.workers[1]!.OwnedResources.Contains(6)&&p.workers[1]!.OwnedResources.Contains(18)&&!p.reserved.Contains(412),"native raw supply retains original bowl/home locks and allocates no plate");
        var work=p.workers[1];var owned=p.reserved.ToHashSet();p.CancelPredictiveBakeryVisit("test-cancel");
        Check(p.predictiveBakeryVisit is null&&ReferenceEquals(work,p.workers[1])&&owned.SetEquals(p.reserved),"cancellation releases metadata without stealing active Work resources or clearing inputs");
        foreach(string mutation in new[]{"dirty-remote","dirty-sink","dirty-dryer","dirty-handoff","held-supplier","busy-supplier","suppressed","not-controlled","source-reserved","bowl-reserved","board-occupied","unknown-arrival","receiver-reused","bowl-detached","bowl-nonempty","nonfinite-progress","negative-progress","future-window","missing-head","only-two-plates","duplicate-plate-meals","duplicate-observation","no-empty-fryer","no-clean-plate","late-round","source-inactive","missing-ordinal","outside-service","wrong-bowl-assignment","duplicate-assignment","changed-fry-time"})
        {
            var q=Make();
            switch(mutation)
            {
                case "dirty-remote":q.Entity(74)!["plateCount"]=1;break;
                case "dirty-sink":q.Entity(75)!["plateCount"]=1;break;
                case "dirty-dryer":q.Entity(76)!["plateCount"]=1;break;
                case "dirty-handoff":q.Entity(46)!["attachedEntityId"]=412;break;
                case "held-supplier":q.chefs[1]["heldEntityId"]=412;break;
                case "busy-supplier":q.workers[1]=new Work("existing-wash",[],[],null);break;
                case "suppressed":q.chefs[1]["inputSuppressed"]=true;break;
                case "not-controlled":q.chefs[1]["controlsEnabled"]=false;break;
                case "source-reserved":q.reserved.Add(66);break;
                case "bowl-reserved":q.reserved.Add(6);break;
                case "board-occupied":q.Entity(24)!["attachedEntityId"]=412;break;
                case "unknown-arrival":q.bakeryPortalArrival=null;break;
                case "receiver-reused":q.Entity(91)!["observedOrdinal"]=999;break;
                case "bowl-detached":q.Entity(18)!["attachedEntityId"]=0;break;
                case "bowl-nonempty":q.Entity(6)!["composition"]=Mix(18448);break;
                case "nonfinite-progress":q.Entity(6)!["mixingProgress"]="NaN";break;
                case "negative-progress":q.Entity(6)!["mixingProgress"]=-1;break;
                case "future-window":q.options=q.options with{Lookahead=7};break;
                case "missing-head":q.mealPlates.Remove(17);break;
                case "only-two-plates":q.workers[0]=null;q.mealPlates.Remove(19);break;
                case "duplicate-plate-meals":q.mealPlates[21]=375;break;
                case "duplicate-observation":q.Entity(408)!["observedOrdinal"]=I(q.Entity(412)?["observedOrdinal"]);break;
                case "no-empty-fryer":q.reserved.Add(8);break;
                case "no-clean-plate":q.reserved.Add(412);break;
                case "late-round":q.state["timer"]=(at14401["state"]??at14401)["timer"]!.DeepClone();break;
                case "source-inactive":q.Entity(66)!["active"]=false;q.Refresh();break;
                case "missing-ordinal":q.Entity(66)!.Remove("observedOrdinal");break;
                case "outside-service":q.chefs[2]["position"]=new JsonObject{["x"]=12,["y"]=.5,["z"]=-12};break;
                case "wrong-bowl-assignment":q.bowlAssignments[6]=25;break;
                case "duplicate-assignment":q.bowlAssignments[3]=24;break;
                case "changed-fry-time":q.Entity(8)!["cookingTime"]=9;break;
            }
            Check(!q.TryPredictiveBakeryReturn()&&q.predictiveBakeryVisit is null,"admission rejects "+mutation);
        }
        var early=Make(at11069);Check(!early.TryPredictiveBakeryReturn(),"actual GF11069 has no normal-window missing index24");
        var residue=Make(at11445);Check(!residue.PredictivePlatePipeline().Any(t=>t.Plate==379)&&!residue.TryPredictiveBakeryReturn(),"actual disabled just-served plate379 never inflates capacity");
        var tooLate=Make();tooLate.state["timer"]=(at15625["state"]??at15625)["timer"]!.DeepClone();Check(!tooLate.TryPredictiveBakeryReturn(),"actual late return time cannot cover native mix/fry production");
        foreach(string mutation in new[]{"bowl-reused","home-detached","source-inactive","wrong-assignment","changed-recipe","changed-mix-time","nonfinite-progress","duplicate-ingredient","unissued-ingredient","lost-ingredient","unrelated-work","timeout"})
        {
            var q=Make();Check(q.TryPredictiveBakeryReturn(),"observer starts before "+mutation);
            switch(mutation)
            {
                case "bowl-reused":q.Entity(6)!["observedOrdinal"]=999;break;
                case "home-detached":q.Entity(18)!["attachedEntityId"]=0;break;
                case "source-inactive":q.Entity(66)!["active"]=false;break;
                case "wrong-assignment":q.bowlAssignments[6]=25;break;
                case "changed-recipe":q.recipes[24]=CarnivalRecipes.GetRecipe(130976);break;
                case "changed-mix-time":q.Entity(6)!["mixingTime"]=11;break;
                case "nonfinite-progress":q.Entity(6)!["mixingProgress"]="NaN";break;
                case "duplicate-ingredient":q.Entity(6)!["composition"]=Mix(18448,18448);break;
                case "unissued-ingredient":q.Entity(6)!["composition"]=Mix(18448);break;
                case "lost-ingredient":q.predictiveBakeryVisit!.LastIngredients=[18448];break;
                case "unrelated-work":q.workers[1]=new Work("board-wash-cannon",[],[],null);break;
                case "timeout":q.state["gameplayFrame"]=q.predictiveBakeryVisit!.Deadline+1;break;
            }
            Reject(q.ObservePredictiveBakeryVisit,"observer fails closed for "+mutation);
        }
        var complete=Make();Check(complete.TryPredictiveBakeryReturn(),"completion fixture starts");Arrive(complete);
        var reservedBefore=complete.reserved.ToHashSet();var accepted=new List<int>();
        foreach(int ingredient in new[]{18448,16620,22804})
        {
            // Explicit synthetic native acceptance continuation. The actual
            // raw/throw/chop Work observers have separate native proof suites.
            complete.predictiveBakeryVisit!.Issued=true;complete.predictiveBakeryVisit.ExpectedIngredient=ingredient;
            accepted.Add(ingredient);complete.Entity(6)!["composition"]=Mix(accepted.ToArray());complete.Entity(6)!["mixingProgress"]=1.9;
            complete.ObservePredictiveBakeryVisit();
        }
        Check(complete.predictiveBakeryVisit is null&&reservedBefore.SetEquals(complete.reserved)&&!complete.mealPlates.ContainsKey(24),"exact native full ingredient kit ends metadata without pretending Mixed/plated or freeing another owner");
        Check(!complete.ContinuePredictiveBakeryVisit(),"ordinary washing dispatch resumes after full-kit acceptance");
        foreach(string mutation in new[]{"lost-resource","extra-resource","second-owner","wrong-ingredient"})
        {
            var q=Make();Check(q.TryPredictiveBakeryReturn(),"supply owner fixture starts before "+mutation);Arrive(q);q.ContinuePredictiveBakeryVisit();
            Check(q.workers[1]?.Name=="supply-Flour","ordinary exact Flour Work exists before "+mutation);
            switch(mutation)
            {
                case "lost-resource":q.reserved.Remove(6);break;
                case "extra-resource":q.workers[1]!.OwnedResources.Add(44);break;
                case "second-owner":q.workers[3]=new Work("stolen-bowl",[],[6],null);break;
                case "wrong-ingredient":q.Entity(6)!["composition"]=Mix(16620);break;
            }
            Reject(q.ObservePredictiveBakeryVisit,"exact supply ownership/delta rejects "+mutation);
        }
        var pending=Make();Check(pending.TryPredictiveBakeryReturn(),"pending-address fixture starts");Arrive(pending);pending.counterSupplies[48]=(6,18448);
        Check(pending.ContinuePredictiveBakeryVisit()&&pending.workers[1] is null&&pending.counterSupplies[48]==(6,18448),"existing exact issued address is retained and never duplicated");
        var heldOwner=Make();Check(heldOwner.TryPredictiveBakeryReturn(),"leased-bowl fixture starts");Arrive(heldOwner);heldOwner.reserved.Add(6);
        Check(heldOwner.ContinuePredictiveBakeryVisit()&&heldOwner.workers[1] is null&&heldOwner.reserved.Contains(6),"visit waits on genuine vessel ownership rather than stealing it");
        Reject(()=>ValidatePredictiveBakeryOption(new(PredictiveBakeryReturn:true)),"unproven disabled prepared route is rejected as configuration");
        ValidatePredictiveBakeryOption(new());Check(true,"ordinary default configuration remains valid");
        return count;
    }
}
