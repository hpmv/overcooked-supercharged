using Hpmv;
using SuperchargedPatch;
using SuperchargedPatch.AlteredComponents;
using UnityEngine;
using Random = UnityEngine.Random;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; }
    throw new Exception("FAIL: expected rejection " + name);
}
InputData Input(int id, bool down = false, double x = 0, bool hintedPress = false, bool hintedRelease = false) => new() {
    Input = new() { [id] = new() { Pad = new() { X = x }, Pickup = new() { Down = down, JustPressed = hintedPress, JustReleased = hintedRelease } } }
};
var chef = new GameObject();
var source = new Hardware { Down = true, Axis = .7f };
var device = (TASLogicalButton)TASLogicalButton.GetOrCreate(10, TASLogicalButtonType.Pickup, source, chef);
var axis = new TASLogicalValue(10, TASLogicalValueType.MovementX, source, chef);
Check(device.IsDown() && axis.GetValue() == .7f, "hardware before first accepted emulation");
Check(ReferenceEquals(device, TASLogicalButton.GetOrCreate(10, TASLogicalButtonType.Pickup, source, chef)), "native chef/device cache");
TASLogicalButton.ApplyInputFrame(new InputData { Input = new() });
Check(!device.IsDown() && axis.GetValue() == 0, "empty activated input is neutral");
Time.time = 1;
TASLogicalButton.ApplyInputFrame(Input(10, true, .5));
Check(device.HasUnclaimedPressEvent(), "Down creates native edge without hinted press");
Check(device.JustPressed() && !device.JustPressed(), "native press claims exactly once");
Time.time = 1.25f;
Check(device.GetHeldTimeLength() == .25f, "native held duration advances");
TASLogicalButton.ApplyInputFrame(Input(10, true, .5, true, true));
Check(!device.JustPressed() && !device.JustReleased(), "contradictory edge hints do not manufacture events");
TASLogicalButton.ApplyInputFrame(Input(10, false, 0, true, false));
Check(device.JustReleased() && !device.JustReleased() && !device.JustPressed(), "native release claimed once");
TASLogicalButton.ApplyInputFrame(Input(10, true));
device.ClaimPressEvent();
Check(!device.HasUnclaimedPressEvent(), "explicit native claim");
TASLogicalButton.ApplyInputFrame(new InputData());
Check(!device.IsDown() && axis.GetValue() == 0, "omitted input cannot fall back to hardware");
TASLogicalButton.ApplyInputFrame(Input(11, true));
Check(!device.IsDown(), "omitted chef neutral");
Reject(() => TASLogicalButton.ApplyInputFrame(Input(10, true, double.NaN)), "nonfinite axis");
Check(!device.IsDown(), "invalid frame leaves accepted levels intact");
Reject(() => TASLogicalButton.ApplyInputFrame(Input(10, true, 1.01)), "oversized axis");

bool gateOpen = true;
var gate = new GateLogicalButton(device, () => gateOpen);
TASLogicalButton.ObserveNativeGate(device, gate);
Check(!gate.HasUnclaimedPressEvent(), "native gate first observes neutral level");
TASLogicalButton.ApplyInputFrame(Input(10, true));
Check(gate.JustPressed() && !gate.JustPressed(), "original native gate claims");
gateOpen = false;
Check(!gate.IsDown(), "native control predicate suppresses device");
gateOpen = true;
Application.isFocused = false;
TASLogicalButton.ApplyInputFrame(Input(10, false));
TASLogicalButton.ApplyInputFrame(Input(10, true));
Check(!device.JustPressed() && !gate.JustPressed(), "native focus consumes claims");
Application.isFocused = true;
Check(!device.JustPressed(), "focus recovery cannot invent new press");
TASLogicalButton.ResetAll();
Check(!device.IsDown() && !device.JustPressed() && !device.JustReleased() && axis.GetValue() == 0, "reset consumes claims without hardware fallback");

