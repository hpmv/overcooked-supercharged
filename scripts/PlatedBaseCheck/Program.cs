using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
    var counts=new Dictionary<string,int>{["platedHotdogBase"]=CarnivalPlanner.PlatedHotdogBaseSelfTest(Read("artifacts/v16-plate-first-opportunities/gf789.json"),Read("artifacts/v16-plate-first-opportunities/gf6377.json"),Read("artifacts/native-round-v16/native-preview-call.json")),
        ["platedOnion"]=CarnivalPlanner.PlatedOnionSelfTest(Read("artifacts/v16-onion-chain-gf5730.json")),
        ["fairness"]=CarnivalPlanner.DirectCleanFairnessSelfTest(Read("artifacts/v17-head-plate-gf14995.json"),Read("artifacts/native-round-v17/native-preview-call.json")),
        ["nearReadyPot"]=CarnivalPlanner.NearReadyPotSelfTest(Read("artifacts/near-ready-pot-audit/native-round-v14-gf789.json"),Read("artifacts/near-ready-pot-audit/native-round-v14-gf843.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),controllerSha256=Hash(typeof(CarnivalPlanner).Assembly.Location),
        sources=new[]{"controller/CarnivalPlatedHotdogBase.cs","controller/CarnivalPlatedHotdogBaseTests.cs","controller/CarnivalPlanner.cs","controller/Program.cs"}.ToDictionary(p=>p,Hash),
        fixtureHash=Hash("artifacts/v16-plate-first-opportunities/gf789.json"),qualification="Offline unchanged native789 candidate and actual6377 exclusion, explicit reconstructed metadata and synthetic transition/mutation tests; no native performance or completed score claim"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
