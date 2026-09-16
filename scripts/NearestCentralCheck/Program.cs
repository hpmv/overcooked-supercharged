using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
try
{
    var counts=new Dictionary<string,int>{
        ["nearestCentral"]=CarnivalPlanner.NearestCentralSelfTest(Read("artifacts/v16-nearest-dispatch-gf494.json"),Read("artifacts/v16-nearest-dispatch-gf495.json"),Read("artifacts/v16-nearest-dispatch-gf6936.json"),Read("artifacts/v16-nearest-dispatch-gf7784.json")),
        ["directCleanPass"]=CarnivalPlanner.DirectCleanPassSelfTest(Read("artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),qualification="Offline native captured geometry and explicitly reconstructed ownership; no native scheduler execution or timing claim"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
