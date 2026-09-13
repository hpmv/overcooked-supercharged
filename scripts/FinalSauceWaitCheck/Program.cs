using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    Console.WriteLine("Final sauce wait: " + CarnivalPlanner.FinalSauceWaitSelfTest(Read("v16-service-departure-gf11099"), Read("v16-final-sauce-gf11104"), Read("v16-final-sauce-gf11105"), Read("v16-final-sauce-gf11184"), Read("v16-service-head-complete-gf11185"), Read("v16-service-departure-gf12407")));
    Console.WriteLine("Existing imminent head: " + CarnivalPlanner.ImminentHeadSelfTest(Read("v9-service-boundary-gf4883"), Read("v9-service-boundary-gf8425"), Read("v9-service-boundary-gf8428")));
    Console.WriteLine("Native imminent target: " + CarnivalPlanner.ImminentTargetSelfTest(Read("v16-pantry-wait-gf1440"), Read("v16-pantry-wait-gf1507"), Read("v16-pantry-wait-gf1508"), Read("v16-pantry-wait-gf1509"), Read("v16-pantry-wait-gf1510")));
    Console.WriteLine("Planner: " + CarnivalPlanner.SelfTest(Read("cycle-start")));
}
catch(Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
