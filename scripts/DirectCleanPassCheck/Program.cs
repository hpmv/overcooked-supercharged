using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
try
{
    var counts=new Dictionary<string,int>{
        ["directCleanPass"]=CarnivalPlanner.DirectCleanPassSelfTest(Read("artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json")),
        ["washerPlating"]=CarnivalPlanner.WasherPlatingSelfTest(Read("artifacts/washer-plating-gf1143.json"),Read("artifacts/washer-plating-gf1159.json"),Read("artifacts/washer-plating-gf1194.json"),Read("artifacts/washer-plating-gf1202.json"),Read("artifacts/washer-plating-gf1211.json"),Read("artifacts/washer-plating-gf1299.json")),
        ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
    Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),qualification="Offline captured-state admission and explicit synthetic ownership/native-state mutation tests; production native trial pending"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
