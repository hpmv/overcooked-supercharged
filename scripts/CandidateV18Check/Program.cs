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
    string candidateHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("artifacts/planner-candidate-v18/OvercookedTAS.Controller.dll")));
    if (assemblyHash != candidateHash) throw new InvalidOperationException("Harness is not testing the frozen V18 binary.");
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
    Passed("Near-ready direct dough", CarnivalPlanner.NearReadyDoughSelfTest(Load("v12-heat-boundary-gf892")));
    Passed("Native supply topology", CarnivalPlanner.SupplyTopologySelfTest(Load("cycle-start"), Load("v13-buffer-boundary-gf7848"), Load("v13-buffer-boundary-gf7851")));
    Passed("Direct supply home barrier", RouteRunner.SupplyHomeBarrierSelfTest(Load("empty-lane-far-pot-prearm-state")));
    Passed("Conditional far throw barrier", RouteRunner.ConditionalFarPotSelfTest(Load("empty-lane-far-pot-prearm-state")));
    Passed("Direct leased onion harvest", CarnivalPlanner.DirectEarlyOnionSelfTest(Load("v13-onion-direct-admission-gf4216"), Load("v13-onion-direct-admission-gf2901")));
    Passed("Exact ordinary-counter onion harvest", CarnivalPlanner.DirectEarlyOnionCounterSelfTest(Load("v14-direct-onion-counter-gf3581")));
    Passed("Old partial mixer prerequisites", CarnivalPlanner.MixerPrerequisiteSelfTest(Load("v14-seed59-partial-mixer-gf9469"), Load("v14-seed59-prerequisite-context-gf9616"), Load("v14-seed59-partial-mixer-gf9929")));
    Passed("Bounded service before safe heat", CarnivalPlanner.SafeServiceHeatSelfTest(Load("v14-safe-service-gf1872")));
    Passed("Exact native prepared-flavor throw", CarnivalPlanner.PreparedFlavorThrowSelfTest("artifacts/chopped-chocolate-throw-a.jsonl.gz", "artifacts/chopped-raspberry-throw-a.jsonl.gz"));
    Passed("Exact intercepted native pot supply", CarnivalPlanner.InterceptedPotSelfTest(Load("v14-held443-gf15497"), Load("v14-held443-gf15519"), Load("v14-held443-gf15520")));
    Passed("Near-ready native pot harvest", CarnivalPlanner.NearReadyPotSelfTest(Load("near-ready-pot-audit/native-round-v14-gf789"), Load("near-ready-pot-audit/native-round-v14-gf843")));
    Passed("Washer-side exact native plating", CarnivalPlanner.WasherPlatingSelfTest(Load("washer-plating-gf1143"), Load("washer-plating-gf1159"), Load("washer-plating-gf1194"), Load("washer-plating-gf1202"), Load("washer-plating-gf1211"), Load("washer-plating-gf1299")));
    Passed("Native bakery departure and return budget", CarnivalPlanner.BakeryDepartureSelfTest(Load("v15-partial-mixer-boundaries-gf11688"), Load("v15-first-wash-complete-gf12116"), Load("v15-partial-mixer-boundaries-gf12167"), Load("v15-partial-mixer-boundaries-gf12398"), Load("v15-arrival-gf11220")));
    Passed("Exact clean-pass assembly", CarnivalPlanner.DirectCleanPassSelfTest(Load("v14-storage-snapshots/clear-clean-plate-handoff-gf7228")));
    Passed("Imminent exact native placement target", CarnivalPlanner.ImminentTargetSelfTest(Load("v16-pantry-wait-gf1440"), Load("v16-pantry-wait-gf1507"), Load("v16-pantry-wait-gf1508"), Load("v16-pantry-wait-gf1509"), Load("v16-pantry-wait-gf1510")));
    Passed("Optional intermediate waypoint continuation", NativeWaypointContinuationTests.Run());
    Passed("Near-ready exact native fryer plating", CarnivalPlanner.NearReadyFryerSelfTest(Load("near-ready-fryer-v15-gf2409"), Load("near-ready-fryer-native-gf1250"), Load("near-ready-fryer-native-gf1263"), Load("near-ready-fryer-native-gf1709"), Load("near-ready-fryer-native-gf1710"), Load("near-ready-fryer-native-gf1718"), Load("near-ready-fryer-native-gf1742")));
    Passed("Nearest available central task preference", CarnivalPlanner.NearestCentralSelfTest(Load("v16-nearest-dispatch-gf494"), Load("v16-nearest-dispatch-gf495"), Load("v16-nearest-dispatch-gf6936"), Load("v16-nearest-dispatch-gf7784")));
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, classification = "Frozen controller offline fixture checks; no game calls", assembly, assemblyHash, total = counts.Values.Sum(), counts }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
