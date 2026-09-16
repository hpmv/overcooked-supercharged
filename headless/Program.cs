using Google.Protobuf;
using Hpmv;
using Supercharged.Headless;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

var stdout = Console.Out;
Console.SetOut(Console.Error);
var options = new Dictionary<string, string>(StringComparer.Ordinal);
string command = args.FirstOrDefault() ?? "help";
for (int i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected --option: " + args[i]);
    string key = args[i][2..]; string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
    if (!options.TryAdd(key, value)) throw new ArgumentException("Duplicate option: " + key);
}
int Int(string name, int fallback) => options.TryGetValue(name, out string text) ? int.Parse(text) : fallback;
string Text(string name, string fallback) => options.GetValueOrDefault(name, fallback);
bool Has(string name) => options.TryGetValue(name, out string text) && text == "true";
GameSetup Setup() => options.TryGetValue("setup", out string path) ? Hpmv.Save.GameSetup.Parser.ParseFrom(File.ReadAllBytes(path)).FromProto() :
    Text("level", "carnival34") switch { "carnival34" => new Carnival34FourLevel(), "story11" => new Story11FourLevel(Has("discover")),
        _ => throw new ArgumentException("Use --level carnival34|story11 or an explicit --setup protobuf file.") };
string root = Path.GetFullPath(Text("evidence-root", "artifacts/framework-headless"));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (command == "help")
    {
        await stdout.WriteLineAsync("Headless framework: serve [--game-port 14455 --control-port 17637 --level carnival34|story11|--setup file.pb --evidence-root directory --trace trace.jsonl --seed N --realtime]; inspect [--full]; pause; resume; step --frames N (2..36000); goto --chef ID --x X --z Z --frames N (2..600); checkpoint --path name.pb; warp --frame N --development; reconstruct --input trace.jsonl; selftest. Story11 initially provides gated native registry inspection. All client commands accept --control-port. Checkpoint/warp are development tools, not native score proof.");
        return;
    }
    if (command == "selftest") { await stdout.WriteLineAsync(SessionTests.Run(root).ToJsonString()); return; }
    if (command == "story11-selftest") { await stdout.WriteLineAsync(Story11Tests.Run(root).ToJsonString()); return; }
    if (command == "proxy-retirement-selftest") { await stdout.WriteLineAsync(ProxyRetirementTests.Run(Text("input",""),root).ToJsonString()); return; }
    if (command == "actions-selftest") { await stdout.WriteLineAsync(new JsonObject { ["ok"] = true, ["checks"] = TypedActionTests.Run(root), ["gameCalls"] = 0 }.ToJsonString()); return; }
    if (command == "status-selftest") { await stdout.WriteLineAsync(new JsonObject { ["ok"] = true, ["checks"] = StatusTests.Run(root), ["gameCalls"] = 0 }.ToJsonString()); return; }
    if (command == "transfer-selftest") { await stdout.WriteLineAsync(new JsonObject { ["ok"] = true, ["checks"] = ObservedTransferTests.RunSynthetic(root), ["gameCalls"] = 0 }.ToJsonString()); return; }
    if (command == "transfer-captured-selftest") { await stdout.WriteLineAsync(ObservedTransferTests.RunCaptured(Text("checkpoint", ""), Text("observations", ""), Text("edges", "")).ToJsonString()); return; }
    if (command == "registration-selftest") { await stdout.WriteLineAsync(new JsonObject { ["ok"] = true, ["checks"] = RegistrationPoseTests.Run(), ["gameCalls"] = 0 }.ToJsonString()); return; }
    if (command == "trace-selftest") { await stdout.WriteLineAsync(TraceTests.Run(root).ToJsonString()); return; }
    if (command == "warp-history-selftest") { await stdout.WriteLineAsync(WarpHistoryTests.Run(Text("checkpoint", ""), Text("fixture", "")).ToJsonString()); return; }
    if (command == "physics-container-selftest") { await stdout.WriteLineAsync(PhysicsContainerTests.Run(Text("checkpoint", ""), Text("fixture", "")).ToJsonString()); return; }
    if (command == "dynamic-warp-selftest") { await stdout.WriteLineAsync(new JsonObject { ["ok"] = true, ["checks"] = DynamicWarpTests.Run(), ["gameCalls"] = 0, ["qualification"] = "Actual fixed native-G metadata with synthetic nested ingredient lifecycle; native dynamic parity pending." }.ToJsonString()); return; }
    if (command == "transport-selftest") { await stdout.WriteLineAsync((await TransportFixture.Run(Text("host-dll", typeof(HeadlessSession).Assembly.Location), root)).ToJsonString()); return; }
    if (command is "validate-registry" or "import-registry")
    {
        byte[] source = File.ReadAllBytes(Text("input", "")); var document = JsonNode.Parse(source)!.AsObject();
        JsonObject inspection = document["registry"] is not null ? document : document["steps"]?.AsArray()
            .Select(s => s?["response"] as JsonObject).FirstOrDefault(s => s?["registry"] is JsonArray)
            ?? throw new InvalidDataException("Expected full headless inspection or integration steps containing one.");
        var audit = CarnivalRegistryAudit.FromInspection(Setup(), inspection); var report = audit.Report();
        report["inspectionSource"] = Path.GetFullPath(Text("input", "")); report["inspectionSourceSha256"] = Convert.ToHexStringLower(SHA256.HashData(source));
        if (command == "import-registry")
        {
            var imported = audit.ImportInitialPositions(); byte[] bytes = imported.ToProto().ToByteArray();
            string path = Path.GetFullPath(Path.Combine(root, Text("path", "imported-carnival34.pb")));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Import path must stay under evidence root.");
            if (File.Exists(path) || File.Exists(path + ".json")) throw new IOException("Import destination already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) output.Write(bytes);
            report["importPath"] = path; report["importSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            report["importScope"] = "Observed initial positions only; original framework prefab/map annotations retained and limitations reported.";
            using var receipt = new StreamWriter(new FileStream(path + ".json", FileMode.CreateNew, FileAccess.Write)); receipt.Write(report.ToJsonString());
        }
        await stdout.WriteLineAsync(report.ToJsonString()); if (report["ok"]!.GetValue<bool>() != true) Environment.ExitCode = 1; return;
    }
    if (command is "serve" or "reconstruct")
    {
        bool discoveryOnly = Has("discover");
        if (discoveryOnly && (Text("level", "carnival34") != "story11" || options.ContainsKey("setup"))) throw new ArgumentException("--discover requires --level story11 without a setup override.");
        string setupSource = options.ContainsKey("setup") ? Path.GetFullPath(options["setup"]) : discoveryOnly ? "Story11 native inspection bootstrap" : Text("level", "carnival34") == "story11" ? "Story11FourLevel: native story11-discovery-b registry/anchors; source-derived walk polygons, collider geometry unverified" : "upstream Carnival34FourLevel (geometry/prefabs validated only by observed registry audit)";
        var setup = Setup();
        int? requestedSeed = options.ContainsKey("seed") ? Int("seed", 0) : null;
        var session = new HeadlessSession(setup, setupSource, root, requestedSeed, Has("realtime"), discoveryOnly, Text("level", "carnival34"));
        if (command == "reconstruct")
        {
            int count = 0, controls = 0, inputMismatches = 0;
            foreach (string line in TraceStore.Expand(TraceStore.ReadLines(Text("input", ""))))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var row = JsonNode.Parse(line)!.AsObject();
                if (row["kind"]?.ToString() == "session")
                {
                    if (count != 0 || controls != 0) throw new InvalidDataException("Session header must precede all exchanges/commands.");
                    byte[] initial = Convert.FromBase64String(row["initialSetupProtobuf"]!.ToString());
                    if (Convert.ToHexStringLower(SHA256.HashData(initial)) != row["initialSetupSha256"]!.ToString()) throw new InvalidDataException("Initial setup hash mismatch.");
                    session = new HeadlessSession(Hpmv.Save.GameSetup.Parser.ParseFrom(initial).FromProto(), row["setupSource"]!.ToString(), root, row["requestedSeed"]?.GetValue<int>(), discoveryOnly: row["discoveryOnly"]?.GetValue<bool>() == true, levelProfile: row["level"]?.ToString() ?? "carnival34");
                    continue;
                }
                if (row["kind"]?.ToString() == "control") { session.Command(row["request"]!.AsObject()); controls++; continue; }
                if (row["kind"]?.ToString() != "exchange") continue;
                var output = row["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
                var actual = await session.getNext(output); count++;
                if (row["input"] is JsonObject expected && !actual.Equals(expected.Deserialize<InputData>(RuntimeHost.Json))) inputMismatches++;
            }
            var inspection = session.Inspect(Has("full")); inspection["reconstructedExchanges"] = count;
            inspection["replayedControlCommands"] = controls; inspection["recordedInputMismatches"] = inputMismatches;
            if (inputMismatches != 0) { inspection["ok"] = false; Environment.ExitCode = 1; }
            await stdout.WriteLineAsync(inspection.ToJsonString()); return;
        }
        int gamePort = Int("game-port", 14455), controlPort = Int("control-port", 17637);
        if (gamePort is < 1 or > 65535 || controlPort is < 1 or > 65535 || gamePort == controlPort) throw new ArgumentException("Ports must be distinct and in1..65535.");
        TraceStore trace = null;
        if (options.TryGetValue("trace", out string tracePath))
        {
            tracePath = Path.GetFullPath(tracePath); Directory.CreateDirectory(Path.GetDirectoryName(tracePath));
            int traceMaximumMiB = Int("trace-max-mib", 1024), traceReserveMiB = Int("trace-reserve-mib", 256);
            if (traceMaximumMiB < 16 || traceReserveMiB < 0) throw new ArgumentException("Trace maximum must be at least16MiB; reserve must be nonnegative.");
            trace = TraceStore.Create(tracePath, traceMaximumMiB * 1024L * 1024, traceReserveMiB * 1024L * 1024);
            byte[] initial = setup.ToProto().ToByteArray();
            trace.Header(new { kind = "session", version = 2, setupSource, requestedSeed, discoveryOnly, level = Text("level", "carnival34"), tracePolicy = trace.Status(),
                initialSetupProtobuf = Convert.ToBase64String(initial), initialSetupSha256 = Convert.ToHexStringLower(SHA256.HashData(initial)),
                hostSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(HeadlessSession).Assembly.Location))) });
            session.AttachTrace(trace);
        }
        using (trace) await RuntimeHost.Serve(session, gamePort, controlPort, cancellation.Token);
        return;
    }
    var request = new JsonObject { ["command"] = command, ["full"] = Has("full"), ["development"] = Has("development") };
    if (command == "actions-clear" && Has("all")) request["all"] = true;
    if (command == "actions")
    {
        var body = JsonNode.Parse(File.ReadAllBytes(Text("input", "")))!.AsObject();
        request["actions"] = body["actions"]?.DeepClone() ?? throw new ArgumentException("Input file requires actions.");
        if (body["maximumFrames"] is not null) request["maximumFrames"] = body["maximumFrames"]!.DeepClone();
    }
    if (command is "raw-input" or "raw-replay")
    {
        var body = JsonNode.Parse(File.ReadAllBytes(Text("input", "")))!.AsObject();
        if (command == "raw-replay") request["recording"] = body;
        else request["segments"] = body["segments"]?.DeepClone() ?? throw new ArgumentException("Input file requires segments.");
    }
    if (options.ContainsKey("frame")) request["frame"] = Int("frame", -1);
    if (options.ContainsKey("frames")) request["frames"] = Int("frames", 1);
    if (options.ContainsKey("path")) request["path"] = options["path"];
    if (options.ContainsKey("chef")) request["chef"] = Int("chef", -1);
    foreach (string axis in new[] { "x", "z" }) if (options.ContainsKey(axis)) request[axis] = float.Parse(options[axis], System.Globalization.CultureInfo.InvariantCulture);
    var result = await RuntimeHost.Send(Int("control-port", 17637), request, cancellation.Token);
    await stdout.WriteLineAsync(result.ToJsonString()); if (result["ok"]?.GetValue<bool>() != true) Environment.ExitCode = 1;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
catch (Exception error) { await stdout.WriteLineAsync(new JsonObject { ["ok"] = false, ["error"] = error.ToString() }.ToJsonString()); Environment.ExitCode = 1; }
