using Hpmv;
using Supercharged.Headless;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Offline only: replay recorded callbacks, benchmark frozen-host methods and
// write new local profiling evidence. No network, native game or plugin calls.
var stdout = Console.Out; Console.SetOut(Console.Error);
string source = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(f)); }
object Stats(IEnumerable<double> values)
{
    var a = values.Order().ToArray();
    return new { count = a.Length, meanMs = a.Length == 0 ? 0 : a.Average(), medianMs = a.Length == 0 ? 0 : a[a.Length / 2], p95Ms = a.Length == 0 ? 0 : a[(int)((a.Length - 1) * .95)], maxMs = a.Length == 0 ? 0 : a[^1] };
}
object Measure(int count, Action action)
{
    for (int i = 0; i < 10; i++) action();
    var times = new List<double>(); long allocated = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < count; i++) { long start = Stopwatch.GetTimestamp(); action(); times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    return new { timing = Stats(times), allocatedBytesPerCall = (GC.GetAllocatedBytesForCurrentThread() - allocated) / count };
}
var phaseTimes = new Dictionary<string, List<double>>();
HeadlessSession? session = null; OutputData? advancingSample = null, pausedSample = null; InputData? advancingInput = null, pausedInput = null;
int exchanges = 0, controls = 0, mismatches = 0, lastFrame = -1;
long parseStart = Stopwatch.GetTimestamp();
var rows = TraceStore.Expand(TraceStore.ReadLines(source)).Select(x => JsonNode.Parse(x)!.AsObject()).ToList();
double readExpandParseMs = Stopwatch.GetElapsedTime(parseStart).TotalMilliseconds;
foreach (var row in rows)
{
    string? kind = (string?)row["kind"];
    if (kind == "session") { session = new(Hpmv.Save.GameSetup.Parser.ParseFrom(Convert.FromBase64String((string)row["initialSetupProtobuf"]!)).FromProto(), "offline captured source", root, (int?)row["requestedSeed"]); continue; }
    if (kind == "control")
    {
        var request = row["request"]!.AsObject();
        if ((string?)request["command"] == "warp") break;
        // Inspection/checkpoint/export don't affect the emitted control stream;
        // avoid generating unrelated checkpoint/recording artifacts in profiling.
        if ((string?)request["command"] is not ("inspect" or "checkpoint" or "record-input")) { session!.Command(request); controls++; }
        continue;
    }
    if (kind != "exchange") continue;
    var output = row["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
    var expected = row["input"]!.Deserialize<InputData>(RuntimeHost.Json)!;
    long start = Stopwatch.GetTimestamp();
    var input = session!.getNext(output).GetAwaiter().GetResult();
    double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    string phase = output.FrameNumber < 2 ? "startup" : output.LastFramePaused ? "paused" : "advancing";
    if (!phaseTimes.ContainsKey(phase)) phaseTimes[phase] = new(); phaseTimes[phase].Add(milliseconds);
    if (!input.Equals(expected)) mismatches++;
    exchanges++; lastFrame = output.FrameNumber;
    if (phase == "advancing") { advancingSample = output; advancingInput = input; }
    if (phase == "paused" && !input.RequestPause && !input.RequestResume && !input.__isset.warp) { pausedSample = output; pausedInput = input; }
}
if (session is null || advancingSample is null || pausedSample is null) throw new InvalidDataException("Need initialized advancing and settled paused samples.");
var phases = phaseTimes.ToDictionary(p => p.Key, p => Stats(p.Value));
var compact = Measure(80, () => session.Inspect());
var statusMethod = typeof(HeadlessSession).GetMethod("Status");
object? statusPoll = statusMethod is null ? null : Measure(800, () => session.Command(new() { ["command"] = "status" }));
var full = Measure(80, () => session.Inspect(true));
var fullJson = Measure(80, () => session.Inspect(true).ToJsonString());
var serialize = Measure(800, () => JsonSerializer.Serialize(new { kind = "exchange", output = advancingSample, input = advancingInput }, RuntimeHost.Json));
object disk;
string tracePath = Path.Combine(root, "benchmark-exchanges.jsonl");
using (var trace = TraceStore.Create(tracePath, 128 * 1024 * 1024, 0)) disk = Measure(500, () => trace.Exchange(advancingSample, advancingInput!, false));
object memory, paused;
using (var trace = new TraceStore(Stream.Null, 128 * 1024 * 1024)) memory = Measure(500, () => trace.Exchange(advancingSample, advancingInput!, false));
using (var trace = new TraceStore(Stream.Null, 128 * 1024 * 1024)) paused = Measure(800, () => trace.Exchange(pausedSample, pausedInput!, true));
var report = new { ok = true, gameCalls = 0, source, sourceSha256 = Hash(source), host = typeof(HeadlessSession).Assembly.Location,
    hostSha256 = Hash(typeof(HeadlessSession).Assembly.Location), exchanges, controls, lastFrame, recordedInputMismatches = mismatches, readExpandParseMs,
    reconstruction = phases, lightweightStatus = statusPoll, compactInspection = compact, fullInspection = full, fullInspectionWithJson = fullJson,
    advancingSerialize = serialize, advancingTraceToNull = memory, advancingTraceToFileWithReserveQueryAndFlush = disk, pausedTraceToNull = paused,
    advancingSampleBytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "exchange", output = advancingSample, input = advancingInput }, RuntimeHost.Json).Length,
    pausedSampleBytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "exchange", output = pausedSample, input = pausedInput }, RuntimeHost.Json).Length,
    scope = "Offline frozen-host costs on this machine, pre-warp captured prefix; no native plugin/Unity/network timing attribution. Input mismatches must be inspected before using reconstruction costs as a like-for-like replay." };
string text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(root, "report.json"), text); stdout.WriteLine(text);
