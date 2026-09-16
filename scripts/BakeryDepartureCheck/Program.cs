using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
try {
var counts=new Dictionary<string,int>{
 ["bakeryDeparture"]=CarnivalPlanner.BakeryDepartureSelfTest(Read("artifacts/v15-partial-mixer-boundaries-gf11688.json"),Read("artifacts/v15-first-wash-complete-gf12116.json"),Read("artifacts/v15-partial-mixer-boundaries-gf12167.json"),Read("artifacts/v15-partial-mixer-boundaries-gf12398.json"),Read("artifacts/v15-arrival-gf11220.json")),
 ["priorMixerPrerequisites"]=CarnivalPlanner.MixerPrerequisiteSelfTest(Read("artifacts/v14-seed59-partial-mixer-gf9469.json"),Read("artifacts/v14-seed59-prerequisite-context-gf9616.json"),Read("artifacts/v14-seed59-partial-mixer-gf9929.json")),
 ["priorFifoSafety"]=CarnivalPlanner.FifoSafetySelfTest(Read("artifacts/cycle-start.json"),Read("artifacts/v10-fryer-8-cooked-gf2334.json"),Read("artifacts/v10-fryer-8-burnt-gf2935.json")),
 ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts,total=counts.Values.Sum(),qualification="Offline captured-state and explicitly synthetic mutation tests; native candidate execution pending"},new JsonSerializerOptions{WriteIndented=true}));
} catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
