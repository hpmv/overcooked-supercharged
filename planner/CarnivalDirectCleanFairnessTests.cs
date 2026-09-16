using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int DirectCleanFairnessSelfTest(JsonObject at14995,JsonObject preview)
    {
        int count=0;string path=Path.Combine(Path.GetTempPath(),"oc2tas-direct-clean-fairness-"+Guid.NewGuid().ToString("N")+".jsonl");
        using var diagnostics=new TraceWriter(path,"offline-admission-purity");
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("Direct clean fairness regression: "+message);count++;}
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline fairness fixture attempted native I/O."),diagnostics)
            {response=at14995.DeepClone().AsObject(),options=new(DirectCleanPassAssembly:true,CooperativeSauces:true,NearSauceStaging:true,ServiceSideHead:true)};
            p.recipes=(preview["response"]?["preview"]??preview["preview"]??preview)["recipes"]!.AsArray().OfType<JsonObject>().Select(r=>CarnivalRecipes.GetRecipe(I(r["recipeId"]))).ToArray();
            p.Refresh();p.runner=new(_=>throw new InvalidOperationException("Offline fairness route attempted native I/O."),null);
            // Exact native head/base plus explicit persistent ownership at14995:
            // P3 is rescuing pot2/home17/counter33; cooked basket8 is parked37.
            p.mealPlates[22]=377;p.mealPlates[25]=450;p.mealFoods[23]=419;
            p.workers[3]=new Work("rescue-cooked-pot-2",[],[],null);p.reserved.UnionWith([2,17,33]);
            p.fryerHomes[8]=20;p.basketAssignments[8]=24;
            var fryer=new FryerRescue(8,24,20,37,I(p.Entity(8)?["observedOrdinal"]),I(p.Entity(20)?["observedOrdinal"]),I(p.Entity(37)?["observedOrdinal"]),-1,p.Frame)
                {Phase=FryerPhase.Parked,Progress=N(p.Entity(8)?["cookingProgress"]),Food=JsonSerializer.Serialize(p.Food(8))};
            p.fryerRescues.Add(8,fryer);p.reserved.UnionWith(fryer.Resources);
            return p;
        }
        string Fingerprint(CarnivalPlanner p)=>new JsonObject{
            ["native"]=p.response.DeepClone(),["status"]=p.Status(),["reserved"]=JsonSerializer.SerializeToNode(p.reserved.Order()),
            ["mealPlates"]=JsonSerializer.SerializeToNode(p.mealPlates.OrderBy(e=>e.Key)),["mealFoods"]=JsonSerializer.SerializeToNode(p.mealFoods.OrderBy(e=>e.Key)),
            ["assembling"]=JsonSerializer.SerializeToNode(p.assembling.Order()),["plating"]=JsonSerializer.SerializeToNode(p.plating.Order()),
            ["bowlAssignments"]=JsonSerializer.SerializeToNode(p.bowlAssignments),["basketAssignments"]=JsonSerializer.SerializeToNode(p.basketAssignments),
            ["counterSupplies"]=JsonSerializer.SerializeToNode(p.counterSupplies),["sauceStagingCount"]=p.sauceStaging.Count,["directCleanCount"]=p.directCleanPasses.Count,
            ["model"]=JsonSerializer.SerializeToNode(p.model),["workers"]=new JsonArray(p.workers.Select(w=>w is null?null:(JsonNode)new JsonObject{
                ["reference"]=RuntimeHelpers.GetHashCode(w),["name"]=w.Name,["owned"]=JsonSerializer.SerializeToNode(w.OwnedResources.Order()),
                ["resources"]=JsonSerializer.SerializeToNode(w.Resources),["actions"]=new JsonArray(w.Actions.Select(a=>(JsonNode?)a.DeepClone()).ToArray()),
                ["activeReference"]=w.Active is null?null:RuntimeHelpers.GetHashCode(w.Active),["activeSpecification"]=w.Active?.Specification.DeepClone(),
                ["callbackReference"]=w.Complete is null?null:RuntimeHelpers.GetHashCode(w.Complete)}).ToArray())}.ToJsonString();
        bool Inspect(CarnivalPlanner p,int plate=484)
        {
            string before=Fingerprint(p);var refs=p.workers.ToArray();var model=p.model;var runner=p.runner;var sauce=p.sauceLease;
            long bytes=new FileInfo(path).Length;
            bool result=p.TryAssemble(0,exactIndex:23,exactPlate:plate,inspectTwoSauceOnly:true);
            Check(before==Fingerprint(p),"entire serialized relevant planner/native state remains unchanged after inspection");
            Check(p.workers.Zip(refs).All(pair=>ReferenceEquals(pair.First,pair.Second))&&ReferenceEquals(p.model,model)&&ReferenceEquals(p.runner,runner)&&ReferenceEquals(p.sauceLease,sauce),"existing Work/model/runner/cooperative references remain unchanged");
            Check(bytes==new FileInfo(path).Length,"inspection emits no diagnostic or action/ownership trace records");
            return result;
        }
        try
        {
            var actual=Make();
            Check(actual.delivered==22&&actual.Held(0)==0&&actual.Held(2)==377&&actual.Attached(44)==484&&actual.AvailablePlates().Select(Id).SequenceEqual(new[]{484}),"actual14995 has one available clean plate while current head is held by P2");
            Check(actual.Attached(40)==419&&CarnivalRecipes.MatchRecipe(actual.Entity(419),296560,false).ReadyToDeliver&&actual.EmptyAttachment(49),"earlier Both24 has the exact native ready base and open ordinary output49");
            Check(Inspect(actual),"busy helper preserves existing serial two-sauce fallback admission");
            string unchanged=Fingerprint(actual);long bytes=new FileInfo(path).Length;
            Check(!actual.TryDirectCleanPassAssembly(0)&&unchanged==Fingerprint(actual)&&bytes==new FileInfo(path).Length,"shortcut declines without consuming plate or logging when it would bypass ready earlier Both24");
            Check(actual.TryAssemble(0,exactIndex:23,exactPlate:484)&&actual.workers[0]?.Name=="assemble-meal-24-Hotdog_Ketchup_Mustard","ordinary original assembly still starts the earlier exact plate/base meal");
            Check(actual.workers[0]!.OwnedResources.IsSupersetOf([484,419,40,49,72,79])&&!actual.directCleanPasses.Any(),"ordinary source/plate/output locks remain outside the direct-clean extension");
            var idle=Make();idle.workers[3]=null;
            Check(Inspect(idle)&&idle.sauceLease is null&&idle.workers.All(w=>w is null),"cooperative inspection returns before creating its lease or either helper Work");
            Check(!idle.TryDirectCleanPassAssembly(0)&&idle.sauceLease is null,"eligible cooperative meal also retains ordinary pending order");
            var ordinary=Make();ordinary.options=ordinary.options with{DirectCleanPassAssembly=false};
            Check(!ordinary.TryDirectCleanPassAssembly(0)&&ordinary.TryAssemble(0,exactIndex:23,exactPlate:484),"disabled shortcut preserves original native assembly behavior");
            foreach(string mutation in new[]{"base-missing","base-unready","base-lease","source-lease","plate-lease","outputs-full","dispenser-lease","button-lease","owner-busy","head-no-plate"})
            {
                var q=Make();
                switch(mutation)
                {
                    case "base-missing":q.mealFoods.Remove(23);break;
                    case "base-unready":q.Entity(419)!["composition"]=q.Entity(484)!["composition"]?.DeepClone();break;
                    case "base-lease":q.reserved.Add(419);break;
                    case "source-lease":q.reserved.Add(40);break;
                    case "plate-lease":q.reserved.Add(484);break;
                    case "outputs-full":q.reserved.UnionWith([49,52]);break;
                    case "dispenser-lease":q.reserved.Add(72);break;
                    case "button-lease":q.reserved.Add(79);break;
                    case "owner-busy":q.workers[0]=new Work("existing-job",[],[],null);break;
                    case "head-no-plate":q.mealPlates.Remove(22);break;
                }
                Check(!Inspect(q),"ordinary exact admission rejects "+mutation);
                if(mutation is "base-missing" or "base-unready" or "base-lease" or "source-lease")
                    Check(q.TryDirectCleanPassAssembly(0)&&q.workers[0]?.Name=="assemble-meal-25-Donut_Chocolate","unready earlier meal does not blindly block supported later native donut: "+mutation);
            }
            var two=Make();
            var extra=two.Entity(484)!.DeepClone().AsObject();extra["id"]=990;extra["observedOrdinal"]=989;extra["position"]=two.Entity(32)!["position"]!.DeepClone();
            two.state["entities"]!.AsArray().Add(extra);two.Entity(32)!["attachedEntityId"]=990;two.Refresh();
            Check(two.AvailablePlates().Length==2&&Inspect(two),"explicit synthetic second clean plate retains exact earlier assembly eligibility");
            Check(two.TryDirectCleanPassAssembly(0)&&two.workers[0]?.Name=="assemble-meal-25-Donut_Chocolate"&&two.AvailablePlates().Any(e=>Id(e)==990),"two clean plates preserve the existing later fast path while leaving the other plate for earlier ordinary assembly");
            var noNear=Make();noNear.options=noNear.options with{NearSauceStaging=false};Check(Inspect(noNear),"existing original-source serial staging remains inspectable");
            var noLoop=Make();
            // Explicit synthetic base on the protected center workspace:
            // other serial staging counters are owned, so the normal selector
            // reaches its refusal diagnostic rather than an earlier null gate.
            noLoop.Entity(40)!["attachedEntityId"]=0;noLoop.Entity(42)!["attachedEntityId"]=419;
            foreach(var c in noLoop.Stations("counter").Where(s=>s.Regions.SequenceEqual(new[]{"center"})&&s.EntityId!=42))noLoop.reserved.Add(c.EntityId);
            Check(!Inspect(noLoop),"no legal serial staging counter rejects without transient diagnostic mutation");
            long refusedBefore=new FileInfo(path).Length;
            var refusal=noLoop.SelectNearSauceStaging(0,23,42,419,484,49,296560,[484,419,42,49,72,79]);
            Check(refusal is null&&new FileInfo(path).Length>refusedBefore,"fixture exercises the normal refusal-log branch that inspection suppresses");
            var invalid=Make();bool rejected=false;
            try{invalid.TryAssemble(0,exactIndex:24,exactPlate:484,inspectTwoSauceOnly:true);}catch(ArgumentException){rejected=true;}
            Check(rejected,"inspection cannot expand into unreviewed donut/native-wait paths");
            return count;
        }
        finally{diagnostics.Dispose();File.Delete(path);}
    }
}
