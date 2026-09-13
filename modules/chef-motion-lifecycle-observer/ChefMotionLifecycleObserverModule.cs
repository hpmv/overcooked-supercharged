using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only event ordering around the managed MovePosition callsite and
    // TimeManager's native pause lifecycle. This module never changes a body.
    public sealed class ChefMotionLifecycleObserverModule : IAuthoringModule
    {
        public sealed class Sample
        {
            internal Rigidbody Body;
            internal int BodyId;
            internal Vector3 Position, TransformPosition, Movement, Target;
            internal bool Kinematic, Gravity, Paused;
            internal float Delta;
        }

        private static ChefMotionLifecycleObserverModule active;
        private Harmony harmony;
        private FieldInfo motionBody, frozenBody;
        private readonly List<object> receipts = new List<object>();
        private bool disposed;
        private long sequence, movementCalls, freezes, unfreezes;
        private string failure;

        public string Name { get { return "local-chef-motion-lifecycle-observer-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefMotionLifecycleObserverModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Motion-lifecycle observer operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation == "clear") { receipts.Clear(); sequence=movementCalls=freezes=unfreezes=0; failure=null; }
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate, clear or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"movementCalls",movementCalls},{"freezes",freezes},{"unfreezes",unfreezes},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Read-only local-chef RigidbodyMotion.Movement and native FrozenPhysicsData constructor/Unfreeze ordering. No body or gameplay writes."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Motion-lifecycle observer activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another motion-lifecycle observer is active.");
            var movement=AccessTools.DeclaredMethod(typeof(RigidbodyMotion),"Movement",new[]{typeof(Vector3),typeof(float)});
            var frozen=typeof(TimeManager).GetNestedType("FrozenPhysicsData",BindingFlags.NonPublic);
            var constructor=frozen==null?null:AccessTools.Constructor(frozen,new[]{typeof(Rigidbody),typeof(int)});
            var unfreeze=frozen==null?null:AccessTools.DeclaredMethod(frozen,"Unfreeze",Type.EmptyTypes);
            motionBody=AccessTools.Field(typeof(RigidbodyMotion),"m_rigidbody");
            frozenBody=frozen==null?null:frozen.GetField("m_frozenBody",BindingFlags.Instance|BindingFlags.NonPublic);
            if(movement==null || movement.ReturnType!=typeof(void) || constructor==null || unfreeze==null
                || unfreeze.ReturnType!=typeof(void) || motionBody==null || motionBody.FieldType!=typeof(Rigidbody)
                || frozenBody==null || frozenBody.FieldType!=typeof(Rigidbody))
                throw new InvalidOperationException("Installed motion/pause lifecycle contract differs.");
            harmony=new Harmony("supercharged.authoring.chef-motion-lifecycle-observer."+GetType().Assembly.GetName().Name);
            active=this;
            try {
                harmony.Patch(movement,prefix:Hook("BeforeMovement"),postfix:Hook("AfterMovement"));
                harmony.Patch(constructor,prefix:Hook("BeforeFreeze"),postfix:Hook("AfterFreeze"));
                harmony.Patch(unfreeze,prefix:Hook("BeforeUnfreeze"),postfix:Hook("AfterUnfreeze"));
            } catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Static));
        }

        private static bool Local(Rigidbody body)
        {
            return body!=null && body.GetComponent<ServerChefSynchroniser>()!=null
                && body.GetComponent<ClientOnTheServerChefSynchroniser>()!=null;
        }

        private static Sample Read(Rigidbody body)
        {
            return new Sample {Body=body,BodyId=body.GetInstanceID(),Position=body.position,
                TransformPosition=body.transform.position,Kinematic=body.isKinematic,Gravity=body.useGravity,
                Paused=TimeManager.IsPaused(TimeManager.PauseLayer.Main)};
        }

        public static void BeforeMovement(RigidbodyMotion __instance,Vector3 __0,float __1,out Sample __state)
        {
            __state=null; var module=active; if(module==null || __instance==null)return;
            try {
                var body=(Rigidbody)module.motionBody.GetValue(__instance); if(!Local(body))return;
                __state=Read(body); __state.Movement=__0; __state.Delta=__1;
                __state.Target=body.position+__0*__1; module.movementCalls++;
            } catch(Exception error){module.failure="Movement prefix: "+error.Message;throw;}
        }

        public static void AfterMovement(Sample __state)
        {
            var module=active; if(module==null || __state==null)return;
            try { module.Add("movement",__state,Read(__state.Body)); }
            catch(Exception error){module.failure="Movement postfix: "+error.Message;throw;}
        }

        public static void BeforeFreeze(Rigidbody __0,out Sample __state)
        {
            __state=null; var module=active; if(module==null || !Local(__0))return;
            __state=Read(__0); module.freezes++;
        }

        public static void AfterFreeze(Sample __state)
        {
            var module=active; if(module==null || __state==null)return;
            try { module.Add("freeze",__state,Read(__state.Body)); }
            catch(Exception error){module.failure="Freeze postfix: "+error.Message;throw;}
        }

        public static void BeforeUnfreeze(object __instance,out Sample __state)
        {
            __state=null; var module=active; if(module==null || __instance==null)return;
            try { var body=(Rigidbody)module.frozenBody.GetValue(__instance); if(Local(body)){__state=Read(body);module.unfreezes++;} }
            catch(Exception error){module.failure="Unfreeze prefix: "+error.Message;throw;}
        }

        public static void AfterUnfreeze(Sample __state)
        {
            var module=active; if(module==null || __state==null)return;
            try { module.Add("unfreeze",__state,Read(__state.Body)); }
            catch(Exception error){module.failure="Unfreeze postfix: "+error.Message;throw;}
        }

        private void Add(string stage,Sample before,Sample after)
        {
            var row=new Dictionary<string,object> {
                {"sequence",++sequence},{"stage",stage},{"unityFrame",Time.frameCount},{"time",Time.time},{"fixedTime",Time.fixedTime},
                {"bodyInstanceId",before.BodyId},{"beforePosition",Point(before.Position)},
                {"beforeTransform",Point(before.TransformPosition)},{"beforeKinematic",before.Kinematic},
                {"beforeGravity",before.Gravity},{"beforePaused",before.Paused},
                {"afterPosition",Point(after.Position)},{"afterTransform",Point(after.TransformPosition)},
                {"afterKinematic",after.Kinematic},{"afterGravity",after.Gravity},{"afterPaused",after.Paused}
            };
            if(stage=="movement") { row.Add("movement",Point(before.Movement));row.Add("delta",before.Delta);row.Add("target",Point(before.Target)); }
            receipts.Add(row); if(receipts.Count>512)receipts.RemoveAt(0);
        }

        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf(); harmony=null;
            if(ReferenceEquals(active,this))active=null;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
