using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

try
{
    JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    JsonObject At(int frame) => Read($"artifacts/v19-bakery-priority-gf{frame}.json");
    string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    var counts = new Dictionary<string, int>
    {
        ["fifoEmptyBowls"] = CarnivalPlanner.FifoEmptyBowlsSelfTest(At(13165), At(11738), At(12355), At(13247),
            Read("artifacts/native-round-v19/native-preview-call.json"), JsonNode.Parse(File.ReadAllText("artifacts/v19-late-bakery-events.json"))!.AsArray()),
        ["existingPredictiveBakery"] = CarnivalPlanner.PredictiveBakerySelfTest(
            Read("artifacts/v16-supply-snapshots/gf12373.json"), Read("artifacts/v16-supply-snapshots/gf11069.json"), Read("artifacts/v16-supply-snapshots/gf11445.json"),
            Read("artifacts/v16-supply-snapshots/gf14401.json"), Read("artifacts/v16-supply-snapshots/gf15625.json"), Read("artifacts/native-round-v16/native-preview-call.json")),
        ["existingDeparture"] = CarnivalPlanner.BakeryDepartureSelfTest(Read("artifacts/v15-partial-mixer-boundaries-gf11688.json"), Read("artifacts/v15-first-wash-complete-gf12116.json"), Read("artifacts/v15-partial-mixer-boundaries-gf12167.json"), Read("artifacts/v15-partial-mixer-boundaries-gf12398.json"), Read("artifacts/v15-arrival-gf11220.json")),
        ["existingMixerPrerequisites"] = CarnivalPlanner.MixerPrerequisiteSelfTest(Read("artifacts/v14-seed59-partial-mixer-gf9469.json"), Read("artifacts/v14-seed59-prerequisite-context-gf9616.json"), Read("artifacts/v14-seed59-partial-mixer-gf9929.json")),
        ["planner"] = CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json"))
    };
    var sources = new[] { "controller/CarnivalFifoEmptyBowls.cs", "controller/CarnivalFifoEmptyBowlsTests.cs", "controller/CarnivalPlanner.cs", "controller/Program.cs", "scripts/FifoEmptyBowlsCheck/Program.cs" }.ToDictionary(p => p, Hash);
    var fixtures = new[] { 11738, 12355, 13165, 13247 }.Select(f => $"artifacts/v19-bakery-priority-gf{f}.json")
        .Append("artifacts/v19-late-bakery-events.json").Append("artifacts/native-round-v19/native-preview-call.json").ToDictionary(p => p, Hash);
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, counts, total = counts.Values.Sum(), controllerSha256 = Hash(typeof(CarnivalPlanner).Assembly.Location), sources, fixtures,
        qualification = "Offline captured native observations with explicitly reconstructed sticky metadata/job ownership and synthetic negative mutations. No native execution or counterfactual score/time proof." }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
