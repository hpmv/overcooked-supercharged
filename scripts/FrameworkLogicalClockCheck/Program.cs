using System.Security.Cryptography;
using System.Text.Json;
using SuperchargedPatch;

int checks = 0;
void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
void Reject(Action action, string reason)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    catch (InvalidOperationException) { checks++; return; }
    throw new Exception(reason);
}
int Bits(float x) => BitConverter.SingleToInt32Bits(x);
string Hash(IEnumerable<float> values) => Convert.ToHexString(SHA256.HashData(values.SelectMany(BitConverter.GetBytes).ToArray())).ToLowerInvariant();
const float step = 1f / 60f;

var clock = new AuthoringClockState(10, 24.75f, step, false);
Check(clock.EligibleTicks == 0 && Bits(clock.Value) == Bits(24.75f), "Seeding an observed frame must not add time.");
float first = clock.ObserveFrame(11);
Check(clock.EligibleTicks == 1 && first > 24.75f, "An active native frame advances exactly once.");
for (int i = 0; i < 100; i++) clock.ObserveFrame(11);
Check(clock.EligibleTicks == 1 && Bits(clock.Value) == Bits(first), "Repeated getters and physics calls must not add ticks.");
clock.SetAuthoringPaused(11, true);
Check(clock.ShouldRunNativeClockUpdates && Bits(clock.ObserveFrame(11)) == Bits(first), "LateUpdate pause cannot relabel the already-active frame.");
clock.ObserveFrame(12);
Check(!clock.ShouldRunNativeClockUpdates && clock.EligibleTicks == 1, "Authoring pause takes effect at the next frame entry.");
clock.SetAuthoringPaused(12, false);
Check(!clock.ShouldRunNativeClockUpdates && Bits(clock.Value) == Bits(first), "LateUpdate resume cannot advance its already-paused frame.");
clock.ObserveFrame(13);
Check(clock.ShouldRunNativeClockUpdates && clock.EligibleTicks == 2, "The first resumed native frame advances once.");

var setterFirst = new AuthoringClockState(0, 0, step, false);
setterFirst.SetAuthoringPaused(1, true); // No getter preceded this late control callback.
Check(setterFirst.EligibleTicks == 1 && setterFirst.ShouldRunNativeClockUpdates, "Pause setter must latch the elapsed frame before changing next-frame ownership.");
setterFirst.SetAuthoringPaused(2, false);
Check(setterFirst.EligibleTicks == 1 && !setterFirst.ShouldRunNativeClockUpdates, "Resume setter cannot count an unobserved paused frame as active.");
setterFirst.ObserveFrame(3);
Check(setterFirst.EligibleTicks == 2, "Getter order does not change the active tick count.");

var nativeMenu = new AuthoringClockState(0, 0, step, false);
var nativeMenuFields = new NativeClockModel(0, 0.2f);
for (long frame = 1; frame <= 60; frame++)
{
    // A native menu/round pause does not call SetAuthoringPaused. Only the game
    // gameplay layer is paused; vanilla global clocks continue to be serviced.
    bool nativeMenuPaused = true;
    float source = nativeMenu.ObserveFrame(frame);
    if (nativeMenu.ShouldRunNativeClockUpdates) nativeMenuFields.Update(source, frame);
    if (!nativeMenuPaused) throw new Exception("Fixture must retain native-only pause.");
}
Check(nativeMenu.EligibleTicks == 60 && nativeMenuFields.Events.Count != 0, "Native/menu-only pause must leave global clocks and their native sync cadence running.");
nativeMenu.SetAuthoringPaused(60, true);
var frozenFields = nativeMenuFields.Snapshot();
for (long frame = 61; frame <= 67; frame++)
{
    var source = nativeMenu.ObserveFrame(frame);
    if (nativeMenu.ShouldRunNativeClockUpdates) nativeMenuFields.Update(source, frame);
}
Check(nativeMenuFields.Snapshot().SequenceEqual(frozenFields), "When both pauses exist, explicit authoring suspension preserves private native clock fields.");
nativeMenu.SetAuthoringPaused(67, false);
nativeMenuFields.Update(nativeMenu.ObserveFrame(68), 68);
Check(nativeMenu.EligibleTicks == 61 && !nativeMenuFields.Snapshot().SequenceEqual(frozenFields), "Removing authoring ownership resumes global clocks even if native menu pause remains.");

