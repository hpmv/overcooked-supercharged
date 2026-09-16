using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
try
{
Console.WriteLine("Intercepted native pot source: " + CarnivalPlanner.InterceptedPotSelfTest(Read("v14-held443-gf15497"), Read("v14-held443-gf15519"), Read("v14-held443-gf15520")));
Console.WriteLine("Planner: " + CarnivalPlanner.SelfTest(Read("cycle-start")));
Console.WriteLine("Supply topology: " + CarnivalPlanner.SupplyTopologySelfTest(Read("cycle-start"), Read("v13-buffer-boundary-gf7848"), Read("v13-buffer-boundary-gf7851")));
Console.WriteLine("Direct-home input guard: " + RouteRunner.SupplyHomeBarrierSelfTest(Read("empty-lane-far-pot-prearm-state")));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
