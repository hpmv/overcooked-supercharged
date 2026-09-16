using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Offline geometry preflight only; does not create a client or emit inputs.
if(args.Length!=3)throw new ArgumentException("Usage: PLAN INITIAL_SNAPSHOT OUTPUT");
var plan=JsonNode.Parse(File.ReadAllText(args[0]))!.AsObject();
var snapshot=JsonNode.Parse(File.ReadAllText(args[1]))!.AsObject();
var state=snapshot["state"] as JsonObject??snapshot;
var model=KitchenModel.Build(state);
void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
int I(JsonNode? n)=>n?.GetValue<int>()??0;
Check(model.Scene=="s_Day_3_4","Wrong native kitchen");
var jobs=plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
Check(jobs.Length==6,"Expected six authored probe jobs");
var expected=new[]{"M01-supply-flour","M02-supply-egg","M03-chop-chocolate","M04-mix-and-park","M05-observe-offmix-thirteen-seconds","M06-fry-and-restore"};
Check(jobs.Select(j=>j["id"]!.ToString()).SequenceEqual(expected),"Unexpected probe sequence");
var positions=state["chefs"]!.AsArray().OfType<JsonObject>().ToDictionary(c=>I(c["playerId"]),c=>KitchenModel.Position(c["position"]));
Check(positions.Count==4,"Four native chefs required");
var checks=new JsonArray();int count=0;
for(int index=0;index<jobs.Length;index++)
{
    var job=jobs[index];int player=I(job["player"]);
    Check(player==(index<3?1:3),"Wrong chef for the authored probe");
    Check(job["dependencies"]!.AsArray().Select(n=>n!.ToString()).SequenceEqual(index==0?[]:new[]{expected[index-1]}),"Dependency chain changed");
    foreach(var action in job["actions"]!.AsArray().OfType<JsonObject>())
    {
        string type=action["type"]!.ToString();Check(I(action["timeoutFrames"])>0,"Unbounded action");count++;
        Check(new[]{"take","place","chop","mix","cook","throw","combine","navigate","wait"}.Contains(type),"Unexpected action type");
        if(type=="wait"){Check(I(action["durationFrames"])>=780,"Offmix wait must cover thirteen native seconds");continue;}
        if(action["target"] is JsonObject target)
        {
            var p=KitchenModel.Position(target);var path=Navigation.FindPath(model,positions[player],p);
            Check(path.Success,"Unreachable staging point: "+path.Error);positions[player]=p;continue;
        }
        var candidates=model.Candidates(action["station"]!.ToString());Check(candidates.Length==1,"Ambiguous or missing native selector: "+action["station"]);
        var station=candidates[0];
        if(type is not("throw" or "mix" or "cook"))
        {
            var path=Navigation.ToStation(model,positions[player],station);Check(path.Success,"Unreachable station: "+station.Key+" "+path.Error);positions[player]=path.Points[^1];
        }
        checks.Add(new JsonObject{["job"]=expected[index],["type"]=type,["station"]=station.Key,["nativeEntityId"]=station.EntityId});
    }
}
int Station(string role,double x,double z)=>model.Stations.Single(s=>s.Role==role&&s.Position.Distance(new(x,z))<.05).EntityId;
JsonObject E(int id)=>state["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==id);
var home=Station("mix-station",24,-10.8);int bowl=I(E(home)["attachedEntityId"]),counter=Station("counter",21.6,-10.8),basket=Station("basket",22.8,-21.6),board=Station("chop",25.2,-14.4);
Check(bowl!=0&&CarnivalRecipes.ClassifyEntity(E(bowl)).Food.IngredientIds.Length==0,"Native bowl must start empty at its original mixer");
Check(CarnivalRecipes.ClassifyEntity(E(basket)).Food.IngredientIds.Length==0&&I(E(counter)["attachedEntityId"])==0&&I(E(board)["attachedEntityId"])==0,"Fryer, parking counter and shared board must start empty");
var report=new JsonObject{["ok"]=true,["classification"]="Offline geometry and bounded action preflight; native offmix proof still required",["jobs"]=jobs.Length,["actions"]=count,["bowlId"]=bowl,["mixerId"]=home,["counterId"]=counter,["basketId"]=basket,["selectors"]=checks};
File.WriteAllText(args[2],report.ToJsonString(new(){WriteIndented=true}));Console.WriteLine(report.ToJsonString());
