using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace SuperchargedPatch
{
    // Native clock algorithms and synchronization execute on every active frame.
    // The source uses the native float capture step and suspends only our own
    // in-kitchen authoring pause. Unity/physics time is never rewritten.
    public class UnrealTimePatch
    {
        private static AuthoringClockState clock;
        private static bool ownsPause;
        public static int AuthoringClockRestores { get; private set; }
        private static AuthoringClockState Observe()
        {
            bool paused = ownsPause && Bridge.NativeSessionBridge.KitchenReady;
            if (clock == null) clock = new AuthoringClockState(Time.frameCount, Time.time, 1f / 60f, paused);
            clock.ObserveFrame(Time.frameCount);
            clock.SetAuthoringPaused(Time.frameCount, paused);
            return clock;
        }
        public static void Update() { Time.captureFramerate = 60; Observe(); }
        public static float LogicalRealtime() { return Observe().Value; }
        public static float CaptureLogicalRealtime() { return LogicalRealtime(); }
        public static AuthoringClockState.Checkpoint CaptureClock() { return Observe().CaptureCheckpoint(); }
        public static void SetAuthoringPause(bool paused)
        {
            Observe();
            ownsPause = paused;
            Observe();
        }
        public static void RestoreClock(AuthoringClockState.Checkpoint saved)
        {
            // The sticky count disqualifies this process from fresh-run scoring.
            Observe().RestoreCheckpoint(saved, Time.frameCount);
            AuthoringClockRestores++;
        }
        public static object Diagnostics()
        {
            var current = Observe();
            return new Dictionary<string, object> {
                { "policy", "native-float-capture-step-with-authoring-pause-suspension" },
                { "value", current.Value }, { "eligibleTicks", current.EligibleTicks }, { "step", current.CaptureStep },
                { "authoringPausedThisFrame", current.AuthoringPausedThisFrame },
                { "authoringPauseRequested", current.AuthoringPauseRequested }, { "restores", AuthoringClockRestores }
            };
        }

        // Preserve pending sync events and captured private delta/offset fields
        // while waiting for the controller at an authoring boundary.
        [HarmonyPatch(typeof(ServerTime), "Update")]
        public static class SuspendServerAuthoringClock
        { public static bool Prefix() { return Observe().ShouldRunNativeClockUpdates; } }
        [HarmonyPatch(typeof(ClientTime), "Update")]
        public static class SuspendClientAuthoringClock
        { public static bool Prefix() { return Observe().ShouldRunNativeClockUpdates; } }
        [HarmonyPatch]
        public static class NativeClockSources
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ClientTime), "Update");
                yield return AccessTools.Method(typeof(ClientTime), "Time");
                yield return AccessTools.Method(typeof(ClientTime), "OnTimeSyncReceived");
                yield return AccessTools.Method(typeof(ServerTime), "Update");
            }
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var original = AccessTools.PropertyGetter(typeof(Time), "realtimeSinceStartup");
                var replacement = AccessTools.Method(typeof(UnrealTimePatch), "LogicalRealtime");
                foreach (var instruction in instructions)
                {
                    if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, original))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                    }
                    yield return instruction;
                }
            }
        }
        [HarmonyPatch]
        public static class WorldObjectRestClock
        {
            public static MethodBase TargetMethod()
            {
                var method = AccessTools.Method(typeof(Team17.Online.Multiplayer.Messaging.ServerWorldObjectSynchroniser),
                    "GetServerUpdate", System.Type.EmptyTypes);
                if (method == null || method.DeclaringType != typeof(Team17.Online.Multiplayer.Messaging.ServerWorldObjectSynchroniser)
                    || method.ReturnType != typeof(Team17.Online.Multiplayer.Messaging.Serialisable))
                    throw new System.InvalidOperationException("ServerWorldObjectSynchroniser.GetServerUpdate contract differs.");
                return method;
            }
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var values = new List<CodeInstruction>(instructions);
                var original = AccessTools.PropertyGetter(typeof(Time), "time");
                var replacement = AccessTools.Method(typeof(UnrealTimePatch), "LogicalRealtime");
                int replaced = 0;
                foreach (var instruction in values)
                {
                    if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                        && Equals(instruction.operand, original))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                        replaced++;
                    }
                }
                // Both reads must share one epoch: the active-send timestamp and
                // the later strict rest-deadline comparison.
                if (original == null || replacement == null || replaced != 2)
                    throw new System.InvalidOperationException("Expected exactly two WorldObject Time.time reads; found " + replaced + ".");
                return values;
            }
        }
        [HarmonyPatch(typeof(TimeManager), "Update")]
        public static class RememberNativeTimeManager
        {
            public static void Prefix(TimeManager __instance) { Helpers.CurrentTimeManager = __instance; }
        }
    }
}