var sequences = new List<object>();
foreach (float anchor in new[] { 0f, 27.95f, 31.999998f, 127.99999f, 65535.996f, 1048575.9375f })
{
    var c = new AuthoringClockState(0, anchor, step, false);
    long f = 0;
    for (int i = 0; i < 179; i++) c.ObserveFrame(++f);
    var saved = c.CaptureCheckpoint();
    var beforeTicks = c.EligibleTicks;
    var expected = new List<float>();
    for (int i = 0; i < 240; i++) expected.Add(c.ObserveFrame(++f));
    Check(saved.EligibleTicks == beforeTicks && Bits(saved.Value) == Bits((float)(saved.Anchor + saved.EligibleTicks * (double)saved.Step)), "Checkpoint is immutable and retains pre-quantization state.");
    foreach (int polls in new[] { 1, 7, 1000 })
    {
        c.SetAuthoringPaused(f, true);
        c.ObserveFrame(++f);
        c.SetAuthoringPaused(f, false); // Temporary native resume inside WarpHandler.
        Check(!c.ShouldRunNativeClockUpdates, "Temporary warp resume cannot enable clocks in the cached paused frame.");
        c.RestoreCheckpoint(saved, f);
        Check(Bits(c.Value) == Bits(saved.Value) && c.EligibleTicks == saved.EligibleTicks && c.AuthoringPauseRequested, "Restore rebases the current paused cache and holds the exact checkpoint.");
        for (int p = 0; p < polls; p++)
        {
            c.ObserveFrame(++f);
            c.ObserveFrame(f);
        }
        Check(Bits(c.Value) == Bits(saved.Value) && !c.ShouldRunNativeClockUpdates, "Paused poll count cannot change the source or native-update eligibility.");
        c.SetAuthoringPaused(f, false);
        var replay = new List<float>();
        for (int i = 0; i < expected.Count; i++) replay.Add(c.ObserveFrame(++f));
        Check(expected.Select(Bits).SequenceEqual(replay.Select(Bits)), "Restoring at float-rounding boundaries must replay the complete active source sequence exactly.");
        sequences.Add(new { anchor, pausedPolls = polls, activeFrames = replay.Count, sha256 = Hash(replay) });
    }
    Check(c.RestoreCount == 3, "Restore diagnostics remain sticky across branch rewinds.");
}

// Freeze the server/client private fields with the source during our pause.
// This is an executable transcription of their shown float arithmetic, not a
// substitute for a native parity probe after integration.
List<(long Frame, int Bits)> ClockEvents(int polls)
{
    var c = new AuthoringClockState(0, 27f, step, true);
    var native = new NativeClockModel(27f, 29f);
    for (long f = 1; f <= polls; f++)
    {
        float source = c.ObserveFrame(f);
        if (c.ShouldRunNativeClockUpdates) native.Update(source, f);
    }
    c.SetAuthoringPaused(polls, false);
    for (long active = 1; active <= 400; active++)
        native.Update(c.ObserveFrame(polls + active), active);
    return native.Events;
}
var syncEvents = ClockEvents(1);
Check(syncEvents.Count >= 2 && syncEvents.SequenceEqual(ClockEvents(7)) && syncEvents.SequenceEqual(ClockEvents(1000)), "Native strict > threshold and payload sequence must be invariant to 1/7/1000 authoring polls.");
var strict = new NativeClockModel(0, 1);
strict.Update(1, 1);
Check(strict.Events.Count == 0, "Exactly equal native sync deadline must not be changed to >=.");
strict.Update(BitConverter.Int32BitsToSingle(Bits(1f) + 1), 2);
Check(strict.Events.Count == 1, "The next representable source value triggers the unmodified strict native threshold.");

// Demonstrate the old float-only restore losing phase; the new helper must not
// use that re-anchoring even when its immediate restored float is identical.
var rounding = new AuthoringClockState(0, 27.95f, step, false);
for (int f = 1; f <= 179; f++) rounding.ObserveFrame(f);
var precise = rounding.CaptureCheckpoint();
int firstFloatOnlyDivergence = -1;
for (int i = 1; i <= 1000; i++)
{
    float expected = rounding.ObserveFrame(179 + i);
    float floatOnly = (float)((double)precise.Value + i * (double)step);
    if (Bits(expected) != Bits(floatOnly)) { firstFloatOnlyDivergence = i; break; }
}
Check(firstFloatOnlyDivergence > 0, "Captured fixture must expose why a float-only checkpoint is insufficient.");
Check(Bits((float)(1485 * (double)step)) != Bits((float)(1485 / 60.0)), "The native float capture step is observably different from a new exact-rational policy.");