Time.time = 2;
TASLogicalButton.ApplyInputFrame(Input(10, true, .25));
device.JustPressed(); gate.JustPressed();
Time.time = 3;
var checkpoint = TASLogicalButton.CaptureCheckpoint();
Check(checkpoint.ButtonHistoryCount == 2, "checkpoint includes original native gate");
TASLogicalButton.ApplyInputFrame(Input(10));
device.JustReleased(); gate.JustReleased();
Time.time = 20;
TASLogicalButton.RestoreCheckpoint(checkpoint);
Check(device.IsDown() && axis.GetValue() == .25f, "checkpoint restores accepted levels");
Check(!device.JustPressed() && !gate.JustPressed(), "checkpoint restores both native claims");
Check(device.GetHeldTimeLength() == 1 && gate.GetHeldTimeLength() == 1, "checkpoint rebases held duration without clock mutation");
Check(Time.time == 20, "checkpoint leaves Unity clock unchanged");
TASLogicalButton.ObserveNativeGate(device, new GateLogicalButton(device, () => true));
Reject(() => TASLogicalButton.RestoreCheckpoint(checkpoint), "new native gate invalidates checkpoint");
TASLogicalButton.ResetAll();
Reject(() => TASLogicalButton.RestoreCheckpoint(checkpoint), "fresh generation invalidates checkpoint");
chef.Destroyed = true;
TASLogicalButton.ApplyInputFrame(Input(10, true, .75));
Check(!device.IsDown() && axis.GetValue() == 0, "destroyed chef cannot consume reused native ID");
var replacement = TASLogicalButton.GetOrCreate(10, TASLogicalButtonType.Pickup, source, new GameObject());
Check(!ReferenceEquals(replacement, device), "new chef incarnation gets native history");

