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
    // A kinematic Rigidbody.MovePosition call can expose two legitimate poses:
    // Rigidbody.position is the current PhysX pose while Transform.position is
    // the pending target. The target is not exposed by Rigidbody and therefore
    // is not part of the normal body checkpoint. Capture only exact, observable
    // same-frame targets issued through the game's sole MovePosition wrapper.
    public sealed class RigidbodyMotionTargetModule : IAuthoringModule
    {
        private sealed class Latest
        {
            internal RigidbodyMotion Motion;
            internal Rigidbody Body;
            internal int MotionId, BodyId, EntityId, UnityFrame;
            internal string Callsite;
            internal Vector3 Target;
        }

        private sealed class Target
        {
            internal RigidbodyMotion Motion;
            internal Rigidbody Body;
            internal Transform Transform;
            internal int MotionId, BodyId, TransformId, EntityId, UnityFrame;
            internal string Callsite;
            internal Vector3 BodyPosition, TransformPosition, TransformLocalPosition, TargetPosition;
            internal Quaternion BodyRotation, TransformRotation, TransformLocalRotation;
            internal Vector3 Velocity, AngularVelocity;
            internal bool IsKinematic, UseGravity, DetectCollisions, Sleeping;
        }

        private sealed class Saved
        {
            internal int Frame;
            internal object Snapshot;
            internal Target[] Targets;
        }

        private static RigidbodyMotionTargetModule active;
        private Harmony harmony;
        private readonly Dictionary<int, Latest> latest = new Dictionary<int, Latest>();
        private readonly Dictionary<object, Saved> saved = new Dictionary<object, Saved>();
        private readonly List<object> receipts = new List<object>();
        private FieldInfo motionBody, history, planSnapshot, snapshotFrame;
        private bool disposed;
        private long calls, captures, restores;
        private string failure;

        public string Name { get { return "kinematic-rigidbody-motion-target-checkpoint-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("RigidbodyMotionTargetModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Rigidbody-motion-target operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"movePositionCalls",calls},{"captures",captures},{"restores",restores},
                {"latestBodies",latest.Count},{"savedSnapshots",saved.Count},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Exact same-frame pending MovePosition targets for registered kinematic RigidbodyMotion bodies only when target == Transform.position and target != Rigidbody.position. Reissued after the matching verified native restore; no pose, velocity, mode, clock, contact or message field may change immediately."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Rigidbody-motion-target activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another Rigidbody-motion-target module is active.");
            var movement = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "Movement", new[] {typeof(Vector3)});
            var movementWithDelta = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "Movement", new[] {typeof(Vector3),typeof(float)});
            var setPosition = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "SetPosition", new[] {typeof(Vector3).MakeByRefType()});
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            motionBody = AccessTools.Field(typeof(RigidbodyMotion), "m_rigidbody");
            history = typeof(NativeKitchenCheckpoint).GetField("history", BindingFlags.Static | BindingFlags.NonPublic);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            snapshotFrame = planSnapshot == null ? null : planSnapshot.FieldType.GetField("Frame", BindingFlags.Instance | BindingFlags.NonPublic);
            if (movement == null || movement.ReturnType != typeof(void) || movementWithDelta == null || movementWithDelta.ReturnType != typeof(void)
                || setPosition == null || setPosition.ReturnType != typeof(void) || capture == null || complete == null || complete.ReturnType != typeof(void)
                || motionBody == null || motionBody.FieldType != typeof(Rigidbody)
                || history == null || !typeof(IDictionary).IsAssignableFrom(history.FieldType)
                || planSnapshot == null || snapshotFrame == null || snapshotFrame.FieldType != typeof(int))
                throw new InvalidOperationException("Installed RigidbodyMotion/checkpoint contract differs.");
            harmony = new Harmony("supercharged.authoring.rigidbody-motion-target." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(movement, prefix: Hook("BeforeMovement"));
                harmony.Patch(movementWithDelta, prefix: Hook("BeforeMovementWithDelta"));
                harmony.Patch(setPosition, prefix: Hook("BeforeSetPosition"));
                harmony.Patch(capture, postfix: Hook("AfterCapture"));
                harmony.Patch(complete, postfix: Hook("AfterComplete"));
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        public static void BeforeMovement(RigidbodyMotion __instance, Vector3 __0)
        {
            var module = active; if (module == null || __instance == null) return;
            try
            {
                var body = (Rigidbody)module.motionBody.GetValue(__instance);
                if (body == null) return;
                module.Record(__instance, body.position + __0 * TimeManager.GetDeltaTime(__instance.gameObject), "Movement(Vector3)");
            }
            catch (Exception error) { module.failure = "Movement capture: " + error.Message; throw; }
        }

        public static void BeforeMovementWithDelta(RigidbodyMotion __instance, Vector3 __0, float __1)
        {
            var module = active; if (module == null || __instance == null) return;
            try
            {
                var body = (Rigidbody)module.motionBody.GetValue(__instance);
                if (body == null) return;
                module.Record(__instance, body.position + __0 * __1, "Movement(Vector3,float)");
            }
            catch (Exception error) { module.failure = "Movement-with-delta capture: " + error.Message; throw; }
        }

        public static void BeforeSetPosition(RigidbodyMotion __instance, Vector3 __0)
        {
            var module = active; if (module == null || __instance == null) return;
            try { module.Record(__instance, __0, "SetPosition(ref Vector3)"); }
            catch (Exception error) { module.failure = "SetPosition capture: " + error.Message; throw; }
        }

        private void Record(RigidbodyMotion motion, Vector3 target)
        {
            Record(motion, target, "unknown");
        }

        private void Record(RigidbodyMotion motion, Vector3 target, string callsite)
        {
            var body = (Rigidbody)motionBody.GetValue(motion);
            if (body == null) return;
            int entity = ResolveEntity(body);
            if (entity <= 0) return;
            int id = body.GetInstanceID();
            latest[id] = new Latest {Motion=motion,Body=body,MotionId=motion.GetInstanceID(),BodyId=id,EntityId=entity,
                UnityFrame=Time.frameCount,Callsite=callsite,Target=target};
            calls++;
        }

        private static int ResolveEntity(Rigidbody body)
        {
            int result=0,count=0;var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];
                if(entry!=null && entry.m_GameObject!=null && ReferenceEquals(entry.m_GameObject,body.gameObject))
                { result=(int)entry.m_Header.m_uEntityID; count++; }
            }
            return count==1?result:0;
        }

        public static void AfterCapture(int __0)
        {
            var module=active;if(module==null)return;
            try { module.Capture(__0); }
            catch(Exception error) { module.failure="Checkpoint capture: "+error.Message; }
        }

        private void Capture(int frame)
        {
            var native=((IDictionary)history.GetValue(null))[frame];
            if(native==null || saved.ContainsKey(native))return;
            var targets=new List<Target>();
            foreach(var row in latest.Values.OrderBy(x=>x.EntityId))
            {
                if(!IsCurrent(row) || row.UnityFrame!=Time.frameCount || !row.Body.isKinematic)continue;
                var transform=row.Body.transform;
                // A visible two-pose state proves this exact target is still
                // pending. Equal poses need no hidden state to reproduce.
                if(row.Target!=transform.position || row.Target==row.Body.position)continue;
                targets.Add(new Target {Motion=row.Motion,Body=row.Body,Transform=transform,MotionId=row.MotionId,BodyId=row.BodyId,
                    TransformId=transform.GetInstanceID(),EntityId=row.EntityId,UnityFrame=row.UnityFrame,Callsite=row.Callsite,
                    TargetPosition=row.Target,BodyPosition=row.Body.position,BodyRotation=row.Body.rotation,
                    TransformPosition=transform.position,TransformRotation=transform.rotation,
                    TransformLocalPosition=transform.localPosition,TransformLocalRotation=transform.localRotation,
                    Velocity=row.Body.velocity,AngularVelocity=row.Body.angularVelocity,IsKinematic=row.Body.isKinematic,
                    UseGravity=row.Body.useGravity,DetectCollisions=row.Body.detectCollisions,Sleeping=row.Body.IsSleeping()});
            }
            saved.Add(native,new Saved {Frame=frame,Snapshot=native,Targets=targets.ToArray()});
            captures++;
            receipts.Add(new Dictionary<string,object>{{"phase","capture"},{"frame",frame},{"unityFrame",Time.frameCount},
                {"targetCount",targets.Count},{"targets",targets.Select(Receipt).ToArray()}});
            if(receipts.Count>48)receipts.RemoveAt(0);
            if(saved.Count>20000)throw new InvalidOperationException("Rigidbody-motion-target checkpoint bound reached.");
            failure=null;
        }

        private static bool IsCurrent(Latest row)
        {
            return row!=null && row.Motion!=null && row.Body!=null && row.Motion.GetInstanceID()==row.MotionId
                && row.Body.GetInstanceID()==row.BodyId && ReferenceEquals(row.Motion.GetComponent<Rigidbody>(),row.Body)
                && ResolveEntity(row.Body)==row.EntityId;
        }

        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;if(module==null)return;
            try { module.Restore(__instance); }
            catch(Exception error) { module.failure="Restore: "+error.Message; throw; }
        }

        private void Restore(NativeKitchenCheckpoint.RestorePlan plan)
        {
            var native=planSnapshot.GetValue(plan);Saved checkpoint;
            int frame=native==null?-1:(int)snapshotFrame.GetValue(native);
            if(native==null || !saved.TryGetValue(native,out checkpoint) || checkpoint.Frame!=frame)
                throw new InvalidOperationException("Completed restore lacks the matching Rigidbody-motion-target checkpoint.");
            var rows=new List<object>();
            foreach(var target in checkpoint.Targets)
            {
                if(!IsCurrent(target) || !target.Body.isKinematic)
                    throw new InvalidOperationException("Captured RigidbodyMotion identity or kinematic mode changed for entity "+target.EntityId+".");
                RequireExact(target,"before MovePosition target restore");
                target.Body.MovePosition(target.TargetPosition);
                RequireExact(target,"after MovePosition target restore");
                rows.Add(Receipt(target));
            }
            restores++;
            receipts.Add(new Dictionary<string,object>{{"phase","restore"},{"frame",frame},{"targetCount",rows.Count},
                {"targets",rows.ToArray()},{"verifiedImmediatePublicStateUnchanged",true}});
            if(receipts.Count>48)receipts.RemoveAt(0);
            foreach(var key in saved.Where(x=>x.Value.Frame>frame).Select(x=>x.Key).ToArray())saved.Remove(key);
            failure=null;
        }

        private static bool IsCurrent(Target row)
        {
            return row!=null && row.Motion!=null && row.Body!=null && row.Transform!=null
                && row.Motion.GetInstanceID()==row.MotionId && row.Body.GetInstanceID()==row.BodyId
                && row.Transform.GetInstanceID()==row.TransformId && ReferenceEquals(row.Body.transform,row.Transform)
                && ReferenceEquals(row.Motion.GetComponent<Rigidbody>(),row.Body) && ResolveEntity(row.Body)==row.EntityId;
        }

        private static void RequireExact(Target expected,string phase)
        {
            var body=expected.Body;var transform=expected.Transform;
            if(body.position!=expected.BodyPosition || body.rotation!=expected.BodyRotation
                || transform.position!=expected.TransformPosition || transform.rotation!=expected.TransformRotation
                || transform.localPosition!=expected.TransformLocalPosition || transform.localRotation!=expected.TransformLocalRotation
                || body.velocity!=expected.Velocity || body.angularVelocity!=expected.AngularVelocity
                || body.isKinematic!=expected.IsKinematic || body.useGravity!=expected.UseGravity
                || body.detectCollisions!=expected.DetectCollisions || body.IsSleeping()!=expected.Sleeping)
                throw new InvalidOperationException("Rigidbody-motion-target public state differs "+phase+" for entity "+expected.EntityId+".");
        }

        private static object Receipt(Target value)
        {
            return new Dictionary<string,object>{{"entityId",value.EntityId},{"bodyInstanceId",value.BodyId},
                {"motionInstanceId",value.MotionId},{"capturedUnityFrame",value.UnityFrame},{"callsite",value.Callsite},
                {"bodyPosition",Point(value.BodyPosition)},{"targetPosition",Point(value.TargetPosition)},
                {"transformLocalPosition",Point(value.TransformLocalPosition)}};
        }

        private static object Point(Vector3 value)
        {
            return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z}};
        }

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;latest.Clear();saved.Clear();
            if(ReferenceEquals(active,this))active=null;
        }

        public void Dispose()
        {
            if(disposed)return;Deactivate();disposed=true;
        }
    }
}
