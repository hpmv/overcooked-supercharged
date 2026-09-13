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
    // Unity 2017's dynamic/kinematic transition retains a second local-chef
    // pose which the settled paused Rigidbody.position does not expose. Record
    // the dynamic pre-freeze pose and the settled checkpoint pose from the same
    // authoring pause. After the normal exact public restore, install the former
    // before WarpHandler's unchanged final native pause, then queue the latter
    // immediately after FrozenPhysicsData has made that body kinematic. This
    // reproduces the original lifecycle ordering rather than merely assigning
    // the two public poses while the body is still dynamic.
    public sealed class ChefTwoPoseModule : IAuthoringModule
    {
        private sealed class FreezePose
        {
            internal Rigidbody Body;
            internal int BodyId, UnityFrame;
            internal Vector3 Position;
        }
        private sealed class Pair
        {
            internal Rigidbody Body;
            internal int BodyId;
            internal Vector3 PreFreeze, Current;
        }
        private sealed class Saved
        {
            internal int Frame;
            internal Pair[] Chefs;
        }

        private static ChefTwoPoseModule active;
        private Harmony harmony;
        private readonly Dictionary<int, FreezePose> latest = new Dictionary<int, FreezePose>();
        private readonly Dictionary<int, Saved> savedByFrame = new Dictionary<int, Saved>();
        private readonly Dictionary<int, Pair> pendingFinalFreeze = new Dictionary<int, Pair>();
        private readonly HashSet<int> sealedFrames = new HashSet<int>();
        private readonly List<object> receipts = new List<object>();
        private FieldInfo history, planSnapshot, snapshotFrame;
        private bool disposed;
        private long freezeCalls, captures, restores;
        private string failure;

        public string Name { get { return "local-chef-pre-freeze-pose-checkpoint-v4"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefTwoPoseModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-two-pose operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"freezeCalls",freezeCalls},{"captures",captures},{"restores",restores},
                {"savedSnapshots",savedByFrame.Count},{"savedFrames",savedByFrame.Keys.OrderBy(x=>x).ToArray()},
                {"sealedFrames",sealedFrames.OrderBy(x=>x).ToArray()},
                {"pendingFinalFreezeBodyIds",pendingFinalFreeze.Keys.OrderBy(x=>x).ToArray()},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Exact same-pause local-chef pre-freeze and settled checkpoint poses; after verified restore only, position(pre-freeze) before the unchanged final native pause and MovePosition(settled) after that body becomes kinematic. No advancing-gameplay hook."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef-two-pose activation requires the authoring pause fence.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-two-pose module is active.");
            var frozen = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var constructor = frozen == null ? null : AccessTools.Constructor(frozen, new[] {typeof(Rigidbody), typeof(int)});
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            history = typeof(NativeKitchenCheckpoint).GetField("history", BindingFlags.Static | BindingFlags.NonPublic);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            snapshotFrame = planSnapshot == null ? null : planSnapshot.FieldType.GetField("Frame", BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructor == null || capture == null || complete == null || complete.ReturnType != typeof(void)
                || history == null || !typeof(IDictionary).IsAssignableFrom(history.FieldType) || planSnapshot == null
                || snapshotFrame == null || snapshotFrame.FieldType != typeof(int))
                throw new InvalidOperationException("Frozen-X freeze/checkpoint contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-two-pose." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(constructor, prefix: Hook("BeforeFreeze"), postfix: Hook("AfterFreeze"));
                harmony.Patch(capture, postfix: Hook("AfterCapture"));
                harmony.Patch(complete, postfix: Hook("AfterComplete"));
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        private static bool IsLocalChef(Rigidbody body)
        {
            return body != null && body.GetComponent<ServerChefSynchroniser>() != null
                && body.GetComponent<ClientOnTheServerChefSynchroniser>() != null;
        }

        public static void BeforeFreeze(Rigidbody __0)
        {
            var module = active;
            if (module == null || !NativeSessionBridge.InputBlocked || !IsLocalChef(__0)) return;
            try
            {
                int id = __0.GetInstanceID(); FreezePose old;
                module.latest.TryGetValue(id, out old);
                module.latest[id] = new FreezePose {Body=__0,BodyId=id,UnityFrame=Time.frameCount,Position=__0.position};
                module.freezeCalls++;
                if (module.receipts.Count < 64)
                    module.receipts.Add(new Dictionary<string, object> {
                        {"stage","freeze"},{"unityFrame",Time.frameCount},{"bodyInstanceId",id},
                        {"position",Point(__0.position)},{"previous",old==null?null:Point(old.Position)}
                    });
            }
            catch (Exception error) { module.failure = "Freeze: " + error.Message; throw; }
        }

        public static void AfterFreeze(Rigidbody __0)
        {
            var module = active;
            if (module == null || __0 == null) return;
            Pair chef;
            int id = __0.GetInstanceID();
            if (!module.pendingFinalFreeze.TryGetValue(id, out chef)) return;
            try
            {
                if (!ReferenceEquals(__0, chef.Body) || !IsLocalChef(__0) || !__0.isKinematic
                    || __0.useGravity || __0.position != chef.PreFreeze)
                    throw new InvalidOperationException("Final native freeze did not preserve the captured local-chef pre-freeze pose and mode.");
                var velocity = __0.velocity;
                var angular = __0.angularVelocity;
                __0.MovePosition(chef.Current);
                var observed = __0.position;
                // Unity keeps the exposed kinematic pose at the current
                // pre-freeze position until its queued MovePosition target is
                // consumed. That is the exact native freeze-after state.
                if (observed != chef.PreFreeze || __0.velocity != velocity || __0.angularVelocity != angular
                    || !__0.isKinematic || __0.useGravity)
                    throw new InvalidOperationException("Post-freeze settled-pose queue differs from the native immediate pre-freeze readback or changed another public body field.");
                module.pendingFinalFreeze.Remove(id);
                module.receipts.Add(new Dictionary<string, object> {
                    {"stage","final-freeze-pair"},{"bodyInstanceId",id},
                    {"preFreeze",Point(chef.PreFreeze)},{"current",Point(chef.Current)},
                    {"immediateReadback",Point(observed)}
                });
                if (module.receipts.Count > 48) module.receipts.RemoveAt(0);
            }
            catch (Exception error)
            {
                module.failure = "Final freeze: " + error.Message;
                module.pendingFinalFreeze.Clear();
                throw;
            }
        }

        public static void AfterCapture(int __0)
        {
            var module = active;
            if (module == null) return;
            try { module.Capture(__0); }
            catch (Exception error) { module.failure = "Capture: " + error.Message; }
        }

        private void Capture(int frame)
        {
            if (pendingFinalFreeze.Count != 0)
                throw new InvalidOperationException("Completed authoring pause did not freeze all pending local-chef pose pairs.");
            var native = ((IDictionary)history.GetValue(null))[frame];
            if (native == null) return;
            var rows = latest.Values.Where(x => IsLocalChef(x.Body) && x.Body.GetInstanceID() == x.BodyId)
                .OrderBy(x => x.BodyId).ToArray();
            if (rows.Length != 4 || rows.Select(x=>x.UnityFrame).Distinct().Count() != 1) return;
            var pairs = new List<Pair>();
            foreach (var row in rows)
            {
                pairs.Add(new Pair {Body=row.Body,BodyId=row.BodyId,PreFreeze=row.Position,Current=row.Body.position});
            }
            if (sealedFrames.Contains(frame)) return;
            var value = new Saved {Frame=frame,Chefs=pairs.ToArray()};
            savedByFrame[frame] = value;
            captures++;
            if (savedByFrame.Count > 20000) throw new InvalidOperationException("Two-pose checkpoint bound reached.");
        }

        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module = active;
            if (module == null) return;
            try { module.Restore(__instance); }
            catch (Exception error) { module.failure = "Restore: " + error.Message; throw; }
        }

        private void Restore(NativeKitchenCheckpoint.RestorePlan plan)
        {
            if (pendingFinalFreeze.Count != 0)
                throw new InvalidOperationException("A previous restore still has pending final-freeze pose pairs.");
            Saved target;
            var native = planSnapshot.GetValue(plan);
            int frame = native == null ? -1 : (int)snapshotFrame.GetValue(native);
            if (native == null || !savedByFrame.TryGetValue(frame, out target)
                || target.Chefs.Length != 4 || target.Frame != frame)
                throw new InvalidOperationException("Completed restore lacks four captured local-chef pose pairs.");
            var rows = new List<object>();
            foreach (var chef in target.Chefs)
            {
                var body = chef.Body;
                if (!IsLocalChef(body) || body.GetInstanceID() != chef.BodyId || body.isKinematic)
                    throw new InvalidOperationException("Captured local-chef identity or dynamic restore mode changed.");
                if (body.position != chef.Current)
                    throw new InvalidOperationException("Normal restore did not first establish the captured public chef pose.");
                var velocity=body.velocity; var angular=body.angularVelocity;
                bool gravity=body.useGravity;
                body.position=chef.PreFreeze;
                if (body.velocity != velocity || body.angularVelocity != angular || body.isKinematic || body.useGravity != gravity)
                    throw new InvalidOperationException("Two-pose priming changed a non-pose public body field.");
                var observed=body.position;
                if (observed != chef.PreFreeze)
                    throw new InvalidOperationException("Pre-freeze pose lacks exact public readback before the final native pause.");
                pendingFinalFreeze.Add(chef.BodyId, chef);
                rows.Add(new Dictionary<string, object> {
                    {"stage","before-final-pause"},{"bodyInstanceId",chef.BodyId},
                    {"preFreeze",Point(chef.PreFreeze)},{"current",Point(chef.Current)},
                    {"immediateReadback",Point(observed)}
                });
            }
            restores++;
            receipts.Add(new Dictionary<string, object> { {"frame",target.Frame},{"chefs",rows.ToArray()} });
            if (receipts.Count > 32) receipts.RemoveAt(0);
            foreach (var key in savedByFrame.Keys.Where(x => x > target.Frame).ToArray()) savedByFrame.Remove(key);
            sealedFrames.Add(target.Frame);
            failure=null;
        }

        private static object Point(Vector3 value)
        {
            return new[] {value.x,value.y,value.z};
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony=null; latest.Clear(); savedByFrame.Clear(); pendingFinalFreeze.Clear(); sealedFrames.Clear();
            if (ReferenceEquals(active,this)) active=null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate(); disposed=true;
        }
    }
}
