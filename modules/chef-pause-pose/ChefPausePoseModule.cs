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
    // TimeManager receives the exact advancing local-chef pose, then its
    // dynamic-to-kinematic pause transition can expose a different pose on a
    // later paused callback. Preserve the position observed immediately before
    // native freeze for the duration of that pause. Native Unfreeze still owns
    // velocity, angular velocity, kinematic mode and gravity restoration.
    public sealed class ChefPausePoseModule : IAuthoringModule
    {
        private sealed class Frozen
        {
            internal object Owner;
            internal Rigidbody Body;
            internal int BodyId;
            internal Vector3 Position;
        }

        private static ChefPausePoseModule active;
        private Harmony harmony;
        private FieldInfo frozenBody;
        private readonly Dictionary<int, Frozen> frozen = new Dictionary<int, Frozen>();
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long freezes, unfreezes, captureChecks, reapplied;
        private string failure;

        public string Name { get { return "authoring-local-chef-pause-pose-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefPausePoseModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Chef-pause-pose operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"freezes",freezes},{"unfreezes",unfreezes},{"captureChecks",captureChecks},
                {"reappliedPositions",reapplied},{"pendingBodyIds",frozen.Keys.OrderBy(x=>x).ToArray()},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Local chefs only: for every ready-kitchen native Main pause, keep exact pre-FrozenPhysicsData Rigidbody.position. No advancing callback, velocity, rotation, gravity, collision, clock or gameplay-state write."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Chef-pause-pose activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another chef-pause-pose module is active.");
            var type = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var constructor = type == null ? null : AccessTools.Constructor(type, new[] {typeof(Rigidbody), typeof(int)});
            var unfreeze = type == null ? null : AccessTools.DeclaredMethod(type, "Unfreeze", Type.EmptyTypes);
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            frozenBody = type == null ? null : type.GetField("m_frozenBody", BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructor == null || unfreeze == null || unfreeze.ReturnType != typeof(void)
                || capture == null
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Installed pause/checkpoint contract differs.");
            harmony = new Harmony("supercharged.authoring.chef-pause-pose." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(constructor, prefix: Hook("BeforeFreeze"));
                harmony.Patch(unfreeze, prefix: Hook("BeforeUnfreeze"), postfix: Hook("AfterUnfreeze"));
                harmony.Patch(capture, prefix: Hook("BeforeCapture"));
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

        public static void BeforeFreeze(object __instance, Rigidbody __0)
        {
            var module=active;
            if (module==null || __instance==null || !IsLocalChef(__0)) return;
            if (!NativeSessionBridge.KitchenReady) return;
            try
            {
                int id=__0.GetInstanceID();
                if (module.frozen.ContainsKey(id))
                    throw new InvalidOperationException("Local chef was frozen twice without unfreeze.");
                var position=__0.position;
                module.frozen.Add(id,new Frozen {Owner=__instance,Body=__0,BodyId=id,Position=position});
                module.freezes++;
                module.AddReceipt("freeze",__0,position,position);
            }
            catch(Exception error){module.failure="Freeze: "+error.Message;throw;}
        }

        public static void BeforeCapture()
        {
            var module=active;
            if(module==null || !TimeManager.IsPaused(TimeManager.PauseLayer.Main))return;
            try
            {
                foreach(var row in module.frozen.Values.ToArray())
                {
                    var body=row.Body;
                    if(!IsLocalChef(body) || body.GetInstanceID()!=row.BodyId || !body.isKinematic)
                        throw new InvalidOperationException("Frozen local-chef identity or pause mode changed.");
                    var before=body.position;
                    var rotation=body.rotation; var velocity=body.velocity; var angular=body.angularVelocity;
                    bool gravity=body.useGravity,detect=body.detectCollisions;
                    if(before!=row.Position)
                    {
                        body.position=row.Position;
                        if(body.position!=row.Position)throw new InvalidOperationException("Exact pre-freeze position lacks public readback.");
                        module.reapplied++;
                        module.AddReceipt("reapply",body,before,body.position);
                    }
                    if(body.rotation!=rotation || body.velocity!=velocity || body.angularVelocity!=angular
                        || !body.isKinematic || body.useGravity!=gravity || body.detectCollisions!=detect)
                        throw new InvalidOperationException("Pause-pose preservation changed another public body field.");
                    module.captureChecks++;
                }
                module.failure=null;
            }
            catch(Exception error){module.failure="Capture: "+error.Message;throw;}
        }

        public static void BeforeUnfreeze(object __instance, out int __state)
        {
            __state=0;
            var module=active;
            if(module==null || __instance==null)return;
            var body=(Rigidbody)module.frozenBody.GetValue(__instance);
            if(body==null || !IsLocalChef(body))return;
            int id=body.GetInstanceID();
            // A module may be activated while the bridge already owns a native
            // pause. Those FrozenPhysicsData instances predate this module and
            // therefore have no saved pose here. Let native Unfreeze retire
            // them normally; only validate pauses captured by BeforeFreeze.
            if(module.frozen.ContainsKey(id))__state=id;
        }

        public static void AfterUnfreeze(object __instance, int __state)
        {
            var module=active;
            if(module==null || __state==0)return;
            try
            {
                Frozen row;
                if(!module.frozen.TryGetValue(__state,out row) || !ReferenceEquals(row.Owner,__instance)
                    || !IsLocalChef(row.Body) || row.Body.GetInstanceID()!=row.BodyId || row.Body.isKinematic)
                    throw new InvalidOperationException("Native unfreeze did not match a captured local-chef pause.");
                module.frozen.Remove(__state);
                module.unfreezes++;
                module.AddReceipt("unfreeze",row.Body,row.Position,row.Body.position);
                module.failure=null;
            }
            catch(Exception error){module.failure="Unfreeze: "+error.Message;throw;}
        }

        private void AddReceipt(string stage,Rigidbody body,Vector3 before,Vector3 after)
        {
            if(receipts.Count>=48)receipts.RemoveAt(0);
            receipts.Add(new Dictionary<string,object>{{"stage",stage},{"unityFrame",Time.frameCount},
                {"bodyInstanceId",body.GetInstanceID()},{"before",Point(before)},{"after",Point(after)}});
        }

        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf();
            harmony=null;frozen.Clear();
            if(ReferenceEquals(active,this))active=null;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
