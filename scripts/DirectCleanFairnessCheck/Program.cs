using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    var counts=new Dictionary<string,int>{
        ["directCleanFairness"]=CarnivalPlanner.DirectCleanFairnessSelfTest(Read("artifacts/v17-head-plate-gf14995.json"),Read("artifacts/native-round-v17/native-preview-call.json")),
        ["priorDirectClean"]=CarnivalPlanner.DirectCleanPassSelfTest(Read("artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),controllerSha256=Hash(typeof(CarnivalPlanner).Assembly.Location),
        sources=new[]{"controller/CarnivalPlanner.cs","controller/CarnivalDirectCleanPass.cs","controller/CarnivalSauceStaging.cs","controller/CarnivalDirectCleanFairnessTests.cs"}.ToDictionary(p=>p,Hash),
        nativeSnapshotSha256=Hash("artifacts/v17-head-plate-gf14995.json"),qualification="Offline actual native14995 observation with explicitly reconstructed scheduler ownership. Serialized state, object references and trace bytes verify read-only admission; no native rerun or claimed recovered time"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
