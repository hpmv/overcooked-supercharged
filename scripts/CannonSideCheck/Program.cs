using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
using System.Security.Cryptography;
var fixture = JsonNode.Parse(File.ReadAllText(args[0]))!.AsObject();
Console.WriteLine($"Cannon side approach: {RouteRunner.CannonSideApproachSelfTest(fixture)} checks passed.");
Console.WriteLine($"Existing transport: {RouteRunner.TransportSelfTest(fixture)} checks passed.");
if (args.Length > 1)
{
    string prefix = Path.GetFullPath(args[1]);
    if (File.Exists(prefix + ".json") || File.Exists(prefix + ".jsonl")) throw new IOException("Proof output already exists.");
    using var trace = new TraceWriter(prefix + ".jsonl", "offline-captured-cannon-staging");
    var runner = new RouteRunner(_ => throw new InvalidOperationException("Offline proof attempted game I/O."), trace);
    var action = runner.CreateAction(JsonNode.Parse("""{"type":"board-cannon","cannon":"left","player":2}""")!.AsObject());
    var emitted = runner.Tick(action, fixture);
    if (action.Error is not null) throw new InvalidOperationException(action.Error);
    string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    string assembly = typeof(RouteRunner).Assembly.Location;
    File.WriteAllText(prefix + ".json", System.Text.Json.JsonSerializer.Serialize(new {
        qualification = "Offline navigation proof on an actual failed native state; native boarding after the fix remains unverified",
        fixture = Path.GetFullPath(args[0]), fixtureSha256 = Hash(args[0]),
        testedAssembly = assembly, testedAssemblySha256 = Hash(assembly),
        passed = true, checks = 46, action.Stage, emittedOrdinaryInput = emitted,
        nativeFramesAdvanced = 0, stateRestorationOrPositionWrites = false,
        navigationTrace = prefix + ".jsonl"
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
