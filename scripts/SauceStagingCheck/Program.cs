using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
try
{
    var snapshot = JsonNode.Parse(File.ReadAllText("artifacts/v11-sauce-admission-gf1346.json"))!.AsObject();
    int count = CarnivalPlanner.SauceStagingSelfTest(snapshot);
    const string trace = "artifacts/native-round-v11/trial001.jsonl.gz";
    IEnumerable<JsonObject> Observations()
    {
        using var file = File.OpenRead(trace); using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip); string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.StartsWith("{\"kind\":\"call\"", StringComparison.Ordinal)) continue;
            using var document = JsonDocument.Parse(line); var response = document.RootElement.GetProperty("response");
            if (!response.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object) continue;
            int frame = state.GetProperty("gameplayFrame").GetInt32();
            if (frame > 2155) yield break;
            if (frame >= 1346) yield return JsonNode.Parse(response.GetRawText())!.AsObject();
        }
    }
    int nativeSamples = CarnivalPlanner.SauceStagingRecordedGuardSelfTest(snapshot, Observations());
    var report = new JsonObject { ["nearSauceStaging"] = count, ["recordedNativeGuardSamples"] = nativeSamples, ["passed"] = true,
        ["traceSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(trace))).ToLowerInvariant(),
        ["checkedAssemblySha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location))).ToLowerInvariant(),
        ["qualification"] = "Offline captured-admission and synthetic ownership/lifecycle checks, not a native run." };
    File.WriteAllText("artifacts/near-sauce-check/tests.json", report.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);
    Console.WriteLine(report.ToJsonString());
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
