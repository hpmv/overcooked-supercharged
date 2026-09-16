using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    Console.WriteLine("Near-ready fryer: " + CarnivalPlanner.NearReadyFryerSelfTest(Read("near-ready-fryer-v15-gf2409"), Read("near-ready-fryer-native-gf1250"),
        Read("near-ready-fryer-native-gf1263"), Read("near-ready-fryer-native-gf1709"), Read("near-ready-fryer-native-gf1710"), Read("near-ready-fryer-native-gf1718"), Read("near-ready-fryer-native-gf1742")));
    Console.WriteLine("Fryer rescue: " + CarnivalPlanner.FryerRescueSelfTest(Read("v10-fryer-rescue-opportunity-gf2336")));
    Console.WriteLine("Direct clean pass: " + CarnivalPlanner.DirectCleanPassSelfTest(Read("v14-storage-snapshots/clear-clean-plate-handoff-gf7228")));
    Console.WriteLine("Native imminent target: " + CarnivalPlanner.ImminentTargetSelfTest(Read("v16-pantry-wait-gf1440"), Read("v16-pantry-wait-gf1507"), Read("v16-pantry-wait-gf1508"), Read("v16-pantry-wait-gf1509"), Read("v16-pantry-wait-gf1510")));
    Console.WriteLine("Near-ready pot: " + CarnivalPlanner.NearReadyPotSelfTest(Read("near-ready-pot-audit/native-round-v14-gf789"), Read("near-ready-pot-audit/native-round-v14-gf843")));
    Console.WriteLine("Washer plating: " + CarnivalPlanner.WasherPlatingSelfTest(Read("washer-plating-gf1143"), Read("washer-plating-gf1159"), Read("washer-plating-gf1194"), Read("washer-plating-gf1202"), Read("washer-plating-gf1211"), Read("washer-plating-gf1299")));
    Console.WriteLine("Planner: " + CarnivalPlanner.SelfTest(Read("cycle-start")));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
