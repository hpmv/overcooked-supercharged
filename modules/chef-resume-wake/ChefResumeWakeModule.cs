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
    // A warp writes chef poses while TimeManager has made their bodies
    // kinematic. Wake only those exact local chef bodies after the following
    // native Unfreeze restores their dynamic mode. No pose, velocity, clock or
    // non-chef state is written.
    public sealed class ChefResumeWakeModule : IAuthoringModule
    {
        private static ChefResumeWakeModule active;
        private Harmony harmony;
        private FieldInfo frozenBody;
        private readonly Dictionary<int, Rigidbody> pending = new Dictionary<int, Rigidbody>();
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long restoreBoundaries, wakes;
        private string failure;

        public string Name { get { return "local-chef-post-restore-wake-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefResumeWakeModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-wake operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"restoreBoundaries",restoreBoundaries},{"wakeCalls",wakes},{"pendingBodyIds",pending.Keys.OrderBy(x=>x).ToArray()},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","WakeUp only after the first native unfreeze following a completed restore, limited to exact registered local chef rigidbodies. No pose, velocity, gravity, clock or non-chef write."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef-wake activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-wake module is active.");
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            var frozen = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var unfreeze = frozen == null ? null : AccessTools.DeclaredMethod(frozen, "Unfreeze", Type.EmptyTypes);
            frozenBody = frozen == null ? null : frozen.GetField("m_frozenBody", BindingFlags.Instance | BindingFlags.NonPublic);
            if (complete == null || complete.ReturnType != typeof(void) || unfreeze == null || unfreeze.ReturnType != typeof(void)
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Frozen-X restore/unfreeze contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-wake." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(complete, postfix: Hook("AfterRestoreComplete"));
                harmony.Patch(unfreeze, prefix: Hook("BeforeUnfreeze"), postfix: Hook("AfterUnfreeze"));
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        public static void AfterRestoreComplete()
        {
            var module = active;
            if (module == null) return;
            try
            {
                module.pending.Clear();
                var entries = EntitySerialisationRegistry.m_EntitiesList;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries._items[i];
                    var obj = entry == null ? null : entry.m_GameObject;
                    if (obj == null || obj.GetComponent<ServerChefSynchroniser>() == null
                        || obj.GetComponent<ClientOnTheServerChefSynchroniser>() == null) continue;
                    var body = obj.GetComponent<Rigidbody>();
                    if (body == null) throw new InvalidOperationException("Local chef lacks its rigidbody.");
                    module.pending.Add(body.GetInstanceID(), body);
                }
                if (module.pending.Count != 4) throw new InvalidOperationException("Expected exactly four registered local chef bodies.");
                module.restoreBoundaries++;
                module.failure = null;
            }
            catch (Exception error)
            {
                module.pending.Clear();
                module.failure = error.Message;
                throw;
            }
        }

        public static void BeforeUnfreeze(object __instance, out Rigidbody __state)
        {
            __state = null;
            var module = active;
            if (module == null || __instance == null) return;
            __state = (Rigidbody)module.frozenBody.GetValue(__instance);
        }

        public static void AfterUnfreeze(Rigidbody __state)
        {
            var module = active;
            if (module == null || __state == null) return;
            Rigidbody expected;
            int id = __state.GetInstanceID();
            if (!module.pending.TryGetValue(id, out expected)) return;
            try
            {
                if (!ReferenceEquals(expected, __state) || __state.GetComponent<ServerChefSynchroniser>() == null
                    || __state.GetComponent<ClientOnTheServerChefSynchroniser>() == null || __state.isKinematic)
                    throw new InvalidOperationException("Pending local chef identity or dynamic resume mode changed.");
                bool before = __state.IsSleeping();
                var velocity = __state.velocity;
                var angular = __state.angularVelocity;
                var position = __state.position;
                var rotation = __state.rotation;
                __state.WakeUp();
                bool after = __state.IsSleeping();
                if (__state.isKinematic || __state.velocity != velocity || __state.angularVelocity != angular
                    || __state.position != position || __state.rotation != rotation)
                    throw new InvalidOperationException("WakeUp changed a required public chef body field.");
                module.pending.Remove(id);
                module.wakes++;
                module.receipts.Add(new Dictionary<string, object> {
                    {"bodyInstanceId",id},{"sleepingBefore",before},{"sleepingAfter",after},
                    {"position",new[]{position.x,position.y,position.z}},{"velocity",new[]{velocity.x,velocity.y,velocity.z}}
                });
                if (module.receipts.Count > 32) module.receipts.RemoveAt(0);
            }
            catch (Exception error)
            {
                module.failure = error.Message;
                module.pending.Clear();
                throw;
            }
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            pending.Clear();
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
