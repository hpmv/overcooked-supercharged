using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Load(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    var counts = new Dictionary<string, int>();
    void Passed(string name, int count) { Console.WriteLine(name + ": " + count + " assertions passed"); counts.Add(name, count); }
    string assembly = typeof(CarnivalPlanner).Assembly.Location;
    string assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly)));
    string candidateHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("artifacts/planner-candidate-v13/OvercookedTAS.Controller.dll")));
    if (assemblyHash != candidateHash) throw new InvalidOperationException("Harness is not testing the frozen V13 binary.");
    Passed("Raw sausage reserve", CarnivalPlanner.SausageBufferSelfTest(Load("v9-sausage-buffer-gf7454"), Load("v7-unplated-lease-gf1108")));
    Passed("Shared pantry chopping", CarnivalPlanner.SharedPantrySelfTest(Load("v7-pantry-placement-gf684")));
    Passed("Imminent FIFO service wait", CarnivalPlanner.ImminentHeadSelfTest(Load("v9-service-boundary-gf4883"), Load("v9-service-boundary-gf8425"), Load("v9-service-boundary-gf8428")));
    Passed("FIFO washer and native ruin", CarnivalPlanner.FifoSafetySelfTest(Load("cycle-start"), Load("v10-fryer-8-cooked-gf2334"), Load("v10-fryer-8-burnt-gf2935")));
    Passed("Native fryer rescue", CarnivalPlanner.FryerRescueSelfTest(Load("v10-fryer-rescue-opportunity-gf2336")));
    Passed("Native pot rescue", CarnivalPlanner.PotRescueSelfTest(Load("v11-heat-boundary-gf2384")));
    Passed("Common heat arbitration", CarnivalPlanner.HeatArbitrationSelfTest(Load("v11-heat-boundary-gf2384"), Load("v11-heat-boundary-gf2489"), Load("v11-heat-boundary-gf2604"), Load("cycle-start")));
    Passed("Cannon boundary preemption", CarnivalPlanner.CannonPreemptionSelfTest(Load("v7-cannon-boundary-postneutral-gf1250"), Load("v7-cannon-boundary-postneutral-gf1287")));
    Passed("Staged resource release", CarnivalPlanner.StagedReleaseSelfTest(Load("v7-unplated-lease-gf958"), Load("v7-unplated-lease-gf1038"), Load("v7-unplated-lease-gf1071"), Load("v7-unplated-lease-gf1127")));
    Passed("Pantry chopping", CarnivalPlanner.PantryChopSelfTest(Load("cycle-start")));
    Passed("Ordinary planner", CarnivalPlanner.SelfTest(Load("cycle-start")));
    Passed("Idle chef traffic", CarnivalPlanner.IdleTrafficSelfTest(Load("native-round-v9-failure-state")));
    Passed("Bakery lookahead", CarnivalPlanner.BakeryLookaheadSelfTest(Load("cycle-start"), Load("bowl-offmix-a-proof")));
    Passed("Early onion", CarnivalPlanner.EarlyOnionSelfTest(Load("cycle-start")));
    Passed("Parallel onion", CarnivalPlanner.ParallelOnionSelfTest(Load("cycle-start")));
    Passed("Heat storage recovery", CarnivalPlanner.HeatRecoverySelfTest(Load("v12-heat-boundary-gf1104"), Load("v12-heat-boundary-gf1200")));
    Passed("Near sauce staging", CarnivalPlanner.SauceStagingSelfTest(Load("v11-sauce-admission-gf1346")));
    Passed("Cannon flight ownership", CarnivalPlanner.CannonFlightSelfTest(Load("v7-cannon-boundary-postneutral-gf1287")));
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, classification = "Frozen controller offline fixture checks; no game calls", assembly, assemblyHash, total = counts.Values.Sum(), counts }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
