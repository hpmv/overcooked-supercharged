using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Offline file reader only. Never creates a game client or emits inputs.
int I(JsonNode? n)=>n?.GetValue<int>()??0;
double N(JsonNode? n)=>n is null?0:double.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
JsonObject S(JsonObject r)=>r["state"] as JsonObject??r;
JsonObject? E(JsonObject s,int id)=>s["entities"]!.AsArray().OfType<JsonObject>().SingleOrDefault(e=>I(e["id"])==id);
bool Pure(JsonObject e,int ingredient,bool chopped){var food=CarnivalRecipes.ClassifyEntity(e);return food.EvidenceGaps.Length==0&&food.Food.IngredientIds.SequenceEqual(new[]{ingredient})&&(!chopped||food.Food.Preparation==FoodPreparation.Chopped);}
var expected=new[]{new Evidence("L01-pantry-chop-bun",2,262914,15.6,-14.4,15.6,-14.4),new Evidence("L02-pantry-chop-onion",2,461162,15.6,-12,15.6,-12),new Evidence("R01-pantry-chop-chocolate",1,22804,25.2,-14.4,25.2,-13.2),new Evidence("R02-pantry-chop-raspberry",1,129618,25.2,-14.4,25.2,-14.4)};
JsonObject report;
try
{
    Check(args.Length>=3,"Usage: preflight PLAN SNAPSHOT [OUT] | analyze TRACE PLAN [OUT]");
    var plan=Read(args[0]=="preflight"?args[1]:args[2]);var jobs=plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
    Check(jobs.Length==4&&jobs.Select(j=>j["id"]!.ToString()).ToHashSet().SetEquals(expected.Select(e=>e.Job)),"Authored four-job probe mismatch.");
    void Resolve(JsonObject state)
    {
        var model=KitchenModel.Build(state);Check(model.Scene=="s_Day_3_4","Wrong kitchen scene.");
        foreach(var e in expected)
        {
            e.Board=model.Stations.Single(s=>s.Role=="chop"&&s.Position.Distance(new(e.X,e.Z))<.05).EntityId;
            e.Output=model.Stations.Single(s=>s.Role==(e.Job.Contains("chocolate")?"counter":"chop")&&s.Position.Distance(new(e.OutX,e.OutZ))<.05).EntityId;
        }
    }
    if(args[0]=="preflight")
    {
        var state=S(Read(args[2]));Resolve(state);var model=KitchenModel.Build(state);
        var positions=state["chefs"]!.AsArray().OfType<JsonObject>().ToDictionary(c=>I(c["playerId"]),c=>KitchenModel.Position(c["position"]));
        Check(positions.Count==4,"Four native chefs required.");
        var lookup=jobs.ToDictionary(j=>j["id"]!.ToString());var visited=new HashSet<string>();var active=new HashSet<string>();
        void Visit(string id){if(visited.Contains(id))return;Check(active.Add(id),"Dependency cycle.");foreach(var d in lookup[id]["dependencies"]!.AsArray())Visit(d!.ToString());active.Remove(id);visited.Add(id);}
        foreach(var id in lookup.Keys)Visit(id);
        int actions=0;var paths=new JsonArray();
        foreach(var j in jobs)
        {
            int player=I(j["player"]);var definition=expected.Single(e=>e.Job==j["id"]!.ToString());Check(player==definition.Player,"Wrong assigned pantry chef.");
            foreach(var a in j["actions"]!.AsArray().OfType<JsonObject>())
            {
                Check(new[]{"take","place","chop"}.Contains(a["type"]!.ToString())&&I(a["timeoutFrames"])>0,"Unexpected or unbounded action.");
                var station=model.Candidates(a["station"]!.ToString());Check(station.Length==1,"Station selector must uniquely resolve.");
                var path=Navigation.ToStation(model,positions[player],station[0]);Check(path.Success,"Unreachable initial approach: "+station[0].Key);
                positions[player]=path.Points.Last();actions++;paths.Add(new JsonObject{["job"]=definition.Job,["station"]=station[0].Key,["nativeIdAtFixture"]=station[0].EntityId,["pathLength"]=path.Length});
            }
        }
        foreach(int id in expected.SelectMany(e=>new[]{e.Board,e.Output}).Distinct())Check(I(E(state,id)?["attachedEntityId"])==0,"Probe board/handoff is not initially empty.");
        report=new JsonObject{["ok"]=true,["classification"]="Offline initial geometry and dependency check; not native chopping proof.",["jobs"]=jobs.Length,["actions"]=actions,["paths"]=paths};
    }
    else
    {
        Check(args[0]=="analyze","Unknown checker mode.");using var file=File.OpenRead(args[1]);using Stream stream=args[1].EndsWith(".gz")?new GZipStream(file,CompressionMode.Decompress):file;using var reader=new StreamReader(stream);
        JsonObject? last=null;bool resolved=false;int firstFrame=-1,lastFrame=-1;double firstTimer=0;string? line;
        while((line=reader.ReadLine()) is not null)
        {
            var row=JsonNode.Parse(line)!.AsObject();
            if(row["kind"]?.ToString()=="event")
            {
                string? name=row["name"]?.ToString(),job=row["value"]?["id"]?.ToString();Check(name is not("planFailure" or "actionFailure"),"Probe contains an action/plan failure.");
                var e=expected.SingleOrDefault(e=>e.Job==job);if(e is not null){if(name=="jobStart")e.Started=true;if(name=="jobComplete")e.Done=true;}continue;
            }
            if(row["kind"]?.ToString()!="call")continue;var response=row["response"]!.AsObject();Check(response["ok"]?.GetValue<bool>()==true,"Recorded game call failed.");
            if(response["state"] is not JsonObject s)continue;last=s;
            if(!resolved){Resolve(s);resolved=true;firstFrame=I(s["gameplayFrame"]);firstTimer=N(s["timer"]);}lastFrame=I(s["gameplayFrame"]);
            foreach(var e in expected.Where(e=>e.Started&&!e.Done))
            {
                int attached=I(E(s,e.Board)?["attachedEntityId"]);var item=E(s,attached);if(item is null||!Pure(item,e.Ingredient,false))continue;
                var chef=s["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>I(c["playerId"])==e.Player);
                if(I(chef["interactingEntityId"])==e.Board)e.ClientInteractionFrames++;
                if(I(chef["serverInteractionId"])==e.Board)e.ServerInteractionFrames++;
                bool workable=(item["components"] as JsonArray)?.Any(c=>c?.ToString() is "WorkableItem" or "ServerWorkableItem")==true;
                if(workable)
                {
                    if(e.Raw==0){e.Raw=attached;e.RawOrdinal=I(item["observedOrdinal"]);}
                    Check(e.Raw==attached&&e.RawOrdinal==I(item["observedOrdinal"]),"Raw item changed before observed preparation: "+e.Job);
                    double p=N(item["workProgress"]);e.MinProgress=Math.Min(e.MinProgress,p);e.MaxProgress=Math.Max(e.MaxProgress,p);
                    if(I(chef["interactingEntityId"])==e.Board&&I(chef["serverInteractionId"])==e.Board&&I(chef["heldEntityId"])==0)e.NativeWorkingFrames++;
                }
                else if(Pure(item,e.Ingredient,true)&&e.Raw!=0&&attached!=e.Raw)
                {
                    if(e.Prepared==0){e.Prepared=attached;e.PreparedOrdinal=I(item["observedOrdinal"]);e.ReplacedFrame=I(s["gameplayFrame"]);}
                    else Check(e.Prepared==attached&&e.PreparedOrdinal==I(item["observedOrdinal"]),"Prepared item changed during its job: "+e.Job);
                }
            }
        }
        Check(last is not null&&lastFrame>firstFrame&&firstTimer-N(last["timer"])>1,"Native simulation/timer progression absent.");
        var results=new JsonArray();
        foreach(var e in expected)
        {
            Check(e.Done&&e.Prepared!=0&&e.Raw!=0&&e.NativeWorkingFrames>=2&&e.MaxProgress-e.MinProgress>.5,"Native chef interaction/progress/replacement incomplete: "+e.Job);
            int id=I(E(last!,e.Output)?["attachedEntityId"]);var food=E(last!,id);
            Check(id==e.Prepared&&food is not null&&I(food["observedOrdinal"])==e.PreparedOrdinal&&Pure(food,e.Ingredient,true),"Final pure chopped food differs from observed replacement: "+e.Job);
            results.Add(new JsonObject{["job"]=e.Job,["player"]=e.Player,["boardId"]=e.Board,["outputId"]=e.Output,["ingredientId"]=e.Ingredient,["rawEntityId"]=e.Raw,["rawObservedOrdinal"]=e.RawOrdinal,["preparedEntityId"]=e.Prepared,["preparedObservedOrdinal"]=e.PreparedOrdinal,["replacementObservedFrame"]=e.ReplacedFrame,["workProgressMin"]=e.MinProgress,["workProgressMax"]=e.MaxProgress,["clientInteractionFrames"]=e.ClientInteractionFrames,["serverInteractionFrames"]=e.ServerInteractionFrames,["sameChefNativeWorkingFrames"]=e.NativeWorkingFrames});
        }
        Check(last!["chefs"]!.AsArray().OfType<JsonObject>().Count()==4&&last["chefs"]!.AsArray().OfType<JsonObject>().All(c=>I(c["heldEntityId"])==0&&c["controlsEnabled"]?.GetValue<bool>()==true),"Final four chefs must have native control and empty hands.");
        report=new JsonObject{["ok"]=true,["classification"]="Observed native four-ingredient pantry-side chopping proof; no score or replay qualification.",["firstFrame"]=firstFrame,["lastFrame"]=lastFrame,["nativeTimerElapsed"]=firstTimer-N(last["timer"]),["fourControlledEmptyChefs"]=true,["ingredients"]=results};
    }
}
catch(Exception error){report=new JsonObject{["ok"]=false,["error"]=error.Message};Environment.ExitCode=1;}
string text=report.ToJsonString(new JsonSerializerOptions{WriteIndented=true});if(args.Length>3)File.WriteAllText(args[3],text);Console.WriteLine(text);

sealed class Evidence(string job,int player,int ingredient,double x,double z,double outX,double outZ)
{
    public string Job=job;public int Player=player,Ingredient=ingredient,Board,Output,Raw,RawOrdinal,Prepared,PreparedOrdinal,ReplacedFrame,ClientInteractionFrames,ServerInteractionFrames,NativeWorkingFrames;
    public double X=x,Z=z,OutX=outX,OutZ=outZ,MinProgress=double.PositiveInfinity,MaxProgress=double.NegativeInfinity;public bool Started,Done;
}
