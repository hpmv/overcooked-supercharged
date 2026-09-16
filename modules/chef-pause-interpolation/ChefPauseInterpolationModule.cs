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
    // TimeManager already freezes local chefs for an authoring pause. While a
    // body is frozen, suppress only Unity's render interpolation buffer; put
    // the exact installed interpolation mode back immediately after native
    // Unfreeze and before advancing gameplay resumes. No pose or motion field
    // is assigned by this sidecar.
    public sealed class ChefPauseInterpolationModule : IAuthoringModule
    {
        private sealed class Frozen
        {
            internal Rigidbody Body;
            internal int BodyId;
            internal RigidbodyInterpolation Interpolation;
        }

        private static ChefPauseInterpolationModule active;
        private Harmony harmony;
        private FieldInfo frozenBody;
        private readonly Dictionary<object, Frozen> frozen = new Dictionary<object, Frozen>();
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long freezes, unfreezes;
        private string failure;

        public string Name { get { return "authoring-local-chef-pause-interpolation-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefPauseInterpolationModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-pause interpolation operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"freezes",freezes},{"unfreezes",unfreezes},{"pendingFrozenObjects",frozen.Count},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Local chefs only: Rigidbody.interpolation is None only while native TimeManager has already frozen the body, then its exact prior mode is restored after native Unfreeze. No pose, velocity, gravity, collision or gameplay field write."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef-pause interpolation activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-pause interpolation module is active.");
            var type = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var constructor = type == null ? null : AccessTools.Constructor(type, new[] {typeof(Rigidbody), typeof(int)});
            var unfreeze = type == null ? null : AccessTools.DeclaredMethod(type, "Unfreeze", Type.EmptyTypes);
            frozenBody = type == null ? null : type.GetField("m_frozenBody", BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructor == null || unfreeze == null || unfreeze.ReturnType != typeof(void)
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Installed TimeManager freeze contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-pause-interpolation." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(constructor, postfix: Hook("AfterFreeze"));
                harmony.Patch(unfreeze, prefix: Hook("BeforeUnfreeze"), postfix: Hook("AfterUnfreeze"));
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

        public static void AfterFreeze(object __instance, Rigidbody __0)
        {
            var module = active;
            if (module == null || __instance == null || !IsLocalChef(__0)) return;
            try
            {
                if (!__0.isKinematic || module.frozen.ContainsKey(__instance))
                    throw new InvalidOperationException("Unexpected local-chef native freeze state.");
                var position = __0.position;
                var rotation = __0.rotation;
                var velocity = __0.velocity;
                var angular = __0.angularVelocity;
                bool gravity = __0.useGravity;
                var interpolation = __0.interpolation;
                __0.interpolation = RigidbodyInterpolation.None;
                if (__0.position != position || __0.rotation != rotation || __0.velocity != velocity
                    || __0.angularVelocity != angular || !__0.isKinematic || __0.useGravity != gravity)
                    throw new InvalidOperationException("Disabling frozen interpolation changed a public physics field.");
                module.frozen.Add(__instance, new Frozen {Body=__0,BodyId=__0.GetInstanceID(),Interpolation=interpolation});
                module.freezes++;
                module.AddReceipt("freeze", __0, interpolation, __0.interpolation);
            }
            catch (Exception error) { module.failure = "Freeze: " + error.Message; throw; }
        }

        public static void BeforeUnfreeze(object __instance, out object __state)
        {
            __state = null;
            var module = active;
            if (module == null || __instance == null) return;
            Frozen value;
            if (module.frozen.TryGetValue(__instance, out value)) __state = value;
        }

        public static void AfterUnfreeze(object __instance, object __state)
        {
            var module = active;
            var saved = __state as Frozen;
            if (module == null || saved == null) return;
            try
            {
                var body = saved.Body;
                if (!IsLocalChef(body) || body.GetInstanceID() != saved.BodyId || body.isKinematic)
                    throw new InvalidOperationException("Local-chef identity or native unfreeze mode changed.");
                var position = body.position;
                var rotation = body.rotation;
                var velocity = body.velocity;
                var angular = body.angularVelocity;
                bool gravity = body.useGravity;
                var before = body.interpolation;
                body.interpolation = saved.Interpolation;
                if (body.position != position || body.rotation != rotation || body.velocity != velocity
                    || body.angularVelocity != angular || body.isKinematic || body.useGravity != gravity)
                    throw new InvalidOperationException("Restoring interpolation changed a public physics field.");
                module.frozen.Remove(__instance);
                module.unfreezes++;
                module.AddReceipt("unfreeze", body, before, body.interpolation);
                module.failure = null;
            }
            catch (Exception error) { module.failure = "Unfreeze: " + error.Message; throw; }
        }

        private void AddReceipt(string stage, Rigidbody body, RigidbodyInterpolation before, RigidbodyInterpolation after)
        {
            if (receipts.Count >= 48) receipts.RemoveAt(0);
            var position = body.position;
            receipts.Add(new Dictionary<string, object> {
                {"stage",stage},{"unityFrame",Time.frameCount},{"bodyInstanceId",body.GetInstanceID()},
                {"position",new[]{position.x,position.y,position.z}},
                {"before",before.ToString()},{"after",after.ToString()}
            });
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            foreach (var item in frozen.Values.ToArray())
                if (item.Body != null && item.Body.GetInstanceID() == item.BodyId)
                    item.Body.interpolation = item.Interpolation;
            frozen.Clear();
            if (ReferenceEquals(active,this)) active=null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate(); disposed=true;
        }
    }
}
