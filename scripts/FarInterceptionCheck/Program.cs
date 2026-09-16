using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Load(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
var far = CarnivalPlanner.FarInterceptionSelfTest(Load("v18-failure-projection/gf4120"), Load("v18-failure-projection/gf4130"), Load("v18-failure-projection/gf4138"));
var near = CarnivalPlanner.InterceptedPotSelfTest(Load("v14-held443-gf15497"), Load("v14-held443-gf15519"), Load("v14-held443-gf15520"));
var planner = CarnivalPlanner.ConditionalFarPotSelfTest(Load("empty-lane-far-pot-prearm-state"));
var barrier = RouteRunner.ConditionalFarPotSelfTest(Load("empty-lane-far-pot-prearm-state"));
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, classification = "Offline captured fixtures and explicit mutations; native recovery execution separate", far, near, planner, barrier, total = far + near + planner + barrier }, new JsonSerializerOptions { WriteIndented = true }));
