using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    var initial = Read("cycle-start");
    Console.WriteLine("Original supply topology: " + CarnivalPlanner.SupplyTopologySelfTest(initial, Read("v13-buffer-boundary-gf7848"), Read("v13-buffer-boundary-gf7851")));
    var prearm = Read("empty-lane-far-pot-prearm-state");
    Console.WriteLine("Direct receiving-home barrier: " + RouteRunner.SupplyHomeBarrierSelfTest(prearm));
    Console.WriteLine("Conditional far-pot planner: " + CarnivalPlanner.ConditionalFarPotSelfTest(prearm));
    Console.WriteLine("Conditional far-pot input barriers: " + RouteRunner.ConditionalFarPotSelfTest(prearm));
    Console.WriteLine("Planner: " + CarnivalPlanner.SelfTest(initial));
    Console.WriteLine("Pot rescue: " + CarnivalPlanner.PotRescueSelfTest(Read("v11-heat-boundary-gf2384")));
    Console.WriteLine("Sausage buffer: " + CarnivalPlanner.SausageBufferSelfTest(Read("v9-sausage-buffer-gf7454"), Read("v7-unplated-lease-gf1108")));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
