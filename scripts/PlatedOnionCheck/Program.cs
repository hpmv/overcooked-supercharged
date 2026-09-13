using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    var counts=new Dictionary<string,int>{
        ["platedOnion"]=CarnivalPlanner.PlatedOnionSelfTest(Read("artifacts/v16-onion-chain-gf5730.json")),
        ["directCleanFairness"]=CarnivalPlanner.DirectCleanFairnessSelfTest(Read("artifacts/v17-head-plate-gf14995.json"),Read("artifacts/native-round-v17/native-preview-call.json")),
        ["directClean"]=CarnivalPlanner.DirectCleanPassSelfTest(Read("artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json")),
        ["directEarlyOnion"]=CarnivalPlanner.DirectEarlyOnionSelfTest(Read("artifacts/v13-onion-direct-admission-gf4216.json"),Read("artifacts/v13-onion-direct-admission-gf2901.json")),
        ["earlyOnion"]=CarnivalPlanner.EarlyOnionSelfTest(Read("artifacts/cycle-start.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),controllerSha256=Hash(typeof(CarnivalPlanner).Assembly.Location),
        sources=new[]{"controller/CarnivalPlanner.cs","controller/Program.cs","controller/CarnivalPlatedOnion.cs","controller/CarnivalPlatedOnionTests.cs","controller/CarnivalEarlyOnion.cs"}.ToDictionary(p=>p,Hash),
        nativeSnapshotSha256=Hash("artifacts/v16-onion-chain-gf5730.json"),qualification="Offline unchanged native5730 admission with explicit scheduler lease reconstruction and synthetic native state transitions/mutations; no game commands, native production admission or saved-time claim"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