var native = new RoundData { m_roundTimer = 270, m_recipes = new RecipeList {
    m_recipes = Enumerable.Range(0, 7).Select(i => new RecipeList.Entry { m_order = new() { m_uID = 100 + i, name = "Recipe" + i }, m_scoreForMeal = 40 + 10 * i }).ToArray()
} };
int[] Baseline(int seed, int count)
{
    var ambient = Random.state;
    try {
        Random.InitState(seed); var instance = native.InitialiseRound();
        return Enumerable.Range(0, count).Select(_ => native.GetNextRecipe(instance)[0].m_order.m_uID).ToArray();
    } finally { Random.state = ambient; }
}
var expected = Baseline(0, 64);
Random.InitState(987); var ambient = Random.state;
WarpableRoundData.ConfigureSeedForNextRound(0);
var wrapper = new WarpableRoundData(native);
var round = (WarpableRoundInstanceData)wrapper.InitialiseRound();
Check(Random.state.Equals(ambient), "initialise restores ambient RNG");
Check(ReferenceEquals(wrapper.m_recipes, native.m_recipes) && wrapper.m_roundTimer == 270, "same native asset and duration");
for (int i = 0; i < 4; i++) wrapper.GetNextRecipe(round);
Check(wrapper.GetDiagnostics(round).nativeRecipes.Select(r => r.recipeId).SequenceEqual(expected.Take(4)), "draws use native weighted path");
var before = wrapper.GetDiagnostics(round); int nativeCalls = Random.DrawCount;
var preview = wrapper.GetAuxMessage(round, 2);
Check(preview.currentIndex == 2 && preview.recipes.Select(r => r.RecipeId).SequenceEqual(expected.Skip(2).Take(10)), "scratch preview starts at actual past checkpoint");
var after = wrapper.GetDiagnostics(round);
Check(Random.DrawCount == nativeCalls + 10, "preview executes native draws on scratch data");
Check(after.nextIndex == before.nextIndex && after.history.SequenceEqual(before.history) && after.cumulativeFrequencies.SequenceEqual(before.cumulativeFrequencies), "preview leaves live count/history/frequencies unchanged");
Check(Random.state.Equals(ambient), "preview restores ambient RNG");
for (int i = 4; i < 16; i++) wrapper.GetNextRecipe(round);
Check(wrapper.GetDiagnostics(round).nativeRecipes.Select(r => r.recipeId).SequenceEqual(expected.Take(16)), "preview cannot consume live future draws");
wrapper.Warp(round, 3);
Check(round.RecipeCount == 3 && round.CumulativeFrequencies.Sum() == 3 && round.nextIndex == 3, "rewind restores native frequencies and count");
nativeCalls = Random.DrawCount;
var repeated = Enumerable.Range(0, 13).Select(_ => wrapper.GetNextRecipe(round)[0].m_order.m_uID).ToArray();
Check(repeated.SequenceEqual(expected.Skip(3).Take(13)) && Random.DrawCount == nativeCalls + 13, "repeated history executes native generator again");
wrapper.Warp(round, 24);
Check(round.RecipeCount == 24 && round.CumulativeFrequencies.Sum() == 24, "forward authoring seek updates exact native state");
Check(wrapper.GetDiagnostics(round).nativeRecipes.Select(r => r.recipeId).SequenceEqual(expected.Take(24)), "forward seek retains native sequence");
Check(wrapper.GetDiagnostics(round).authoringWarpCount == 2, "authoring seeks remain disclosed");
before = wrapper.GetDiagnostics(round);
Random.ThrowNext = true;
Reject(() => wrapper.GetNextRecipe(round), "native exception");
after = wrapper.GetDiagnostics(round);
Check(after.nextIndex == before.nextIndex && after.history.SequenceEqual(before.history) && after.cumulativeFrequencies.SequenceEqual(before.cumulativeFrequencies) && Random.state.Equals(ambient), "draw failure restores native count/frequency/stream and ambient");
Check(wrapper.GetNextRecipe(round)[0].m_order.m_uID == expected[24], "draw after failure keeps expected native stream");
Random.ThrowNext = true;
Reject(() => wrapper.GetAuxMessage(round, 0), "scratch preview failure");
Check(round.RecipeCount == 25 && Random.state.Equals(ambient), "preview failure cannot change live state or ambient");
WarpableRoundData.ConfigureSeedForNextRound(72);
var second = (WarpableRoundInstanceData)wrapper.InitialiseRound();
var secondExpected = Baseline(72, 6);
var secondActual = new List<int>();
for (int i = 0; i < 6; i++) { secondActual.Add(wrapper.GetNextRecipe(second)[0].m_order.m_uID); wrapper.GetNextRecipe(round); }
Check(secondActual.SequenceEqual(secondExpected), "independent rounds cannot consume each other's RNG");
Check(wrapper.GetDiagnostics(round).seed == 0 && wrapper.GetDiagnostics(second).seed == 72, "new seed leaves old instance unchanged");
Check(wrapper.GetDiagnostics(round).nativeRecipes.Select(r => r.recipeId).SequenceEqual(expected.Take(31)), "old native weighted stream survives interleaving");
Reject(() => new WarpableRoundData(native).GetNextRecipe(round), "wrong wrapper ownership");
Reject(() => wrapper.Warp(round, -1), "negative seek");
Check(Random.state.Equals(ambient), "all operations preserve ambient stream");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ok = true, assertions = checks, classification = "CPU source-semantic tests; verbatim native button/RoundData methods with controlled Unity doubles. Not live native RNG, input acceptance, physics or TAS score proof." }));

sealed class Hardware : LogicalButtonBase, ILogicalValue
{
    public bool Down; public float Axis;
    public override bool IsDown() => Down;
    public float GetValue() => Axis;
    public override void GetLogicTreeData(out AcyclicGraph<ILogicalElement, LogicalLinkInfo> graph, out AcyclicGraph<ILogicalElement, LogicalLinkInfo>.Node head) { graph = new(); head = graph.GetNode(this); }
}
