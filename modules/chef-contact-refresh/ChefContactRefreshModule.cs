using System;
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
    // TimeManager's authoring pause changes local chefs between dynamic and
    // kinematic modes. Re-register each enabled local-chef capsule after every
    // ready-kitchen unfreeze so original and replay both rebuild PhysX contact
    // pairs from the same public pose. All exposed fields remain bit-exact.
    public sealed class ChefContactRefreshModule : IAuthoringModule
    {
        private static ChefContactRefreshModule active;
        private Harmony harmony;
        private FieldInfo frozenBody;
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long unfreezes, refreshed;
        private string failure;

        public string Name { get { return "authoring-local-chef-contact-refresh-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefContactRefreshModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-contact operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"unfreezes",unfreezes},{"refreshedColliders",refreshed},{"failure",failure},
                {"receipts",receipts.ToArray()},
                {"scope","After every ready-kitchen native Unfreeze: enabled local-chef CapsuleCollider unregister/register with exact public body/collider readback. No advancing-gameplay hook or pose write."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef-contact activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-contact module is active.");
            var frozen=typeof(TimeManager).GetNestedType("FrozenPhysicsData",BindingFlags.NonPublic);
            var unfreeze=frozen==null?null:AccessTools.DeclaredMethod(frozen,"Unfreeze",Type.EmptyTypes);
            frozenBody=frozen==null?null:frozen.GetField("m_frozenBody",BindingFlags.Instance|BindingFlags.NonPublic);
            if (unfreeze == null || unfreeze.ReturnType != typeof(void)
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Installed native unfreeze contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-contact-refresh." + GetType().Assembly.GetName().Name);
            active = this;
            try { harmony.Patch(unfreeze,
                prefix: new HarmonyMethod(GetType().GetMethod("BeforeUnfreeze", BindingFlags.Public | BindingFlags.Static)),
                postfix: new HarmonyMethod(GetType().GetMethod("AfterUnfreeze", BindingFlags.Public | BindingFlags.Static))); }
            catch { Deactivate(); throw; }
        }

        public static void BeforeUnfreeze(object __instance, out Rigidbody __state)
        {
            __state=null;
            var module=active;
            if(module==null || __instance==null || !NativeSessionBridge.KitchenReady)return;
            var body=(Rigidbody)module.frozenBody.GetValue(__instance);
            if(body!=null && body.GetComponent<ServerChefSynchroniser>()!=null
                && body.GetComponent<ClientOnTheServerChefSynchroniser>()!=null)__state=body;
        }

        public static void AfterUnfreeze(Rigidbody __state)
        {
            var module = active;
            if (module == null || __state == null) return;
            try
            {
                var body=__state;
                var colliders=body.GetComponents<CapsuleCollider>();
                if(colliders.Length!=1 || !colliders[0].enabled)
                    throw new InvalidOperationException("Expected one enabled capsule on a local chef.");
                var collider=colliders[0];
                var position=body.position; var rotation=body.rotation;
                var velocity=body.velocity; var angular=body.angularVelocity;
                bool kinematic=body.isKinematic, gravity=body.useGravity, detect=body.detectCollisions;
                var center=collider.center; float radius=collider.radius,height=collider.height;
                int direction=collider.direction; bool trigger=collider.isTrigger;
                collider.enabled=false; collider.enabled=true;
                if(!collider.enabled || collider.center!=center || collider.radius!=radius || collider.height!=height
                    || collider.direction!=direction || collider.isTrigger!=trigger || body.position!=position
                    || body.rotation!=rotation || body.velocity!=velocity || body.angularVelocity!=angular
                    || body.isKinematic!=kinematic || body.useGravity!=gravity || body.detectCollisions!=detect)
                    throw new InvalidOperationException("Contact refresh changed an exposed body or collider field.");
                module.unfreezes++; module.refreshed++;
                module.receipts.Add(new Dictionary<string,object>{{"unfreeze",module.unfreezes},
                    {"bodyInstanceId",body.GetInstanceID()},{"colliderInstanceId",collider.GetInstanceID()},
                    {"position",new[]{position.x,position.y,position.z}}});
                if(module.receipts.Count>48)module.receipts.RemoveAt(0);
                module.failure=null;
            }
            catch (Exception error) { module.failure=error.Message; throw; }
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony=null;
            if (ReferenceEquals(active,this)) active=null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate(); disposed=true;
        }
    }
}
