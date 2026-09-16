using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    Console.WriteLine("Common heat arbitration: " + CarnivalPlanner.HeatArbitrationSelfTest(Read("v11-heat-boundary-gf2384"), Read("v11-heat-boundary-gf2489"), Read("v11-heat-boundary-gf2604"), Read("cycle-start")));
    Console.WriteLine("Heat storage recovery: " + CarnivalPlanner.HeatRecoverySelfTest(Read("v12-heat-boundary-gf1104"), Read("v12-heat-boundary-gf1200")));
    Console.WriteLine("Pot rescue: " + CarnivalPlanner.PotRescueSelfTest(Read("v11-heat-boundary-gf2384")));
    Console.WriteLine("Cannon preemption: " + CarnivalPlanner.CannonPreemptionSelfTest(Read("v7-cannon-boundary-postneutral-gf1250"), Read("v7-cannon-boundary-postneutral-gf1287")));
    var initial = Read("cycle-start");
    var related = new JsonObject
    {
        ["fryerRescue"] = CarnivalPlanner.FryerRescueSelfTest(Read("v10-fryer-rescue-opportunity-gf2336")),
        ["stagedRelease"] = CarnivalPlanner.StagedReleaseSelfTest(Read("v7-unplated-lease-gf958"), Read("v7-unplated-lease-gf1038"), Read("v7-unplated-lease-gf1071"), Read("v7-unplated-lease-gf1127")),
        ["sharedPantry"] = CarnivalPlanner.SharedPantrySelfTest(Read("v7-pantry-placement-gf684")),
        ["pantry"] = CarnivalPlanner.PantryChopSelfTest(initial),
        ["planner"] = CarnivalPlanner.SelfTest(initial),
        ["idleTraffic"] = CarnivalPlanner.IdleTrafficSelfTest(Read("native-round-v9-failure-state")),
        ["bakery"] = CarnivalPlanner.BakeryLookaheadSelfTest(initial, Read("bowl-offmix-a-proof")),
        ["earlyOnion"] = CarnivalPlanner.EarlyOnionSelfTest(initial),
        ["parallelOnion"] = CarnivalPlanner.ParallelOnionSelfTest(initial),
        ["fifoSafety"] = CarnivalPlanner.FifoSafetySelfTest(initial, Read("v10-fryer-8-cooked-gf2334"), Read("v10-fryer-8-burnt-gf2935"))
    };
    Console.WriteLine("Related: " + related.ToJsonString());
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
