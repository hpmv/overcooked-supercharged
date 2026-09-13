using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
try
{
    var counts=new Dictionary<string,int>{
        ["imminentNativeTarget"]=CarnivalPlanner.ImminentTargetSelfTest(Read("artifacts/v16-pantry-wait-gf1440.json"),Read("artifacts/v16-pantry-wait-gf1507.json"),Read("artifacts/v16-pantry-wait-gf1508.json"),Read("artifacts/v16-pantry-wait-gf1509.json"),Read("artifacts/v16-pantry-wait-gf1510.json")),
        ["priorImminentHead"]=CarnivalPlanner.ImminentHeadSelfTest(Read("artifacts/v9-service-boundary-gf4883.json"),Read("artifacts/v9-service-boundary-gf8425.json"),Read("artifacts/v9-service-boundary-gf8428.json")),
        ["directCleanPass"]=CarnivalPlanner.DirectCleanPassSelfTest(Read("artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),qualification="Offline captured-state and explicit synthetic ownership mutations; no native execution of changed scheduler"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
