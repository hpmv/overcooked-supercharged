using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;

namespace SuperchargedPatch.Authoring.Modules
{
    // The bridge deliberately owns one stable Main-pause arbitration key while
    // an authoring boundary is held. Native TimeManager appends duplicate keys,
    // whereas releasing that key removes every equal entry. Re-acquiring an
    // already-owned key therefore cannot change pause semantics; it only grows
    // the native list and makes every later pause query increasingly expensive.
    public sealed class PauseOwnerGuardModule : IAuthoringModule
    {
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static PauseOwnerGuardModule active;

        private readonly object gate = new object();
        private Harmony harmony;
        private MethodInfo pauseMethod;
        private PropertyInfo currentManagerProperty;
        private FieldInfo stableOwnerField, suppressorsField;
        private object stableOwner;
        private bool disposed;
        private string failure;
        private long suppressedPauseCalls, passedThroughPauseCalls, normalizations;
        private int initialStableMainOwnerCount = -1, activationUnityFrame = -1;

        public string Name { get { return "authoring-pause-owner-guard-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("PauseOwnerGuardModule");
            if (args == null) args = new Dictionary<string, object>();
            if (args.Count != 0) throw new ArgumentException("Pause-owner operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return Status();
        }

        private void Activate()
        {
            if (ReferenceEquals(active, this)) return;
            if (active != null) throw new InvalidOperationException("Another pause-owner guard is active.");
            if (!NativeSessionBridge.InputBlocked || !TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Pause-owner activation requires the bridge's paused input fence.");

            Type helpers = typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers", true);
            pauseMethod = AccessTools.DeclaredMethod(helpers, "Pause", Type.EmptyTypes);
            currentManagerProperty = helpers.GetProperty("CurrentTimeManager", StaticMembers);
            stableOwnerField = helpers.GetField("timeManagerPauseArbitration", StaticMembers);
            suppressorsField = typeof(TimeManager).GetField("m_arbitrationSupressors", InstanceMembers);
            if (pauseMethod == null || pauseMethod.ReturnType != typeof(void) ||
                    currentManagerProperty == null || currentManagerProperty.PropertyType != typeof(TimeManager) ||
                    stableOwnerField == null || stableOwnerField.FieldType != typeof(object) ||
                    suppressorsField == null || suppressorsField.FieldType != typeof(List<object>[]))
                throw new InvalidOperationException("Installed framework/native pause ownership contract differs.");
            stableOwner = stableOwnerField.GetValue(null);
            if (stableOwner == null) throw new InvalidOperationException("Framework stable pause owner is null.");

            TimeManager manager = CurrentManager();
            List<object> main = MainOwners(manager);
            initialStableMainOwnerCount = StableCount(main);
            object[] unrelated = main.Where(owner => !ReferenceEquals(owner, stableOwner)).ToArray();

            // Activation occurs in one fenced main-thread authoring callback,
            // before this stack's other pause observers are installed. RemoveAll
            // plus Add yields the same paused ownership set with one stable key;
            // no PlayerLoop or physics callback can occur between the calls.
            manager.SetPaused(TimeManager.PauseLayer.Main, false, stableOwner);
            manager.SetPaused(TimeManager.PauseLayer.Main, true, stableOwner);
            normalizations++;
            main = MainOwners(manager);
            object[] afterUnrelated = main.Where(owner => !ReferenceEquals(owner, stableOwner)).ToArray();
            if (StableCount(main) != 1 || !ReferenceSequenceEqual(unrelated, afterUnrelated) ||
                    !TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Stable pause-owner normalization changed the arbitration set.");

            harmony = new Harmony("supercharged.authoring.pause-owner-guard." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(pauseMethod,
                    prefix: new HarmonyMethod(GetType().GetMethod("BeforePause", BindingFlags.Public | BindingFlags.Static)));
                activationUnityFrame = UnityEngine.Time.frameCount;
                failure = null;
            }
            catch
            {
                if (harmony != null) harmony.UnpatchSelf();
                harmony = null;
                active = null;
                throw;
            }
        }

        public static bool BeforePause()
        {
            PauseOwnerGuardModule module = active;
            if (module == null) return true;
            try
            {
                lock (module.gate)
                {
                    List<object> main = module.MainOwners(module.CurrentManager());
                    int count = module.StableCount(main);
                    if (count == 0)
                    {
                        module.passedThroughPauseCalls++;
                        return true;
                    }
                    if (count != 1)
                        module.failure = "Framework stable Main-pause owner multiplicity changed to " + count + ".";
                    module.suppressedPauseCalls++;
                }
                // Preserve the second, independently idempotent effect of
                // Helpers.Pause even when its redundant native Add is skipped.
                UnrealTimePatch.SetAuthoringPause(true);
                return false;
            }
            catch (Exception error)
            {
                lock (module.gate) module.failure = error.ToString();
                // Pause correctness wins over leak prevention on any diagnostic
                // contract failure: execute the original helper.
                return true;
            }
        }

        private TimeManager CurrentManager()
        {
            var manager = (TimeManager)currentManagerProperty.GetValue(null, null);
            if (manager == null) throw new InvalidOperationException("Framework TimeManager is unavailable.");
            return manager;
        }

        private List<object> MainOwners(TimeManager manager)
        {
            var layers = (List<object>[])suppressorsField.GetValue(manager);
            int index = (int)TimeManager.PauseLayer.Main;
            if (layers == null || index < 0 || index >= layers.Length || layers[index] == null)
                throw new InvalidOperationException("Native Main-pause owner list is unavailable.");
            return layers[index];
        }

        private int StableCount(IEnumerable<object> owners)
        {
            int count = 0;
            foreach (object owner in owners) if (ReferenceEquals(owner, stableOwner)) count++;
            return count;
        }

        private static bool ReferenceSequenceEqual(object[] left, object[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!ReferenceEquals(left[i], right[i])) return false;
            return true;
        }

        private object Status()
        {
            lock (gate)
            {
                int stableMain = -1, otherMain = -1, nonMain = -1, total = -1;
                try
                {
                    TimeManager manager = CurrentManager();
                    var layers = (List<object>[])suppressorsField.GetValue(manager);
                    List<object> main = MainOwners(manager);
                    stableMain = StableCount(main);
                    otherMain = main.Count - stableMain;
                    nonMain = 0; total = main.Count;
                    for (int i = 0; i < layers.Length; i++)
                    {
                        if (i == (int)TimeManager.PauseLayer.Main || layers[i] == null) continue;
                        nonMain += layers[i].Count; total += layers[i].Count;
                    }
                }
                catch (Exception error) { if (failure == null) failure = error.ToString(); }
                return new Dictionary<string, object> {
                    {"name", Name}, {"apiVersion", 1}, {"active", ReferenceEquals(active, this)},
                    {"failure", failure}, {"activationUnityFrame", activationUnityFrame},
                    {"initialStableMainOwnerCount", initialStableMainOwnerCount},
                    {"stableMainOwnerCount", stableMain}, {"otherMainOwnerCount", otherMain},
                    {"nonMainOwnerCount", nonMain}, {"totalOwnerCount", total},
                    {"normalizations", normalizations}, {"suppressedPauseCalls", suppressedPauseCalls},
                    {"passedThroughPauseCalls", passedThroughPauseCalls},
                    {"scope", "Authoring-only idempotence for the framework's exact stable Main-pause key. Unrelated game owners and every resume/release path are unchanged."}
                };
            }
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            if (ReferenceEquals(active, this)) active = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate();
            disposed = true;
        }
    }
}
