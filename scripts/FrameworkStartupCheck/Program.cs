using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Hpmv;
using Supercharged.Headless;

// File-only regression utility. The assembly under test is supplied at build time;
// it opens no listeners, game connections, or development-warp requests.
var stdout = Console.Out; Console.SetOut(Console.Error);
string source = Path.GetFullPath(args.ElementAtOrDefault(0) ?? "artifacts/framework-migration/native-e/exchange.jsonl");
string reportPath = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/framework-startup-tests.json");
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
void Reject(Action action, string message)
{
    try { action(); }
    catch (InvalidOperationException error) when (error.Message.Contains("finite limit", StringComparison.Ordinal)) { checks++; return; }
    throw new InvalidOperationException(message);
}
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
byte[] initial = Array.Empty<byte>();
var callbacks = new List<OutputData>();
int exchange = 0, loadExchange = 0, startExchange = 0;
ServerMessage? loadMessage = null, startMessage = null;
foreach (string line in File.ReadLines(source))
{
    var row = JsonNode.Parse(line)!.AsObject();
    if (row["kind"]?.ToString() == "session")
    {
        initial = Convert.FromBase64String(row["initialSetupProtobuf"]!.ToString());
        Check(Convert.ToHexStringLower(SHA256.HashData(initial)) == row["initialSetupSha256"]!.ToString(), "Recorded initial setup hash mismatch.");
    }
    if (row["kind"]?.ToString() != "exchange") continue;
    exchange++;
    var output = row["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
    foreach (var message in output.ServerMessages ?? new())
    {
        if (message.Type is 1 or 2) { loadMessage = message.DeepCopy(); loadExchange = exchange; }
        if (message.Type == 11 && message.Message.Length > 0 && (message.Message[0] >> 2) == 18)
        { startMessage = message.DeepCopy(); startExchange = exchange; }
    }
    callbacks.Add(output);
    if (startMessage is not null) break;
}
Check(initial.Length > 0 && loadMessage is not null && startMessage is not null, "Fixture must contain real native load and InLevel.");
Check(loadExchange == 1021 && startExchange == 1451, "Pinned native-e callback boundary changed.");
var schemas = callbacks.SelectMany(o => o.EntityRegistry ?? new()).GroupBy(e => e.EntityId).ToDictionary(g => g.Key, g => g.First());
Check(schemas.Count == 122, "Expected actual 122 original registrations.");
var witnesses = callbacks[1215 - 1].ServerMessages;
Check(witnesses.Any(m => Convert.ToBase64String(m.Message) == "AEQ/") && witnesses.Any(m => Convert.ToBase64String(m.Message) == "D8YAgA=="), "Native preplaced extinguisher pair absent from captured callback.");
Check(callbacks[1211 - 1].EntityRegistry.Count == 0 && callbacks[1211 - 1].ServerMessages.Any(m => m.Type == 4), "Expected native payload before initial schema.");

HeadlessSession Session() => new(Hpmv.Save.GameSetup.Parser.ParseFrom(initial).FromProto(), "captured native-e initial setup", Path.GetDirectoryName(reportPath)!);
object Core(HeadlessSession session) => typeof(HeadlessSession).GetField("core", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
RealGameSimulator Simulator(HeadlessSession session) => (RealGameSimulator)Core(session).GetType().GetField("simulator")!.GetValue(Core(session))!;
int Buffered(HeadlessSession session) => ((System.Collections.ICollection)Core(session).GetType().GetField("startupObservations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Core(session))!).Count;
int Count(HeadlessSession session, string name) => Simulator(session).Stats.messageTypeStats.GetValueOrDefault(name);
OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), EntityRegistry = new(), Chefs = new(), Items = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
void Send(HeadlessSession session, OutputData output) => session.getNext(output).GetAwaiter().GetResult();
ServerMessage Physical(int parent, int entity = 1)
{
    // Native observed entity1/component1 header followed by10-bit parent ID.
    int bits = (entity << 14) | (1 << 10) | parent;
    return new() { Type = 4, Message = new byte[] { (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits } };
}
bool Attached(HeadlessSession session, int parent) => Simulator(session).entityIdToRecord[1].data[0].attachmentParent?.path.ids[0] == parent;
var native = Session();
for (int i = 0; i < callbacks.Count - 1; i++) Send(native, callbacks[i]);
Check(native.Inspect()["state"]!.ToString() == "AwaitingStart" && Simulator(native).Frame == 0, "Load callbacks must not advance simulated frames.");
Check(Buffered(native) == startExchange - loadExchange, "Every callback from latest load retained once.");
Check(!Attached(native, 63), "Native startup observations must remain deferred before replay.");
Send(native, callbacks[^1]);
Check(Simulator(native).Frame == 0 && native.Inspect()["lastEmpiricalFrame"]!.GetValue<int>() == 0, "Native replay must not advance empirical or logical time.");
Check(Buffered(native) == 0, "Startup buffer must be drained once at InLevel.");
Check(Attached(native, 63) && Simulator(native).entityIdToRecord[63].data[0].attachment?.path.ids[0] == 1, "Observed physical/attach station pair reconstructed in both directions.");
Check(Simulator(native).entityIdToRecord[2].data[0].attachmentParent?.path.ids[0] == 17, "Original pot home is reconstructed from pre-InLevel messages.");
Check(Simulator(native).entityIdToRecord[10].data[0].attachmentParent?.path.ids[0] == 38, "Original plate home is reconstructed from pre-InLevel messages.");
var earlyItem = callbacks[1211 - 1].Items.Keys.First();
var latestItem = callbacks.Select(o => o.Items.GetValueOrDefault(earlyItem)).Last(i => i is not null && i.__isset.pos)!;
var nativePosition = Simulator(native).entityIdToRecord[earlyItem].position[0];
Check(nativePosition.X == (float)latestItem.Pos.X && nativePosition.Y == (float)latestItem.Pos.Y && nativePosition.Z == (float)latestItem.Pos.Z, "Pre-InLevel item observations retain exact latest native position.");
int physicalPackets = callbacks.Skip(loadExchange - 1).SelectMany(o => o.ServerMessages).Count(m =>
{
    if (m.Type != 4 || m.Message.Length < 2) return false;
    int id = (m.Message[0] << 2) | (m.Message[1] >> 6), component = (m.Message[1] >> 2) & 15;
    return schemas.TryGetValue(id, out var schema) && component < schema.SyncEntityTypes.Count && schema.SyncEntityTypes[component] == 11;
});
Check(physicalPackets > 0 && Count(native, "PhysicalAttachMessage") == physicalPackets, "Every observed physical attachment is applied exactly once.");
var nativeInitial = native.Inspect(true);
var paused = Frame(); paused.LastFramePaused = paused.NextFramePaused = true;
Send(native, paused); Send(native, paused);
Check(Count(native, "PhysicalAttachMessage") == physicalPackets && Simulator(native).Frame == 0, "Repeated paused callbacks cannot replay startup observations or add time.");

// Schema delivered on InLevel: raw messages before registry still decode.
var delayed = Session(); Send(delayed, Frame(loadMessage!.DeepCopy()));
var original = Frame(Physical(63)); original.Items[1] = new() { Pos = new() { X = 11, Y = 2, Z = -3 } };
Send(delayed, original); original.ServerMessages[0] = Physical(0); original.Items[1].Pos.X = 99;
var delayedStart = Frame(startMessage!.DeepCopy()); delayedStart.EntityRegistry = schemas.Values.Select(e => e.DeepCopy()).ToList();
Send(delayed, delayedStart);
Check(Attached(delayed, 63) && Simulator(delayed).entityIdToRecord[1].position[0].X == 11, "Retained callback owns mutable input bytes and item DTOs.");
Check(Count(delayed, "PhysicalAttachMessage") == 1 && Simulator(delayed).Frame == 0, "First InLevel schema decodes earlier raw observation exactly once.");

// Chronology: an observed later detach must win over an earlier attach.
var chronology = Session(); Send(chronology, Frame(loadMessage.DeepCopy()));
Send(chronology, Frame(Physical(63))); Send(chronology, Frame(Physical(0))); Send(chronology, delayedStart.DeepCopy());
Check(!Attached(chronology, 63) && Simulator(chronology).entityIdToRecord[1].data[0].attachmentParent is null, "Replay preserves native attach then detach ordering.");
Check(Count(chronology, "PhysicalAttachMessage") == 2, "Chronological callbacks applied once each.");

// A fresh native load clears queued old-round payloads before any new registry.
var restart = Session(); Send(restart, Frame(loadMessage.DeepCopy())); Send(restart, Frame(Physical(63)));
Send(restart, Frame(loadMessage.DeepCopy())); Send(restart, delayedStart.DeepCopy());
Check(!Attached(restart, 63) && Count(restart, "PhysicalAttachMessage") == 0, "Restart discards pending prior-round observations.");
Check(Buffered(restart) == 0 && Simulator(restart).Frame == 0, "Restart preserves exactly one new frame-zero replay.");

// Same packet reset/schema/start, with old-epoch data preceding the load.
var joined = Session(); var joinedStart = Frame(Physical(0), loadMessage.DeepCopy(), Physical(63), startMessage.DeepCopy());
joinedStart.EntityRegistry = schemas.Values.Select(e => e.DeepCopy()).ToList(); Send(joined, joinedStart);
Check(Attached(joined, 63) && Count(joined, "PhysicalAttachMessage") == 1, "Reset precedes same-packet registry; pre-load messages are discarded.");
Check(Simulator(joined).Frame == 0 && Buffered(joined) == 0, "Same-packet start callback is not double-applied.");

// Native same-packet load without InLevel must retain that registry through later start.
var joinedDelayed = Session(); var joinedLoad = Frame(loadMessage.DeepCopy(), Physical(63));
joinedLoad.EntityRegistry = schemas.Values.Select(e => e.DeepCopy()).ToList(); Send(joinedDelayed, joinedLoad);
Send(joinedDelayed, Frame(startMessage.DeepCopy()));
Check(Attached(joinedDelayed, 63), "Same load/registry packet survives later frame-zero replay.");

// The upstream setup stores some attachment defaults at time -1. An actual
// frame-zero native reassignment must supersede that default, not be shadowed.
var reassigned = Session(); Send(reassigned, Frame(loadMessage.DeepCopy()));
Send(reassigned, Frame(Physical(63, 10))); Send(reassigned, delayedStart.DeepCopy());
Check(Simulator(reassigned).entityIdToRecord[10].data[0].attachmentParent?.path.ids[0] == 63,
    "Native frame-zero plate reassignment must supersede setup's negative-frame attachment.");
Check(Simulator(reassigned).entityIdToRecord[38].data[0].attachment is null,
    "Native frame-zero reassignment clears the original setup parent.");
var versioned = new Versioned<string>("constructor"); versioned.ChangeTo("setup", -1); versioned.ChangeTo("native", 0);
Check(versioned[-2] == "constructor" && versioned[-1] == "setup" && versioned[0] == "native" && versioned.Last() == "native",
    "Frame-zero observation preserves chronological constructor and negative setup history.");
versioned.ChangeTo("native-later", 0);
Check(versioned.changes.Count == 2 && versioned[0] == "native-later", "Same-frame observations replace once without duplicate history entries.");
versioned.ChangeTo("next", 1); versioned.RemoveAllAfter(0);
Check(versioned[1] == "native-later" && versioned[-1] == "setup", "History truncation preserves the native zero baseline and negative setup.");
var initialOnly = new Versioned<string>("constructor"); initialOnly.ChangeTo("native", 0);
Check(initialOnly.changes.Count == 0 && initialOnly.initialValue == "native", "Empty history retains original frame-zero initialValue behavior.");

// Buffer limits fail explicitly instead of silently dropping state or accumulating forever.
var bounded = Session(); Send(bounded, Frame(loadMessage.DeepCopy()));
var core = Core(bounded); core.GetType().GetField("startupEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(core, 262144);
Reject(() => Send(bounded, Frame(Physical(63))), "Finite startup record budget must reject overflow.");
var report = new JsonObject
{
    ["ok"] = true, ["checks"] = checks, ["gameCalls"] = 0,
    ["source"] = source, ["sourceSha256"] = Hash(source), ["loadExchange"] = loadExchange, ["inLevelExchange"] = startExchange,
    ["bufferedCallbacks"] = startExchange - loadExchange, ["physicalAttachPacketsAppliedOnce"] = physicalPackets,
    ["testedAssembly"] = typeof(HeadlessSession).Assembly.Location, ["testedAssemblySha256"] = Hash(typeof(HeadlessSession).Assembly.Location),
    ["connectorSourceSha256"] = Hash("framework/controller/Data/RealGameConnector.cs"),
    ["versionedSourceSha256"] = Hash("framework/controller/Data/Versioned.cs"),
    ["qualification"] = "File-only replay of actual native-e startup and targeted synthetic ordering/reset/bounds fixtures. No new native run, warp, scoring or full-state equivalence claim.",
    ["frameZeroEntities"] = new JsonArray(nativeInitial["entities"]!.AsArray().Where(e => new[] { 1, 2, 10, 63 }.Contains(e!["id"]!.GetValue<int>())).Select(e => e!.DeepClone()).ToArray())
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
stdout.WriteLine(new JsonObject { ["ok"] = true, ["checks"] = checks, ["report"] = reportPath, ["physicalAttachPackets"] = physicalPackets }.ToJsonString());
