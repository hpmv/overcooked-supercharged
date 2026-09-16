using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Reads closed files only. No native client, input generator, or controller mutation.
int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString());
double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
long L(JsonNode? n) => n is null ? -1 : long.Parse(n.ToString());
bool B(JsonNode? n) => n?.GetValue<bool>() == true;
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(f)); }
JsonObject State(JsonObject r) => r["state"] as JsonObject ?? r;
JsonObject? Entity(JsonObject s, int id) => s["entities"]!.AsArray().OfType<JsonObject>().SingleOrDefault(e => I(e["id"]) == id);
JsonObject Chef(JsonObject s, int player) => s["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == player);
bool Pure(JsonObject? e, params int[] ids) => e is not null && CarnivalRecipes.ClassifyEntity(e).Food.IngredientIds.Order().SequenceEqual(ids.Order()) && !CarnivalRecipes.ClassifyEntity(e).Food.IsRuined;
JsonObject? Registry(JsonObject s, int id) => s["entityRegistration"]?["events"]?.AsArray().OfType<JsonObject>()
    .Where(e => I(e["entity"]?["entityId"]) == id).OrderByDescending(e => L(e["sequence"])).FirstOrDefault();
var issues = new List<string>();
void Check(bool ok, string message) { if (!ok && !issues.Contains(message)) issues.Add(message); }
void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
const string FinalJob = "FB04-far-flour-full-near";
try
{
    Require(args.Length == 4, "Usage: preflight PLAN INITIAL OUTPUT | TRACE PLAN RESULT OUTPUT");
    bool preflight = args[0] == "preflight";
    string planPath = args[1]; var plan = Read(planPath); var jobs = plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
    Require(jobs.Length == 4 && jobs.SelectMany(j => j["actions"]!.AsArray()).Count() == 16 && I(plan["timeoutFrames"]) == 1800, "Unexpected probe job/action bounds.");
    for (int j = 0; j < jobs.Length; j++)
    {
        Require(I(jobs[j]["player"]) == 1 && jobs[j]["dependencies"]!.AsArray().Select(n => n!.ToString()).SequenceEqual(j == 0 ? [] : new[] { jobs[j-1]["id"]!.ToString() }), "Probe must be sequential P1-only jobs.");
        Require(jobs[j]["actions"]!.AsArray().OfType<JsonObject>().All(a => I(a["timeoutFrames"]) > 0 && a["type"]?.ToString() is "take" or "place" or "chop" or "navigate" or "throw" or "wait"), "Unexpected or unbounded probe action.");
    }
    var old = Read("routes/probes/chopped-chocolate-throw.json");
    Require(jobs.Take(3).Select(j => j["actions"]!.ToJsonString()).SequenceEqual(old["jobs"]!.AsArray().Take(3).Select(j => j!["actions"]!.ToJsonString())), "Native near-kit setup differs from the previously proved first12 actions.");
    string bundle = Path.GetFullPath("artifacts/planner-candidate-v21"); var manifest = Read(Path.Combine(bundle, "manifest.json"));
    string controllerHash = Hash(typeof(CarnivalRecipes).Assembly.Location);
    Require(controllerHash == manifest["controllerSha256"]?.ToString(), "Checker classifier differs from frozen V21.");
    int near = 0, far = 0, nearHome = 0, farHome = 0; var identities = new Dictionary<int,int>();
    void Resolve(JsonObject s)
    {
        var m = KitchenModel.Build(s); Require(m.Scene == "s_Day_3_4", "Wrong scene.");
        nearHome = m.Resolve("mix-station:dlc08_workstation_mixer@24.00,-10.80").EntityId;
        farHome = m.Resolve("mix-station:dlc08_workstation_mixer@22.80,-10.80").EntityId;
        near = I(Entity(s, nearHome)?["attachedEntityId"]); far = I(Entity(s, farHome)?["attachedEntityId"]);
        Require(near != 0 && far != 0 && near != far, "Original homes lack distinct bowls.");
        foreach (int id in new[] { near, far, nearHome, farHome }) identities.Add(id, I(Entity(s, id)?["observedOrdinal"]));
        Require(new[] { near, far }.All(id => Pure(Entity(s,id)) && N(Entity(s,id)?["mixingProgress"]) == 0), "Probe must start with native empty reset bowls.");
    }
    if (preflight)
    {
        var s = State(Read(args[2])); Resolve(s); var m = KitchenModel.Build(s); var pos = KitchenModel.Position(Chef(s,1)["position"]); var paths = new JsonArray();
        foreach (var action in jobs.SelectMany(j => j["actions"]!.AsArray().OfType<JsonObject>()))
        {
            if (action["type"]!.ToString() == "wait") continue;
            if (action["target"] is {} target)
            {
                var p = Navigation.FindPath(m, pos, KitchenModel.Position(target)); Require(p.Success, "Throw staging is unreachable."); pos = p.Points[^1];
                paths.Add(JsonSerializer.SerializeToNode(p)); continue;
            }
            var station = m.Resolve(action["station"]!.ToString());
            if (action["type"]!.ToString() == "throw") { Require(I(Entity(s,station.EntityId)?["attachedEntityId"]) is var id && (id == near || id == far), "Throw does not address original bowl home."); continue; }
            var path = Navigation.ToStation(m,pos,station); Require(path.Success, "Station approach unavailable: " + station.Key); pos = path.Points[^1]; paths.Add(JsonSerializer.SerializeToNode(path));
        }
        File.WriteAllText(args[3], new JsonObject { ["ok"] = true, ["jobs"] = 4, ["actions"] = 16, ["near"] = near, ["far"] = far,
            ["nearHome"] = nearHome, ["farHome"] = farHome, ["paths"] = paths, ["planSha256"] = Hash(planPath), ["snapshotSha256"] = Hash(args[2]),
            ["classifierSha256"] = controllerHash, ["qualification"] = "Read-only native static-path/selector preflight. Far flight/catch remains untested." }.ToJsonString(new(){WriteIndented=true}));
        Console.WriteLine("Preflight PASS: 4 jobs,16 actions; unique original bowl/home selectors and static paths."); return;
    }

    JsonObject? last = null, start = null; string? nearContents = null, nearChildren = null, pluginHash = null, instrumentation = null;
    var done = new HashSet<string>(); var commands = new Dictionary<string,int>(); var trajectory = new JsonArray();
    bool active = false, sawFlight = false, sawSourceRemoval = false, accepted = false; int calls = 0, first = -1, lastFrame = -1, startFrame = -1, source = 0, sourceOrdinal = -1, acceptedFrame = -1, flightFrame = -1, samples = 0;
    long sourceRegistration = -1; int sourceUnity = 0; double minProgress = double.PositiveInfinity, maxProgress = double.NegativeInfinity;
    void Observe(JsonObject s)
    {
        if (!active) return; int frame = I(s["gameplayFrame"]); if (frame == lastFrame) return;
        if (lastFrame >= 0) Check(frame == lastFrame + 1, "Missing native sample during far transaction."); lastFrame = frame; samples++;
        foreach (var p in identities) Check(Entity(s,p.Key) is {} e && B(e["active"]) && I(e["observedOrdinal"]) == p.Value, "Original bowl/home incarnation changed.");
        var a = Entity(s,near); var b = Entity(s,far);
        Check(I(Entity(s,nearHome)?["attachedEntityId"]) == near && I(Entity(s,farHome)?["attachedEntityId"]) == far &&
            s["entities"]!.AsArray().OfType<JsonObject>().Count(e => I(e["attachedEntityId"]) == near) == 1 &&
            s["entities"]!.AsArray().OfType<JsonObject>().Count(e => I(e["attachedEntityId"]) == far) == 1, "Original bowl attachment changed.");
        Check(s["chefs"]!.AsArray().OfType<JsonObject>().All(c => I(c["heldEntityId"]) != near && I(c["heldEntityId"]) != far), "An original bowl was carried.");
        Check(a?["contents"]?.ToJsonString() == nearContents && a?["composition"]?["children"]?.ToJsonString() == nearChildren &&
            Pure(a,CarnivalRecipes.Flour.Id,CarnivalRecipes.Egg.Id,CarnivalRecipes.Chocolate.Id), "Near kit contents changed or were ruined.");
        double progress = N(a?["mixingProgress"]); Check(double.IsFinite(progress) && progress >= 0 && progress < 21, "Near bowl passed its finite native21s guard.");
        if(double.IsFinite(progress)){minProgress=Math.Min(minProgress,progress);maxProgress=Math.Max(maxProgress,progress);}
        var chef = Chef(s,1); int held = I(chef["heldEntityId"]);
        if (source == 0 && held != 0)
        {
            var e = Entity(s,held); var r = Registry(s,held);
            Check(Pure(e,CarnivalRecipes.Flour.Id) && r?["kind"]?.ToString() == "register", "Fresh far source lacks native pure Flour registration.");
            source = held; sourceOrdinal = I(e?["observedOrdinal"]); sourceRegistration = L(r?["entity"]?["observedRegistrationSequence"]); sourceUnity = I(r?["entity"]?["unityInstanceId"]);
        }
        if (source != 0)
        {
            var e = Entity(s,source); var r = Registry(s,source);
            Check(e is null || I(e["observedOrdinal"]) == sourceOrdinal, "Far source ID was reused.");
            Check(r is null || L(r["entity"]?["observedRegistrationSequence"]) == sourceRegistration && I(r["entity"]?["unityInstanceId"]) == sourceUnity, "Far source registration changed.");
            if (B(e?["throwFlying"]))
            {
                Check(I(e?["throwerEntityId"]) == I(chef["entityId"]), "Another chef threw the source."); sawFlight = true; if(flightFrame<0)flightFrame=frame;
            }
            if(r?["kind"]?.ToString()=="remove")sawSourceRemoval=true;
            trajectory.Add(new JsonObject { ["frame"] = frame, ["held"] = held, ["sourceActive"] = e?["active"]?.DeepClone(), ["position"] = e?["position"]?.DeepClone(),
                ["colliders"] = e?["colliders"]?.DeepClone(), ["flying"] = e?["throwFlying"]?.DeepClone(), ["flightTime"] = e?["throwFlightTime"]?.DeepClone(),
                ["thrower"] = e?["throwerEntityId"]?.DeepClone(), ["sourceRegistryKind"] = r?["kind"]?.DeepClone(), ["farContents"] = b?["contents"]?.DeepClone(),
                ["attachmentParents"] = new JsonArray(s["entities"]!.AsArray().OfType<JsonObject>().Where(parent => I(parent["attachedEntityId"]) == source).Select(parent => JsonValue.Create(I(parent["id"])) as JsonNode).ToArray()) });
            if(Pure(b,CarnivalRecipes.Flour.Id))
            {
                Check(sawFlight && held==0 && (e is null || !B(e["active"])), "Far contents delta lacks same-source native flight/consumption.");
                accepted=true;if(acceptedFrame<0)acceptedFrame=frame;
            }
        }
        Check(Pure(b) || Pure(b,CarnivalRecipes.Flour.Id), "Unexpected far bowl ingredients.");
    }
    using var file = new FileStream(args[0],FileMode.Open,FileAccess.Read,FileShare.Read);
    Require(file.Length is >0 and <1_000_000_000,"Probe file exceeds bounded size."); string traceHash=Convert.ToHexStringLower(SHA256.HashData(file));file.Position=0;
    using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);int lines=0;
    while(reader.ReadLine() is {} line)
    {
        Require(++lines<15000&&line.Length<8_000_000,"Probe exceeds bounded read budget.");var row=JsonNode.Parse(line)!.AsObject();
        if(row["kind"]?.ToString()=="event")
        {
            string? name=row["name"]?.ToString(),id=row["value"]?["id"]?.ToString();
            Check(name is not("planFailure" or "actionFailure"),"Native route reported failure: "+name);
            if(name=="jobStart"&&id==FinalJob)
            {
                Require(last is not null,"Far job lacks start state.");start=last!.DeepClone().AsObject();startFrame=I(start["gameplayFrame"]);
                nearContents=Entity(start,near)?["contents"]?.ToJsonString();nearChildren=Entity(start,near)?["composition"]?["children"]?.ToJsonString();
                Check(Entity(start,near)?["contents"]?.AsArray().Count==3&&Pure(Entity(start,near),18448,16620,22804),"Near kit is not exactly three native ingredients before far supply.");
                Check(Pure(Entity(start,far))&&N(Entity(start,far)?["mixingProgress"])==0,"Far bowl was not empty/reset before source issuance.");active=true;Observe(start);
            }
            if(name=="jobComplete"&&id is not null)done.Add(id);continue;
        }
        if(row["kind"]?.ToString()!="call")continue;Require(++calls<=3000,"Too many probe calls.");var request=row["request"]!.AsObject();string command=request["command"]!.ToString();
        commands[command]=commands.GetValueOrDefault(command)+1;Check(command is "restart" or "inspect" or "step","Unexpected native command: "+command);
        if(command=="step")
        {
            Check(I(request["steps"])==1&&request.All(p=>new[]{"version","command","steps","inputs"}.Contains(p.Key)),"Step differs from one-frame ordinary input schema.");
            var pads=request["inputs"]!.AsArray().OfType<JsonObject>().ToArray();Check(pads.Length==4&&pads.Select(p=>I(p["player"])).Order().SequenceEqual(new[]{0,1,2,3}),"Four unique inputs required.");
            foreach(var pad in pads)
            {
                Check(double.IsFinite(N(pad["x"]))&&double.IsFinite(N(pad["y"]))&&Math.Abs(N(pad["x"]))<=1&&Math.Abs(N(pad["y"]))<=1,"Invalid native stick input.");
                if(I(pad["player"])!=1)Check(N(pad["x"])==0&&N(pad["y"])==0&&!B(pad["use"])&&!B(pad["pickup"])&&!B(pad["dash"]),"Another chef received probe inputs.");
            }
        }
        var response=row["response"]!.AsObject();Check(B(response["ok"]),"Native call failed.");if(response["state"] is not JsonObject s)continue;last=s;
        if(first<0){Resolve(s);first=I(s["gameplayFrame"]);pluginHash=s["instrumentation"]?["manifest"]?["pluginSha256"]?.ToString();instrumentation=s["instrumentation"]?["manifestSha256"]?.ToString();}
        Check(s["instrumentation"]?["manifestSha256"]?.ToString()==instrumentation&&pluginHash is not null,"Instrumentation changed or missing.");
        Check(I(s["score"])==0&&I(s["delivered"])==0&&I(s["deductions"])==0,"Probe affected native scoring.");Observe(s);
    }
    Check(active&&samples>=3&&accepted&&sawFlight&&sawSourceRemoval,"Exact far native catch/source-removal proof incomplete.");
    Check(done.SetEquals(jobs.Select(j=>j["id"]!.ToString())),"Not all four native jobs completed.");
    if(last is not null)Check(last["chefs"]!.AsArray().OfType<JsonObject>().All(c=>I(c["heldEntityId"])==0&&B(c["controlsEnabled"])),"Final chefs are not empty-handed and controlled.");
    JsonObject? result=File.Exists(args[2])&&new FileInfo(args[2]).Length>0?Read(args[2]):null;
    Check(result is not null&&B(result["ok"])&&B(result["paused"]),"No successful paused result receipt.");
    if(result?["state"] is JsonObject final&&last is not null)Check(I(final["gameplayFrame"])==I(last["gameplayFrame"])&&Pure(Entity(final,far),18448),"Result differs from final far catch observation.");
    var report=new JsonObject { ["ok"]=issues.Count==0,["classification"]=issues.Count==0?"Native full-near-kit to original far-bowl Flour catch proved":"Native mechanism did not pass; inspect preserved trajectory and failure reasons",
        ["issues"]=JsonSerializer.SerializeToNode(issues),["nearBowl"]=near,["nearHome"]=nearHome,["farBowl"]=far,["farHome"]=farHome,["identities"]=JsonSerializer.SerializeToNode(identities),
        ["initialFrame"]=first,["farJobStartedFrame"]=startFrame,["finalFrame"]=lastFrame,["samples"]=samples,["source"]=source,["sourceOrdinal"]=sourceOrdinal,
        ["sourceRegistration"]=sourceRegistration,["sourceUnityInstance"]=sourceUnity,["firstFlightFrame"]=flightFrame,["farAcceptedFrame"]=acceptedFrame,
        ["sourceRemovalObserved"]=sawSourceRemoval,["nearContentsUnchanged"]=!issues.Any(s=>s.Contains("Near kit")),["nearMixingProgressMin"]=double.IsFinite(minProgress)?minProgress:null,
        ["nearMixingProgressMax"]=double.IsFinite(maxProgress)?maxProgress:null,["commands"]=JsonSerializer.SerializeToNode(commands),["trajectory"]=trajectory,
        ["traceSha256"]=traceHash,["planSha256"]=Hash(planPath),["resultSha256"]=File.Exists(args[2])?Hash(args[2]):null,["pluginSha256"]=pluginHash,["instrumentationManifestSha256"]=instrumentation,
        ["controllerSha256"]=controllerHash,["controllerSourceTreeSha256"]=manifest["controllerSourceTreeSha256"]?.DeepClone(),["checkerSourceSha256"]=Hash("scripts/FarBowlProbeCheck/Program.cs"),
        ["limitations"]="Capacity rejection follows pinned native scene/source rules, not a catch-rejection observer. A failed trajectory alone does not prove a specific collision. Executed controller identified by operator; frozen classifier hash independently checked. No future route speed or score proof." };
    File.WriteAllText(args[3],report.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"{(issues.Count==0?"PASS":"FAIL")}: far source{source}, flight{flightFrame}, accepted{acceptedFrame}, samples{samples}; "+string.Join("; ",issues));
    Environment.ExitCode=issues.Count==0?0:1;
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
