using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // PlayerControls.FixedUpdate derives cosmetic movement speed from the
    // current local position and a retained previous-position sample. Rewind
    // already restores the authoritative Transform/Rigidbody state; this
    // sidecar restores the four private history fields that make the next
    // FixedUpdate observe the same displacement as the original continuation.
    // Ordinary forward play is read-only.
    public sealed class ChefMovementHistoryCheckpointModule : IAuthoringModule
    {
        private sealed class ChefState
        {
            internal PlayerControls Controls;
            internal Transform Transform;
            internal object Movement;
            internal int ControlsId, TransformId;
            internal string Path;
            internal Transform PreviousParent;
            internal Vector3 PreviousPosition, LocalVelocity;
            internal float XzSpeed, RunSpeed;
        }

        private sealed class FrameState
        {
            internal int Frame;
            internal object NativeSnapshot;
            internal ChefState[] Chefs;
        }

        private static ChefMovementHistoryCheckpointModule active;
        private readonly SortedDictionary<int, FrameState> history = new SortedDictionary<int, FrameState>();
        private readonly SortedDictionary<int, FrameState> replayReference = new SortedDictionary<int, FrameState>();
        private readonly List<object> restoreReceipts = new List<object>();
        private Harmony harmony;
        private FieldInfo nativeHistory, planSnapshot;
        private FieldInfo cachedTransform, movement, previousParent, previousPosition, localVelocity, xzSpeed, runSpeed;
        private string sceneIdentity, failure, resumeFailure;
        private int warpTarget = -1;
        private bool warpEligible, pendingResumeRestore, disposed;
        private FrameState resumeFrame;
        private long captures, duplicateCaptures, restores, resumeRestores, replayComparisons;
        private object firstReplayDifference, lastRestore;

        public string Name { get { return "chef-movement-history-checkpoint-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefMovementHistoryCheckpointModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef movement-history operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation == "clear") ClearDiagnostics();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate, clear or status.");
            return Status(operation);
        }

        private void Activate()
        {
            RequireFence();
            if (ReferenceEquals(active, this)) return;
            if (active != null) throw new InvalidOperationException("Another chef movement-history checkpoint is active.");

            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            var prepare = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "Prepare", new[] {typeof(Hpmv.WarpSpec)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            var restoreFailure = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "RecordRestoreFailure", new[] {typeof(int), typeof(Exception), typeof(bool)});
            Type helpers = typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers", true);
            var resume = AccessTools.DeclaredMethod(helpers, "Resume", Type.EmptyTypes);

            nativeHistory = typeof(NativeKitchenCheckpoint).GetField("history", BindingFlags.Static | BindingFlags.NonPublic);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            cachedTransform = AccessTools.Field(typeof(PlayerControls), "m_Transform");
            movement = AccessTools.Field(typeof(PlayerControls), "m_movement");
            previousParent = AccessTools.Field(typeof(PlayerControls), "m_previousParent");
            previousPosition = AccessTools.Field(typeof(PlayerControls), "m_previousPosition");
            localVelocity = AccessTools.Field(typeof(PlayerControls), "m_localVelocity");
            xzSpeed = AccessTools.Field(typeof(PlayerControls), "m_xzSpeed");
            Type movementType = movement == null ? null : movement.FieldType;
            runSpeed = movementType == null ? null : AccessTools.Field(movementType, "RunSpeed");

            if (capture == null || prepare == null || complete == null || restoreFailure == null || resume == null || resume.ReturnType != typeof(void)
                || nativeHistory == null || !typeof(IDictionary).IsAssignableFrom(nativeHistory.FieldType) || planSnapshot == null
                || cachedTransform == null || cachedTransform.FieldType != typeof(Transform)
                || movement == null || previousParent == null || previousParent.FieldType != typeof(Transform)
                || previousPosition == null || previousPosition.FieldType != typeof(Vector3)
                || localVelocity == null || localVelocity.FieldType != typeof(Vector3)
                || xzSpeed == null || xzSpeed.FieldType != typeof(float)
                || runSpeed == null || runSpeed.FieldType != typeof(float))
                throw new InvalidOperationException("Installed PlayerControls/checkpoint lifecycle contract differs.");

            harmony = new Harmony("supercharged.authoring.chef-movement-history-checkpoint." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(capture, postfix: Hook("AfterCaptureFrame"));
                harmony.Patch(prepare, prefix: Hook("BeforePrepare"));
                harmony.Patch(complete, postfix: Hook("AfterRestoreComplete"));
                harmony.Patch(restoreFailure, postfix: Hook("AfterRestoreFailure"));
                harmony.Patch(resume, prefix: Hook("BeforeAuthoringResume"));
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        public static void AfterCaptureFrame(int __0)
        {
            var module = active;
            if (module == null || !NativeSessionBridge.KitchenReady) return;
            try { module.CaptureFrame(__0); if (module.resumeFailure == null) module.failure = null; }
            catch (Exception error) { module.failure = error.ToString(); }
        }

        public static void BeforePrepare(Hpmv.WarpSpec __0)
        {
            var module = active;
            if (module == null) return;
            module.warpTarget = __0 == null ? -1 : __0.Frame;
            module.warpEligible = false;
            module.pendingResumeRestore = false;
            module.resumeFrame = null;
            module.replayReference.Clear();
            module.replayComparisons = 0;
            module.firstReplayDifference = null;

            FrameState target;
            object native = module.NativeSnapshot(module.warpTarget);
            if (native != null && module.history.TryGetValue(module.warpTarget, out target)
                && ReferenceEquals(native, target.NativeSnapshot))
            {
                module.warpEligible = true;
                foreach (var pair in module.history.Where(value => value.Key > module.warpTarget))
                    module.replayReference.Add(pair.Key, pair.Value);
            }
            if (!module.warpEligible)
                throw new InvalidOperationException("No exact chef movement-history checkpoint at output frame " + module.warpTarget + ".");
        }

        public static void AfterRestoreComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module = active;
            if (module == null || !module.warpEligible) return;
            try
            {
                FrameState target = module.history[module.warpTarget];
                if (!ReferenceEquals(module.planSnapshot.GetValue(__instance), target.NativeSnapshot))
                    throw new InvalidOperationException("Completed restore does not own the selected movement-history checkpoint.");
                module.Restore(target, "after-restore-complete");
                foreach (int frame in module.history.Keys.Where(value => value > module.warpTarget).ToArray()) module.history.Remove(frame);
                module.resumeFrame = target;
                module.pendingResumeRestore = true;
                module.restores++;
                module.failure = null;
            }
            catch (Exception error) { module.failure = error.ToString(); throw; }
            finally { module.warpEligible = false; }
        }

        public static void AfterRestoreFailure()
        {
            var module = active;
            if (module == null) return;
            module.warpEligible = false;
            module.warpTarget = -1;
            module.pendingResumeRestore = false;
            module.resumeFrame = null;
        }

        public static void BeforeAuthoringResume()
        {
            var module = active;
            if (module == null || !NativeSessionBridge.KitchenReady || module.warpEligible
                || !module.pendingResumeRestore || module.resumeFrame == null
                || !TimeManager.IsPaused(TimeManager.PauseLayer.Main)) return;
            try
            {
                module.Restore(module.resumeFrame, "before-authoring-resume");
                module.resumeRestores++;
                module.pendingResumeRestore = false;
                module.resumeFrame = null;
                module.resumeFailure = null;
                module.failure = null;
            }
            catch (Exception error) { module.resumeFailure = error.ToString(); module.failure = module.resumeFailure; throw; }
        }

        private void CaptureFrame(int frame)
        {
            if (frame < 0) return;
            object native = NativeSnapshot(frame);
            if (native == null) throw new InvalidOperationException("Native checkpoint is absent for movement-history frame " + frame + ".");
            FrameState duplicate;
            if (history.TryGetValue(frame, out duplicate) && ReferenceEquals(duplicate.NativeSnapshot, native))
            {
                duplicateCaptures++;
                return;
            }
            PlayerControls[] chefs = FindChefs();
            string identity = String.Join("|", chefs.Select(value => value.GetInstanceID().ToString()).ToArray());
            if (sceneIdentity != identity)
            {
                history.Clear();
                replayReference.Clear();
                sceneIdentity = identity;
                warpTarget = -1;
                warpEligible = false;
                pendingResumeRestore = false;
                resumeFrame = null;
            }
            if (history.ContainsKey(frame))
                throw new InvalidOperationException("Output frame " + frame + " identifies a different native checkpoint in the same chef incarnation.");
            var snapshot = new FrameState {Frame = frame, NativeSnapshot = native, Chefs = chefs.Select(Capture).ToArray()};
            FrameState expected;
            if (replayReference.TryGetValue(frame, out expected))
            {
                replayComparisons++;
                if (firstReplayDifference == null) firstReplayDifference = FirstDifference(expected, snapshot);
            }
            if (history.Count != 0 && frame < history.Keys.Last())
                throw new InvalidOperationException("Chef movement-history frame regressed without a completed rewind.");
            history.Add(frame, snapshot);
            captures++;
            if (history.Count > 20000) throw new InvalidOperationException("Chef movement-history checkpoint bound reached.");
        }

        private ChefState Capture(PlayerControls controls)
        {
            var transform = (Transform)cachedTransform.GetValue(controls);
            object movementValue = movement.GetValue(controls);
            if (transform == null || !ReferenceEquals(transform, controls.transform) || movementValue == null)
                throw new InvalidOperationException("PlayerControls cached Transform/movement identity is incomplete.");
            float speed = (float)xzSpeed.GetValue(controls);
            float configuredRunSpeed = (float)runSpeed.GetValue(movementValue);
            if (!Finite(speed) || !Finite(configuredRunSpeed) || configuredRunSpeed <= 0f)
                throw new InvalidOperationException("PlayerControls movement speed configuration is invalid.");
            return new ChefState {
                Controls = controls, Transform = transform, Movement = movementValue,
                ControlsId = controls.GetInstanceID(), TransformId = transform.GetInstanceID(), Path = PathOf(transform),
                PreviousParent = (Transform)previousParent.GetValue(controls),
                PreviousPosition = (Vector3)previousPosition.GetValue(controls),
                LocalVelocity = (Vector3)localVelocity.GetValue(controls),
                XzSpeed = speed, RunSpeed = configuredRunSpeed
            };
        }

        private void Restore(FrameState frame, string phase)
        {
            PlayerControls[] current = FindChefs();
            if (frame.Chefs.Length != 4 || current.Length != 4)
                throw new InvalidOperationException("Movement-history restore requires four local chefs.");
            var rows = new List<object>();
            foreach (ChefState saved in frame.Chefs)
            {
                PlayerControls controls = current.SingleOrDefault(value => value.GetInstanceID() == saved.ControlsId);
                if (controls == null || !ReferenceEquals(controls, saved.Controls)
                    || controls.transform.GetInstanceID() != saved.TransformId || !ReferenceEquals(controls.transform, saved.Transform)
                    || !ReferenceEquals(cachedTransform.GetValue(controls), saved.Transform)
                    || !ReferenceEquals(movement.GetValue(controls), saved.Movement)
                    || (float)runSpeed.GetValue(saved.Movement) != saved.RunSpeed)
                    throw new InvalidOperationException("PlayerControls incarnation or movement configuration changed for " + saved.Path + ".");

                var before = Capture(controls);
                previousParent.SetValue(controls, saved.PreviousParent);
                previousPosition.SetValue(controls, saved.PreviousPosition);
                localVelocity.SetValue(controls, saved.LocalVelocity);
                xzSpeed.SetValue(controls, saved.XzSpeed);
                var after = Capture(controls);
                object difference = Difference(saved, after);
                if (difference != null) throw new InvalidOperationException("PlayerControls movement-history restore verification failed for " + saved.Path + ".");
                rows.Add(new Dictionary<string, object> {
                    {"controlsInstanceId", saved.ControlsId}, {"path", saved.Path},
                    {"beforePreviousPosition", Point(before.PreviousPosition)}, {"savedPreviousPosition", Point(saved.PreviousPosition)},
                    {"beforeLocalVelocity", Point(before.LocalVelocity)}, {"savedLocalVelocity", Point(saved.LocalVelocity)},
                    {"beforeXzSpeed", before.XzSpeed}, {"savedXzSpeed", saved.XzSpeed}, {"runSpeed", saved.RunSpeed},
                    {"previousParentChanged", !ReferenceEquals(before.PreviousParent, saved.PreviousParent)}
                });
            }
            lastRestore = new Dictionary<string, object> { {"phase", phase}, {"frame", frame.Frame}, {"chefCount", 4},
                {"verified", true}, {"chefs", rows.ToArray()} };
            restoreReceipts.Add(lastRestore);
            if (restoreReceipts.Count > 16) restoreReceipts.RemoveAt(0);
        }

        private object FirstDifference(FrameState expected, FrameState actual)
        {
            if (expected.Chefs.Length != actual.Chefs.Length)
                return new Dictionary<string, object> { {"frame", actual.Frame}, {"field", "chefCount"},
                    {"expected", expected.Chefs.Length}, {"actual", actual.Chefs.Length} };
            for (int i = 0; i < expected.Chefs.Length; i++)
            {
                object difference = Difference(expected.Chefs[i], actual.Chefs[i]);
                if (difference != null) return new Dictionary<string, object> { {"frame", actual.Frame},
                    {"chefIndex", i}, {"controlsInstanceId", expected.Chefs[i].ControlsId}, {"difference", difference} };
            }
            return null;
        }

        private static object Difference(ChefState expected, ChefState actual)
        {
            if (!ReferenceEquals(expected.Controls, actual.Controls) || expected.ControlsId != actual.ControlsId)
                return new Dictionary<string, object> { {"field", "controlsIdentity"} };
            if (!ReferenceEquals(expected.Transform, actual.Transform) || expected.TransformId != actual.TransformId)
                return new Dictionary<string, object> { {"field", "transformIdentity"} };
            if (!ReferenceEquals(expected.Movement, actual.Movement) || expected.RunSpeed != actual.RunSpeed)
                return new Dictionary<string, object> { {"field", "movementConfiguration"} };
            if (!ReferenceEquals(expected.PreviousParent, actual.PreviousParent))
                return new Dictionary<string, object> { {"field", "previousParent"} };
            if (expected.PreviousPosition != actual.PreviousPosition)
                return new Dictionary<string, object> { {"field", "previousPosition"}, {"expected", Point(expected.PreviousPosition)}, {"actual", Point(actual.PreviousPosition)} };
            if (expected.LocalVelocity != actual.LocalVelocity)
                return new Dictionary<string, object> { {"field", "localVelocity"}, {"expected", Point(expected.LocalVelocity)}, {"actual", Point(actual.LocalVelocity)} };
            if (expected.XzSpeed != actual.XzSpeed)
                return new Dictionary<string, object> { {"field", "xzSpeed"}, {"expected", expected.XzSpeed}, {"actual", actual.XzSpeed} };
            return null;
        }

        private PlayerControls[] FindChefs()
        {
            var result = UnityEngine.Object.FindObjectsOfType<PlayerControls>()
                .Where(value => value != null && value.GetComponent<ServerChefSynchroniser>() != null
                    && value.GetComponent<ClientOnTheServerChefSynchroniser>() != null)
                .OrderBy(value => value.GetInstanceID()).ToArray();
            if (result.Length != 4) throw new InvalidOperationException("Expected four local PlayerControls, found " + result.Length + ".");
            return result;
        }

        private object NativeSnapshot(int frame)
        {
            if (frame < 0 || nativeHistory == null) return null;
            var values = (IDictionary)nativeHistory.GetValue(null);
            return values != null && values.Contains(frame) ? values[frame] : null;
        }

        private object Status(string operation)
        {
            return new Dictionary<string, object> {
                {"name", Name}, {"apiVersion", 1}, {"operation", operation}, {"active", ReferenceEquals(active, this)},
                {"historyFrames", history.Count}, {"captures", captures}, {"duplicateCaptures", duplicateCaptures},
                {"restores", restores}, {"resumeRestores", resumeRestores}, {"replayComparisons", replayComparisons},
                {"warpTarget", warpTarget}, {"warpEligible", warpEligible}, {"pendingResumeRestore", pendingResumeRestore},
                {"firstReplayDifference", firstReplayDifference}, {"lastRestore", lastRestore},
                {"restoreReceipts", restoreReceipts.ToArray()}, {"failure", failure}, {"resumeFailure", resumeFailure},
                {"scope", "Capture/restore PlayerControls m_previousParent, m_previousPosition, m_localVelocity and m_xzSpeed for four ordinary local chefs. Forward play is read-only; restoration occurs only in an exact authoring rewind and at its final resume fence."}
            };
        }

        private void ClearDiagnostics()
        {
            if (pendingResumeRestore || warpEligible)
                throw new InvalidOperationException("Cannot clear movement-history diagnostics during a restore/resume transaction.");
            restoreReceipts.Clear();
            firstReplayDifference = null;
            lastRestore = null;
            replayComparisons = 0;
            failure = null;
            resumeFailure = null;
        }

        private static void RequireFence()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef movement-history activation requires the native main pause fence.");
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            history.Clear();
            replayReference.Clear();
            pendingResumeRestore = false;
            resumeFrame = null;
            warpEligible = false;
            if (ReferenceEquals(active, this)) active = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate();
            disposed = true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static object Point(Vector3 value) { return new[] {value.x, value.y, value.z}; }
        private static string PathOf(Transform value)
        {
            var names = new List<string>();
            for (Transform item = value; item != null; item = item.parent) names.Add(item.name);
            names.Reverse();
            return String.Join("/", names.ToArray());
        }
    }
}
