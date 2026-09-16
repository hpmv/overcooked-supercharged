using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Rigidbody.MovePosition's pending target is not returned by Rigidbody.position.
    // Capture the exact target computed by the installed local-chef Movement
    // callsite and reissue it only after restoring the matching native snapshot.
    public sealed class ChefMotionTargetModule : IAuthoringModule
    {
        private sealed class Latest
        {
            internal RigidbodyMotion Motion;
            internal Rigidbody Body;
            internal int MotionId, BodyId, UnityFrame;
            internal Vector3 Target;
        }
        private sealed class Saved
        {
            internal int Frame;
            internal object Snapshot;
            internal Latest[] Chefs;
        }

        private static ChefMotionTargetModule active;
        private Harmony harmony;
        private readonly Dictionary<int, Latest> latest = new Dictionary<int, Latest>();
        private readonly Dictionary<object, Saved> saved = new Dictionary<object, Saved>();
        private readonly List<object> receipts = new List<object>();
        private FieldInfo motionBody, history, planSnapshot;
        private bool disposed;
        private long movementCalls, captures, restores;
        private string failure;

        public string Name { get { return "local-chef-motion-target-checkpoint-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefMotionTargetModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-motion-target operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"movementCalls",movementCalls},{"captures",captures},{"restores",restores},
                {"savedSnapshots",saved.Count},{"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Exact target computed by local chef RigidbodyMotion.Movement(Vector3,float); reissued after the matching completed native restore. No canonical pose, velocity, clock or physics step."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef motion-target activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef motion-target module is active.");
            var movement = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "Movement", new[] {typeof(Vector3), typeof(float)});
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            motionBody = AccessTools.Field(typeof(RigidbodyMotion), "m_rigidbody");
            history = typeof(NativeKitchenCheckpoint).GetField("history", BindingFlags.Static | BindingFlags.NonPublic);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            if (movement == null || movement.ReturnType != typeof(void) || capture == null || complete == null
                || motionBody == null || motionBody.FieldType != typeof(Rigidbody)
                || history == null || !typeof(IDictionary).IsAssignableFrom(history.FieldType) || planSnapshot == null)
                throw new InvalidOperationException("Frozen-X motion/checkpoint contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-motion-target." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(movement, prefix: Hook("BeforeMovement"));
                harmony.Patch(capture, postfix: Hook("AfterCapture"));
                harmony.Patch(complete, postfix: Hook("AfterComplete"));
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        public static void BeforeMovement(RigidbodyMotion __instance, Vector3 __0, float __1)
        {
            var module = active;
            if (module == null || __instance == null) return;
            try
            {
                var body = (Rigidbody)module.motionBody.GetValue(__instance);
                if (body == null || body.GetComponent<ServerChefSynchroniser>() == null
                    || body.GetComponent<ClientOnTheServerChefSynchroniser>() == null) return;
                int id = body.GetInstanceID();
                module.latest[id] = new Latest {Motion=__instance,Body=body,MotionId=__instance.GetInstanceID(),BodyId=id,
                    UnityFrame=Time.frameCount,Target=body.position + __0 * __1};
                module.movementCalls++;
            }
            catch (Exception error) { module.failure = "Movement capture: " + error.Message; throw; }
        }

        public static void AfterCapture(int __0)
        {
            var module = active;
            if (module == null) return;
            try { module.Capture(__0); }
            catch (Exception error) { module.failure = "Checkpoint capture: " + error.Message; }
        }

        private void Capture(int frame)
        {
            var native = ((IDictionary)history.GetValue(null))[frame];
            if (native == null || saved.ContainsKey(native)) return;
            var rows = latest.Values.Where(IsCurrentLocalChef).OrderBy(x=>x.BodyId).ToArray();
            if (rows.Length != 4 || rows.Any(x=>x.UnityFrame != Time.frameCount))
                throw new InvalidOperationException("Checkpoint lacks four same-Unity-frame local chef movement targets.");
            var copy = rows.Select(x=>new Latest {Motion=x.Motion,Body=x.Body,MotionId=x.MotionId,BodyId=x.BodyId,
                UnityFrame=x.UnityFrame,Target=x.Target}).ToArray();
            saved[native] = new Saved {Frame=frame,Snapshot=native,Chefs=copy};
            captures++;
            if (saved.Count > 20000) throw new InvalidOperationException("Motion-target checkpoint bound reached.");
        }

        private static bool IsCurrentLocalChef(Latest row)
        {
            return row != null && row.Motion != null && row.Body != null
                && row.Motion.GetInstanceID() == row.MotionId && row.Body.GetInstanceID() == row.BodyId
                && row.Body.GetComponent<ServerChefSynchroniser>() != null
                && row.Body.GetComponent<ClientOnTheServerChefSynchroniser>() != null;
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
            var native = planSnapshot.GetValue(plan);
            Saved target;
            if (native == null || !saved.TryGetValue(native, out target) || target.Chefs.Length != 4)
                throw new InvalidOperationException("Completed restore lacks exact captured chef motion targets.");
            var rows = new List<object>();
            foreach (var chef in target.Chefs)
            {
                if (!IsCurrentLocalChef(chef) || !ReferenceEquals(motionBody.GetValue(chef.Motion), chef.Body))
                    throw new InvalidOperationException("Captured local chef motion/body identity changed.");
                var before = chef.Body.position;
                var rotation = chef.Body.rotation;
                var velocity = chef.Body.velocity;
                var angular = chef.Body.angularVelocity;
                bool kinematic = chef.Body.isKinematic, gravity = chef.Body.useGravity;
                chef.Body.MovePosition(chef.Target);
                var after = chef.Body.position;
                if (after != before || chef.Body.rotation != rotation || chef.Body.velocity != velocity
                    || chef.Body.angularVelocity != angular || chef.Body.isKinematic != kinematic || chef.Body.useGravity != gravity)
                    throw new InvalidOperationException("Reissuing captured MovePosition target changed a public paused body field.");
                rows.Add(new Dictionary<string, object> {
                    {"bodyInstanceId",chef.BodyId},{"capturedUnityFrame",chef.UnityFrame},
                    {"current",new[]{before.x,before.y,before.z}},{"target",new[]{chef.Target.x,chef.Target.y,chef.Target.z}}
                });
            }
            restores++;
            receipts.Add(new Dictionary<string, object> {{"frame",target.Frame},{"chefs",rows.ToArray()},{"verifiedPublicFieldsUnchanged",true}});
            if (receipts.Count > 32) receipts.RemoveAt(0);
            foreach (var key in saved.Where(x=>x.Value.Frame>target.Frame).Select(x=>x.Key).ToArray()) saved.Remove(key);
            failure = null;
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            latest.Clear(); saved.Clear();
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
