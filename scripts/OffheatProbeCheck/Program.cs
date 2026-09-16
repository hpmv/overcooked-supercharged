using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Offline only: this executable has no game connection or input-emission path.
const string PanKey="pan:dlc08_utensil_frying_pan@18.00,-21.60";
const string HomeKey="cook-station:workstation_cooker_01@18.00,-21.60";
const string ParkKey="counter:dlc08_countertop_01_standard_circus@19.20,-21.60";
const string OutputKey="counter:dlc08_countertop_01_standard_circus@21.60,-21.60";
const string DwellJob="O04-observe-offheat-13-seconds";
void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
int I(JsonNode? n)=>n?.GetValue<int>()??0;
double N(JsonNode? n)=>n is null?0:double.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
JsonObject S(JsonObject x)=>x["state"] as JsonObject??x;
JsonObject E(JsonObject s,int id)=>s["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==id);
string Hash(JsonNode? n)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(n?.ToJsonString()??"null")));
bool Onion(JsonObject e){var f=CarnivalRecipes.ClassifyEntity(e);return f.EvidenceGaps.Length==0&&f.Food.Kind==FoodNodeKind.Cooked&&f.Food.CookingStepId==CarnivalRecipes.PanCookingStepId&&f.Food.Preparation==FoodPreparation.Cooked&&f.Food.IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Onion.Id});}
JsonObject report;
try
{
    Require(args.Length>=3,"Usage: preflight PLAN SNAPSHOT [OUT] | analyze TRACE PLAN [OUT]");
    if(args[0]=="preflight")
    {
        var plan=Read(args[1]);var snapshot=Read(args[2]);var state=S(snapshot);var model=KitchenModel.Build(snapshot);
        Require(model.Scene=="s_Day_3_4","Wrong scene.");
        var jobs=plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
        var lookup=jobs.ToDictionary(j=>j["id"]!.ToString());var visited=new HashSet<string>();var active=new HashSet<string>();
        void Visit(string id){if(visited.Contains(id))return;Require(active.Add(id),"Dependency cycle.");foreach(var d in lookup[id]["dependencies"]!.AsArray())Visit(d!.ToString());active.Remove(id);visited.Add(id);}
        foreach(var id in lookup.Keys)Visit(id);
        var positions=state["chefs"]!.AsArray().OfType<JsonObject>().ToDictionary(c=>I(c["playerId"]),c=>KitchenModel.Position(c["position"]));
        Require(positions.Count==4,"Four chefs required.");
        var paths=new JsonArray();int count=0;
        foreach(var job in jobs)
        {
            int player=I(job["player"]);Require(player is >=0 and <=3,"Invalid player.");
            Require(job["resources"]!.AsArray().Select(x=>x!.ToString()).Distinct().Count()==job["resources"]!.AsArray().Count,"Duplicate job resource.");
            foreach(var a in job["actions"]!.AsArray().OfType<JsonObject>())
            {
                string type=a["type"]!.ToString();Require(new[]{"take","place","chop","cook","combine","wait","navigate"}.Contains(type),"Unexpected probe action.");
                Require(I(a["timeoutFrames"])>0,"Bounded action timeout required.");count++;
                if(a["station"] is {} selector)
                {
                    var candidates=model.Candidates(selector.ToString());Require(candidates.Length==1,"Station selector must uniquely resolve: "+selector);
                    var station=candidates.Single();
                    if(type=="cook")continue;
                    var path=Navigation.ToStation(model,positions[player],station);
                    Require(path.Success,"Unreachable recorded-scene approach: "+selector+": "+path.Error);
                    positions[player]=path.Points.Last();
                    paths.Add(new JsonObject{["job"]=job["id"]!.DeepClone(),["player"]=player,["station"]=station.Key,["nativeIdAtFixture"]=station.EntityId,["length"]=path.Length});
                }
                else if(type=="navigate")
                {
                    var path=Navigation.FindPath(model,positions[player],KitchenModel.Position(a["target"]));Require(path.Success,"Retreat target unreachable.");positions[player]=path.Points.Last();
                }
            }
        }
        var pan=model.Resolve(PanKey);var home=model.Resolve(HomeKey);var park=model.Resolve(ParkKey);var output=model.Resolve(OutputKey);
        Require(I(E(state,home.EntityId)["attachedEntityId"])==pan.EntityId,"Pan is not on its measured stove.");
        Require(I(E(state,park.EntityId)["attachedEntityId"])==0&&I(E(state,output.EntityId)["attachedEntityId"])==0,"Probe staging or output counter occupied.");
        Require(CarnivalRecipes.ClassifyEntity(E(state,pan.EntityId)).Food.IngredientIds.Length==0,"Fresh empty pan required.");
        Require(lookup[DwellJob]["actions"]!.AsArray().OfType<JsonObject>().Any(a=>a["type"]?.ToString()=="wait"&&I(a["durationFrames"])>=780),"Missing thirteen-second native wait.");
        report=new JsonObject{["ok"]=true,["classification"]="Offline structure and initial static geometry only; native interaction success untested.",["jobs"]=jobs.Length,["actions"]=count,["planSha256"]=Hash(plan),["snapshotSha256"]=Hash(snapshot),["panId"]=pan.EntityId,["stoveId"]=home.EntityId,["parkCounterId"]=park.EntityId,["outputCounterId"]=output.EntityId,["paths"]=paths};
    }
    else
    {
        Require(args[0]=="analyze","Unknown mode.");var plan=Read(args[2]);
        using var file=File.OpenRead(args[1]);using Stream input=args[1].EndsWith(".gz",StringComparison.OrdinalIgnoreCase)?new GZipStream(file,CompressionMode.Decompress):file;
        using var reader=new StreamReader(input);
        JsonObject? previous=null;int pan=0,home=0,park=0,output=0,ordinal=-1,samples=0,firstFrame=-1,lastFrame=-1;
        bool dwelling=false,completed=false,heldCooked=false;double firstTime=0,lastTime=0,firstTimer=0,lastTimer=0,progress=0,onHeatMin=double.PositiveInfinity,onHeatMax=double.NegativeInfinity;
        string? composition=null;var done=new HashSet<string>();
        void Sample(JsonObject s)
        {
            var e=E(s,pan);Require(I(e["observedOrdinal"])==ordinal,"Pan observation identity changed.");
            Require(I(E(s,home)["attachedEntityId"])!=pan&&I(E(s,park)["attachedEntityId"])==pan,"Dwell pan left its non-heating counter or returned to stove.");
            Require(Onion(e),"Dwell no longer has fully cooked native pan-fried onion.");
            double p=N(e["cookingProgress"]);string hash=Hash(e["composition"]);
            if(firstFrame<0){firstFrame=I(s["gameplayFrame"]);firstTime=N(s["clientTime"]);firstTimer=N(s["timer"]);progress=p;composition=hash;}
            Require(p==progress&&hash==composition,"Native cooking progress/composition changed off heat.");
            lastFrame=I(s["gameplayFrame"]);lastTime=N(s["clientTime"]);lastTimer=N(s["timer"]);samples++;
        }
        string? line;
        while((line=reader.ReadLine()) is not null)
        {
            var row=JsonNode.Parse(line)!.AsObject();
            if(row["kind"]?.ToString()=="event")
            {
                string? name=row["name"]?.ToString(),id=row["value"]?["id"]?.ToString();
                Require(name is not("planFailure" or "actionFailure"),"Recorded attempt reports failure.");
                if(name=="jobStart"&&id==DwellJob){Require(previous is not null,"Dwell has no preceding native state.");dwelling=true;Sample(previous!);}
                if(name=="jobComplete"&&id is not null){done.Add(id);if(id==DwellJob){Sample(previous!);dwelling=false;completed=true;}}
                continue;
            }
            if(row["kind"]?.ToString()!="call")continue;
            var response=row["response"]!.AsObject();Require(response["ok"]?.GetValue<bool>()==true,"Protocol call failed.");
            if(response["state"] is not JsonObject s)continue;previous=s;
            if(pan==0)
            {
                var m=KitchenModel.Build(s);pan=m.Resolve(PanKey).EntityId;home=m.Resolve(HomeKey).EntityId;park=m.Resolve(ParkKey).EntityId;output=m.Resolve(OutputKey).EntityId;ordinal=I(E(s,pan)["observedOrdinal"]);
            }
            var vessel=E(s,pan);
            if(I(E(s,home)["attachedEntityId"])==pan&&CarnivalRecipes.ClassifyEntity(vessel).Food.IngredientIds.Contains(CarnivalRecipes.Onion.Id))
            {onHeatMin=Math.Min(onHeatMin,N(vessel["cookingProgress"]));onHeatMax=Math.Max(onHeatMax,N(vessel["cookingProgress"]));}
            if(s["chefs"]!.AsArray().OfType<JsonObject>().Any(c=>I(c["heldEntityId"])==pan)&&Onion(vessel)&&I(E(s,home)["attachedEntityId"])!=pan)heldCooked=true;
            if(dwelling)Sample(s);
        }
        Require(completed&&samples>=780&&lastFrame-firstFrame>=780,"Complete thirteen-second dwell was not captured.");
        Require(lastTime-firstTime>=12&&firstTimer-lastTimer>=12,"Native clock/timer did not advance by at least twelve seconds.");
        Require(heldCooked&&onHeatMax-onHeatMin>=10,"Native heating and whole-pan pickup evidence missing.");
        Require(plan["jobs"]!.AsArray().OfType<JsonObject>().All(j=>done.Contains(j["id"]!.ToString())),"Not all authored probe jobs completed.");
        var final=previous!;int food=I(E(final,output)["attachedEntityId"]);var match=CarnivalRecipes.MatchRecipe(E(final,food),472326,false);
        Require(match.ReadyToDeliver,"Final unplated onion hotdog lacks complete native recipe evidence.");
        Require(I(E(final,home)["attachedEntityId"])==pan&&CarnivalRecipes.ClassifyEntity(E(final,pan)).Food.IngredientIds.Length==0,"Empty original pan was not returned to its stove.");
        report=new JsonObject{["ok"]=true,["classification"]="Observed native onion off-heat probe; no score or replay qualification.",["planSha256"]=Hash(plan),["panId"]=pan,["observedOrdinal"]=ordinal,["dwellSamples"]=samples,["firstFrame"]=firstFrame,["lastFrame"]=lastFrame,["nativeElapsedSeconds"]=lastTime-firstTime,["timerElapsedSeconds"]=firstTimer-lastTimer,["unchangedCookingProgress"]=progress,["unchangedCompositionSha256"]=composition,["onHeatProgressGain"]=onHeatMax-onHeatMin,["wholeCookedPanPickupObserved"]=heldCooked,["finalFoodId"]=food,["finalRecipeId"]=472326,["finalNativeRecipeReady"]=match.ReadyToDeliver,["emptyPanReturned"]=true,["finalGameplayFrame"]=final["gameplayFrame"]!.DeepClone()};
    }
}
catch(Exception error){report=new JsonObject{["ok"]=false,["error"]=error.Message};Environment.ExitCode=1;}
string result=report.ToJsonString(new JsonSerializerOptions{WriteIndented=true});if(args.Length>3)File.WriteAllText(args[3],result);Console.WriteLine(result);