var negativeZero = new AuthoringClockState(0, BitConverter.Int32BitsToSingle(int.MinValue), step, true);
negativeZero.RestoreCheckpoint(negativeZero.CaptureCheckpoint(), 0);
Check(Bits(negativeZero.Value) == int.MinValue, "Even a seed sign bit must survive an exact paused restore.");
Reject(() => new AuthoringClockState(0, float.NaN, step, false), "Reject NaN seed.");
Reject(() => new AuthoringClockState(0, float.PositiveInfinity, step, false), "Reject infinite seed.");
Reject(() => new AuthoringClockState(0, 0, 0, false), "Reject zero step.");
Reject(() => new AuthoringClockState(0, 0, -step, false), "Reject negative step.");
Reject(() => new AuthoringClockState(0, 0, float.NaN, false), "Reject NaN step.");
Reject(() => new AuthoringClockState(-1, 0, step, false), "Reject unknown frame identity.");
var invalid = new AuthoringClockState(4, 0, step, false);
var invalidCheckpoint = invalid.CaptureCheckpoint();
Reject(() => invalid.ObserveFrame(6), "Reject skipped native frame; no guessed pause history.");
Reject(() => invalid.ObserveFrame(3), "Reject backward native frame.");
Check(invalid.Frame == 4 && invalid.EligibleTicks == 0, "Rejected frame identity must not mutate time.");
Reject(() => invalid.RestoreCheckpoint(invalidCheckpoint, 4), "Reject restore during an active native frame.");
invalid.SetAuthoringPaused(4, true);
invalid.ObserveFrame(5);
Reject(() => invalid.RestoreCheckpoint(null, 5), "Reject missing exact checkpoint.");
Reject(() => invalid.RestoreCheckpoint(clock.CaptureCheckpoint(), 5), "Reject checkpoint from another session.");
Reject(() => invalid.RestoreCheckpoint(invalidCheckpoint, 4), "Reject stale current frame identity.");
Check(invalid.Frame == 5 && invalid.EligibleTicks == 0 && invalid.RestoreCount == 0, "Rejected restore must be side-effect free.");

var report = new
{
    passed = true, assertions = checks,
    classification = "Pure state-machine and native-arithmetic-model checks; no plugin integration or native execution",
    policy = "authoring pause only; frame entry latch; native float capture step multiplied in double; exact anchor/tick checkpoint",
    captureStep = step, firstFloatOnlyDivergence, sequences,
    syncEvents = syncEvents.Select(x => new { activeFrame = x.Frame, floatBits = x.Bits }).ToArray()
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
    File.WriteAllText(args[0], json + Environment.NewLine);
}
Console.WriteLine($"PASS {checks} assertions; float-only restore first differs after {firstFloatOnlyDivergence} active frames.");

sealed class NativeClockModel
{
    float serverTime, lastTime, nextSyncTime, localRunningTime, localTimeLastFrame, delta;
    public readonly List<(long Frame, int Bits)> Events = new();
    public NativeClockModel(float now, float next) { serverTime = lastTime = localRunningTime = localTimeLastFrame = now; nextSyncTime = next; }
    public void Update(float source, long activeFrame)
    {
        // Native ServerTime.Update's m_fLastTime assignment is intentionally
        // retained verbatim, even though naming it suggests another expression.
        float elapsed = source - lastTime;
        serverTime += elapsed;
        lastTime = serverTime;
        if (serverTime > nextSyncTime)
        {
            nextSyncTime = 3f + source;
            Events.Add((activeFrame, BitConverter.SingleToInt32Bits(serverTime)));
        }
        delta = source - localTimeLastFrame;
        localRunningTime += delta;
        localTimeLastFrame = source;
    }
    public int[] Snapshot() => new[] { serverTime, lastTime, nextSyncTime, localRunningTime, localTimeLastFrame, delta }.Select(BitConverter.SingleToInt32Bits).ToArray();
}
