using System.Text.Json;
using SuperchargedPatch;
using SuperchargedPatch.Authoring.Modules;
using SuperchargedPatch.Bridge;

var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks.Add(name);
}

Dictionary<string, object> Status(PauseOwnerGuardModule module) =>
    (Dictionary<string, object>)module.Invoke("status", new());

var unrelatedA = new object();
var unrelatedB = new object();
var cameraOwner = new object();
var manager = new TimeManager();
TimeManager.Current = manager;
Helpers.CurrentTimeManager = manager;
NativeSessionBridge.InputBlocked = true;
NativeSessionBridge.KitchenReady = true;
UnityEngine.Time.frameCount = 73;
manager.SetPaused(TimeManager.PauseLayer.Main, true, unrelatedA);
for (int i = 0; i < 7; i++) manager.SetPaused(TimeManager.PauseLayer.Main, true, Helpers.StableOwner);
manager.SetPaused(TimeManager.PauseLayer.Main, true, unrelatedB);
manager.SetPaused(TimeManager.PauseLayer.Camera, true, cameraOwner);

var module = new PauseOwnerGuardModule();
module.Invoke("activate", new());
var activated = Status(module);
Check((bool)activated["active"], "guard activates only at the paused bridge fence");
Check((int)activated["initialStableMainOwnerCount"] == 7 &&
      (int)activated["stableMainOwnerCount"] == 1,
    "activation normalizes all duplicate stable Main owners to exactly one");
Check(manager.Owners(TimeManager.PauseLayer.Main).SequenceEqual(
          new[] { unrelatedA, unrelatedB, Helpers.StableOwner }) &&
      manager.Owners(TimeManager.PauseLayer.Camera).SequenceEqual(new[] { cameraOwner }),
    "normalization preserves unrelated Main order and every other pause layer");
Check(TimeManager.IsPaused(TimeManager.PauseLayer.Main) &&
      TimeManager.IsPaused(TimeManager.PauseLayer.Camera),
    "normalization preserves native pause truth");

int authoringTrueBefore = UnrealTimePatch.TrueCalls;
for (int i = 0; i < 11; i++) Helpers.Pause();
var suppressed = Status(module);
Check(manager.StableCount == 1 && (long)suppressed["suppressedPauseCalls"] == 11,
    "repeated framework Pause calls do not grow the native owner list");
Check(UnrealTimePatch.TrueCalls == authoringTrueBefore + 11,
    "suppressed native acquisitions retain every authoring-clock pause notification");

Helpers.Resume();
var released = Status(module);
Check(manager.StableCount == 0 && manager.Owners(TimeManager.PauseLayer.Main).SequenceEqual(
          new[] { unrelatedA, unrelatedB }),
    "unmodified Resume removes every stable owner and preserves unrelated owners");
Check((int)released["stableMainOwnerCount"] == 0 &&
      (int)released["otherMainOwnerCount"] == 2,
    "status distinguishes released framework ownership from unrelated game ownership");
Helpers.Pause();
var reacquired = Status(module);
Check(manager.StableCount == 1 && (long)reacquired["passedThroughPauseCalls"] == 1,
    "the first acquisition after release executes the original native Pause path");

module.Invoke("deactivate", new());
Helpers.Pause();
Check(manager.StableCount == 2,
    "deactivation retires only the owned prefix and restores the original duplicate behavior");
module.Dispose();

var broken = new TimeManager();
TimeManager.Current = broken;
Helpers.CurrentTimeManager = broken;
broken.SetPaused(TimeManager.PauseLayer.Main, true, Helpers.StableOwner);
var fallback = new PauseOwnerGuardModule();
fallback.Invoke("activate", new());
broken.BreakMainOwnersForTest();
Check(PauseOwnerGuardModule.BeforePause(),
    "diagnostic reflection failure falls back to the original Pause implementation");
broken.RestoreMainOwnersForTest(Helpers.StableOwner);
Check(Status(fallback)["failure"] is string,
    "diagnostic failure is retained explicitly instead of silently suppressing Pause");
fallback.Dispose();

var report = new
{
    ok = true,
    checks = checks.Count,
    names = checks,
    scope = "Production PauseOwnerGuard source with deterministic TimeManager/Harmony stubs; live CLR2 activation and native owner-list evidence remain separate."
};
Console.WriteLine(JsonSerializer.Serialize(report));
