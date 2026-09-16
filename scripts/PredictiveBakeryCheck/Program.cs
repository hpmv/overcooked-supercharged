using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    var counts=new Dictionary<string,int>{["predictiveBakery"]=CarnivalPlanner.PredictiveBakerySelfTest(
        Read("artifacts/v16-supply-snapshots/gf12373.json"),Read("artifacts/v16-supply-snapshots/gf11069.json"),Read("artifacts/v16-supply-snapshots/gf11445.json"),
        Read("artifacts/v16-supply-snapshots/gf14401.json"),Read("artifacts/v16-supply-snapshots/gf15625.json"),Read("artifacts/native-round-v16/native-preview-call.json")),
        ["existingDeparture"]=CarnivalPlanner.BakeryDepartureSelfTest(Read("artifacts/v15-partial-mixer-boundaries-gf11688.json"),Read("artifacts/v15-first-wash-complete-gf12116.json"),Read("artifacts/v15-partial-mixer-boundaries-gf12167.json"),Read("artifacts/v15-partial-mixer-boundaries-gf12398.json"),Read("artifacts/v15-arrival-gf11220.json")),
        ["existingMixerPrerequisites"]=CarnivalPlanner.MixerPrerequisiteSelfTest(Read("artifacts/v14-seed59-partial-mixer-gf9469.json"),Read("artifacts/v14-seed59-prerequisite-context-gf9616.json"),Read("artifacts/v14-seed59-partial-mixer-gf9929.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    var sources=new[]{"controller/CarnivalPredictiveBakery.cs","controller/CarnivalPredictiveBakeryTests.cs","controller/CarnivalPlanner.cs","controller/Program.cs","scripts/PredictiveBakeryCheck/Program.cs"}.ToDictionary(p=>p,Hash);
    var fixtures=new[]{12373,11069,11445,14401,15625}.Select(f=>$"artifacts/v16-supply-snapshots/gf{f}.json").ToDictionary(p=>p,Hash);
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),controllerSha256=Hash(typeof(CarnivalPlanner).Assembly.Location),sources,fixtures,qualification="Offline actual native fixtures with explicitly reconstructed planner leases and synthetic negative/lifecycle continuations; no native scheduling run"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
