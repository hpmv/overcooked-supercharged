using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only observation of the exact native freeze envelope used by the
    // game. This deliberately records both Rigidbody and Transform poses on
    // each side of construction/unfreeze; it never writes a Unity object.
    public sealed class ChefFreezeObserverModule : IAuthoringModule
    {
        private static ChefFreezeObserverModule active;
        private Harmony harmony;
        private FieldInfo frozenBody;
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long transitions, restoreBoundaries;
        private string failure;

        public string Name { get { return "local-chef-freeze-observer-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefFreezeObserverModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-freeze observer operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation == "clear") { receipts.Clear(); transitions = 0; restoreBoundaries = 0; failure = null; }
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate, clear or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"transitions",transitions},{"restoreBoundaries",restoreBoundaries},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Read-only local-chef Rigidbody/Transform observations immediately before and after native FrozenPhysicsData construction and Unfreeze."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef-freeze observer activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-freeze observer is active.");
            var frozen = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var constructor = frozen == null ? null : AccessTools.Constructor(frozen, new[] {typeof(Rigidbody), typeof(int)});
            var unfreeze = frozen == null ? null : AccessTools.DeclaredMethod(frozen, "Unfreeze", Type.EmptyTypes);
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            frozenBody = frozen == null ? null : frozen.GetField("m_frozenBody", BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructor == null || unfreeze == null || unfreeze.ReturnType != typeof(void)
                || complete == null || complete.ReturnType != typeof(void)
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Frozen-X freeze/unfreeze observation contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-freeze-observer." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(constructor, prefix: Hook("BeforeFreeze"), postfix: Hook("AfterFreeze"));
                harmony.Patch(unfreeze, prefix: Hook("BeforeUnfreeze"), postfix: Hook("AfterUnfreeze"));
                harmony.Patch(complete, postfix: Hook("AfterRestoreComplete"));
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

        private static object Observe(string stage, Rigidbody body, int layer)
        {
            var p = body.position; var tp = body.transform.position;
            var r = body.rotation; var tr = body.transform.rotation;
            var v = body.velocity; var a = body.angularVelocity;
            return new Dictionary<string, object> {
                {"stage",stage},{"unityFrame",Time.frameCount},{"fixedTime",Time.fixedTime},{"layer",layer},
                {"bodyInstanceId",body.GetInstanceID()},{"entityName",body.name},
                {"position",new[]{p.x,p.y,p.z}},{"transformPosition",new[]{tp.x,tp.y,tp.z}},
                {"rotation",new[]{r.x,r.y,r.z,r.w}},{"transformRotation",new[]{tr.x,tr.y,tr.z,tr.w}},
                {"velocity",new[]{v.x,v.y,v.z}},{"angularVelocity",new[]{a.x,a.y,a.z}},
                {"isKinematic",body.isKinematic},{"useGravity",body.useGravity},{"sleeping",body.IsSleeping()}
            };
        }

        private void Add(object receipt)
        {
            receipts.Add(receipt); transitions++;
            if (receipts.Count > 256) receipts.RemoveAt(0);
        }

        public static void BeforeFreeze(Rigidbody __0, int __1)
        {
            var module = active;
            if (module == null || !IsLocalChef(__0)) return;
            try { module.Add(Observe("freeze-before", __0, __1)); }
            catch (Exception error) { module.failure = error.Message; throw; }
        }

        public static void AfterFreeze(Rigidbody __0, int __1)
        {
            var module = active;
            if (module == null || !IsLocalChef(__0)) return;
            try { module.Add(Observe("freeze-after", __0, __1)); }
            catch (Exception error) { module.failure = error.Message; throw; }
        }

        public static void BeforeUnfreeze(object __instance, out Rigidbody __state)
        {
            __state = null;
            var module = active;
            if (module == null || __instance == null) return;
            try
            {
                var body = (Rigidbody)module.frozenBody.GetValue(__instance);
                if (!IsLocalChef(body)) return;
                __state = body;
                module.Add(Observe("unfreeze-before", body, -1));
            }
            catch (Exception error) { module.failure = error.Message; throw; }
        }

        public static void AfterUnfreeze(Rigidbody __state)
        {
            var module = active;
            if (module == null || __state == null) return;
            try { module.Add(Observe("unfreeze-after", __state, -1)); }
            catch (Exception error) { module.failure = error.Message; throw; }
        }

        public static void AfterRestoreComplete()
        {
            var module = active;
            if (module == null) return;
            module.restoreBoundaries++;
            module.receipts.Add(new Dictionary<string, object> {
                {"stage","restore-complete"},{"unityFrame",Time.frameCount},{"fixedTime",Time.fixedTime}
            });
            if (module.receipts.Count > 256) module.receipts.RemoveAt(0);
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
            Deactivate(); disposed = true;
        }
    }
}
