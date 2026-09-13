using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
var reports=new Dictionary<string,int>{
 ["preparedFlavorNativeObserverAndMutations"]=CarnivalPlanner.PreparedFlavorThrowSelfTest("artifacts/chopped-chocolate-throw-a.jsonl.gz","artifacts/chopped-raspberry-throw-a.jsonl.gz"),
 ["existingPantryChopping"]=CarnivalPlanner.PantryChopSelfTest(Read("artifacts/cycle-start.json")),
 ["mixerPrerequisites"]=CarnivalPlanner.MixerPrerequisiteSelfTest(Read("artifacts/v14-seed59-partial-mixer-gf9469.json"),Read("artifacts/v14-seed59-prerequisite-context-gf9616.json"),Read("artifacts/v14-seed59-partial-mixer-gf9929.json")),
 ["existingPlanner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))};
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,reports,total=reports.Values.Sum(),qualification="File-only replay of two native mechanism observations and explicitly synthetic controller mutations; optional full production behavior remains untested in the game."},new JsonSerializerOptions{WriteIndented=true}));
