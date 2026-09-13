using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Native checkpoint restoration writes both Transform and Rigidbody poses.
    // Synchronize Unity's physics-scene transform cache once after the verified
    // restore, before the unchanged final native pause. Every exposed body pose,
    // velocity, mode and sleep value must remain exact across the call.
    public sealed class PhysicsSyncAfterRestoreModule : IAuthoringModule
    {
        private sealed class BodyState
        {
            internal int EntityId, BodyId;
            internal Rigidbody Body;
            internal Transform Transform;
            internal CapsuleCollider Capsule;
            internal Vector3 BodyPosition, LocalPosition, WorldPosition, Velocity, AngularVelocity;
            internal Vector3 CapsuleCenter, CapsuleBoundsCenter, CapsuleBoundsExtents;
            internal Quaternion BodyRotation, LocalRotation, WorldRotation;
            internal bool Kinematic, Gravity, DetectCollisions, Sleeping;
            internal bool CapsuleEnabled, CapsuleTrigger;
            internal float CapsuleRadius, CapsuleHeight, CapsuleContactOffset;
            internal int CapsuleDirection, CapsuleId;
            internal PhysicMaterial CapsuleMaterial;
        }

        private static PhysicsSyncAfterRestoreModule active;
        private Harmony harmony;
        private readonly List<object> receipts=new List<object>();
        private bool disposed;
        private long restores, syncCalls, refreshedColliders,pausedBoundaryCorrections;
        private bool pending;
        private List<BodyState> pendingTransforms;
        private string failure;
        private FieldInfo planSnapshot,snapshotFixedBodyPoses;

        public string Name { get { return "native-physics-sync-after-restore-v13-fixed-only-last"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("PhysicsSyncAfterRestoreModule");
            if(args!=null && args.Count!=0)throw new ArgumentException("Physics-sync operations take no arguments.");
            if(operation=="activate")Activate();
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"restores",restores},{"syncCalls",syncCalls},{"refreshedColliders",refreshedColliders},{"pending",pending},
                {"pendingTransformCorrection",pendingTransforms!=null},{"pausedBoundaryCorrections",pausedBoundaryCorrections},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","After every other restore postfix: retain only immutable fixed-body checkpoint Transform poses (never live dynamic-body poses), synchronize the frozen physics scene once with no collider reinsertion, then reapply only those fixed Transform poses at the start of each paused LateUpdate before bridge observation. Clear on resume; Rigidbody state must remain exact."}};
        }

        private void Activate()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Physics-sync activation requires native pause.");
            if(harmony!=null)return;
            if(active!=null)throw new InvalidOperationException("Another physics-sync module is active.");
            var complete=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan),"Complete",Type.EmptyTypes);
            var handle=AccessTools.DeclaredMethod(typeof(WarpHandler),"HandleWarpRequestIfAny",Type.EmptyTypes);
            var patcher=AccessTools.TypeByName("SuperchargedPatch.TASPatcher");
            var lateUpdate=patcher==null?null:AccessTools.DeclaredMethod(patcher,"LateUpdate",Type.EmptyTypes);
            var sync=AccessTools.DeclaredMethod(typeof(Physics),"SyncTransforms",Type.EmptyTypes);
            planSnapshot=typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic);
            snapshotFixedBodyPoses=planSnapshot==null?null:planSnapshot.FieldType.GetField("FixedBodyPoses",BindingFlags.Instance|BindingFlags.NonPublic);
            if(complete==null || complete.ReturnType!=typeof(void) || handle==null || handle.ReturnType!=typeof(void)
                || lateUpdate==null || lateUpdate.ReturnType!=typeof(void) || sync==null || sync.ReturnType!=typeof(void)
                || planSnapshot==null || snapshotFixedBodyPoses==null
                || snapshotFixedBodyPoses.FieldType!=typeof(NativeBodyPoseCheckpoint.Snapshot[]))
                throw new InvalidOperationException("Installed restore/physics synchronization contract differs.");
            harmony=new Harmony("supercharged.authoring.physics-sync-after-restore."+GetType().Assembly.GetName().Name);
            active=this;
            try
            {
                var afterComplete=new HarmonyMethod(GetType().GetMethod("AfterComplete",BindingFlags.Public|BindingFlags.Static));
                afterComplete.priority=Priority.Last;
                harmony.Patch(complete,postfix:afterComplete);
                harmony.Patch(handle,postfix:new HarmonyMethod(GetType().GetMethod("AfterHandleWarp",BindingFlags.Public|BindingFlags.Static)));
                harmony.Patch(lateUpdate,prefix:new HarmonyMethod(GetType().GetMethod("BeforeTasLateUpdate",BindingFlags.Public|BindingFlags.Static)));
            }
            catch{Deactivate();throw;}
        }

        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;if(module==null)return;
            var snapshot=module.planSnapshot.GetValue(__instance);
            var fixedBodies=snapshot==null?null:(NativeBodyPoseCheckpoint.Snapshot[])module.snapshotFixedBodyPoses.GetValue(snapshot);
            if(fixedBodies==null)throw new InvalidOperationException("Completed restore lacks its immutable fixed-body Transform checkpoint.");
            module.pendingTransforms=Capture(fixedBodies);
            var registered=Capture();
            var fixedIds=new HashSet<int>();
            for(int i=0;i<module.pendingTransforms.Count;i++)fixedIds.Add(module.pendingTransforms[i].EntityId);
            module.receipts.Add(new Dictionary<string,object>{{"phase","after-complete-fixed-target-collection"},
                {"fixedTargetCount",module.pendingTransforms.Count},{"fixedTargetIds",SortedIds(module.pendingTransforms)},
                {"excludedRegisteredBodyCount",registered.Count-module.pendingTransforms.Count},
                {"excludedRegisteredBodyIds",SortedIds(registered.FindAll(row=>!fixedIds.Contains(row.EntityId)))},
                {"harmonyPriority",Priority.Last},{"verifiedFixedOnly",true}});
            if(module.receipts.Count>32)module.receipts.RemoveAt(0);
            NativeBodyPoseCheckpoint.Snapshot saved47=null;
            for(int i=0;i<fixedBodies.Length;i++)if(fixedBodies[i]!=null&&fixedBodies[i].EntityId==47)saved47=fixedBodies[i];
            BodyState target47=null;
            for(int i=0;i<module.pendingTransforms.Count;i++)if(module.pendingTransforms[i].EntityId==47)target47=module.pendingTransforms[i];
            if(saved47!=null||target47!=null)
            {
                module.receipts.Add(new Dictionary<string,object>{{"phase","after-complete-checkpoint-target-audit"},
                    {"savedEntity47LocalPosition",saved47==null?null:Point(saved47.LocalPosition)},
                    {"savedEntity47WorldPosition",saved47==null?null:Point(saved47.WorldPosition)},
                    {"targetEntity47LocalPosition",target47==null?null:Point(target47.LocalPosition)},
                    {"targetEntity47WorldPosition",target47==null?null:Point(target47.WorldPosition)},
                    {"liveEntity47LocalPosition",target47==null?null:Point(target47.Transform.localPosition)},
                    {"liveEntity47BodyPosition",target47==null?null:Point(target47.Body.position)}});
                if(module.receipts.Count>32)module.receipts.RemoveAt(0);
            }
            module.pending=true;
        }

        public static void AfterHandleWarp()
        {
            var module=active;if(module==null)return;
            if(!module.pending)return;
            module.pending=false;
            try
            {
                if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                    throw new InvalidOperationException("Post-restore synchronization did not run after final native pause.");
                var before=Capture();
                Physics.SyncTransforms();
                module.syncCalls++;
                int refreshed=0;
                var after=Capture();
                if(before.Count!=after.Count)throw new InvalidOperationException("Registered rigidbody membership changed during Physics.SyncTransforms.");
                for(int i=0;i<before.Count;i++)RequireSame(before[i],after[i]);
                module.restores++;module.refreshedColliders+=refreshed;
                module.receipts.Add(new Dictionary<string,object>{{"restore",module.restores},{"syncCalls",module.syncCalls},
                    {"refreshedColliders",refreshed},{"bodyCount",before.Count},{"firstEntityId",before.Count==0?-1:before[0].EntityId},
                    {"lastEntityId",before.Count==0?-1:before[before.Count-1].EntityId},
                    {"verifiedExposedStateUnchanged",true}});
                if(module.receipts.Count>32)module.receipts.RemoveAt(0);
                module.failure=null;
            }
            catch(Exception error){module.failure=error.Message;throw;}
        }

        public static void BeforeTasLateUpdate()
        {
            var module=active;if(module==null||module.pendingTransforms==null)return;
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
            {
                module.pendingTransforms=null;
                return;
            }
            var target=module.pendingTransforms;
            try
            {
                var before=CaptureTargets(target);
                if(target.Count!=before.Count)
                    throw new InvalidOperationException("Registered rigidbody membership changed before paused-boundary Transform correction.");
                var changed=new List<object>();
                for(int i=0;i<target.Count;i++)
                {
                    var expected=target[i];var current=before[i];
                    RequireIdentity(expected,current);
                    if(!Exact(current.LocalPosition,expected.LocalPosition)||!Exact(current.LocalRotation,expected.LocalRotation)
                        ||!Exact(current.WorldPosition,expected.WorldPosition)||!Exact(current.WorldRotation,expected.WorldRotation))
                    {
                        changed.Add(new Dictionary<string,object>{{"entityId",expected.EntityId},
                            {"beforeLocalPosition",Point(current.LocalPosition)},{"targetLocalPosition",Point(expected.LocalPosition)},
                            {"beforeLocalRotation",Rotation(current.LocalRotation)},{"targetLocalRotation",Rotation(expected.LocalRotation)}});
                        if(!Exact(current.LocalPosition,expected.LocalPosition))current.Transform.localPosition=expected.LocalPosition;
                        if(!Exact(current.LocalRotation,expected.LocalRotation))current.Transform.localRotation=expected.LocalRotation;
                    }
                }
                var after=CaptureTargets(target);
                if(after.Count!=before.Count)
                    throw new InvalidOperationException("Registered rigidbody membership changed during paused-boundary Transform correction.");
                for(int i=0;i<after.Count;i++)
                {
                    RequirePhysicsSame(before[i],after[i]);
                    RequireTargetTransform(target[i],after[i]);
                }
                module.pausedBoundaryCorrections++;
                if(changed.Count!=0)
                {
                    module.receipts.Add(new Dictionary<string,object>{{"phase","pre-bridge-paused-transform-correction"},
                        {"correction",module.pausedBoundaryCorrections},{"bodyCount",after.Count},{"changed",changed.ToArray()},
                        {"rigidbodyStateUnchanged",true},{"targetTransformsExact",true}});
                    if(module.receipts.Count>32)module.receipts.RemoveAt(0);
                }
                module.failure=null;
            }
            catch(Exception error){module.failure=error.Message;throw;}
        }

        private static List<BodyState> Capture(NativeBodyPoseCheckpoint.Snapshot[] checkpointTransforms=null)
        {
            var rows=new List<BodyState>();
            var matched=checkpointTransforms==null?null:new HashSet<int>();
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];var obj=entry==null?null:entry.m_GameObject;
                var body=obj==null?null:obj.GetComponent<Rigidbody>();if(body==null)continue;
                var transform=body.transform;
                CapsuleCollider capsule=null;
                if(body.GetComponent<ServerChefSynchroniser>()!=null && body.GetComponent<ClientOnTheServerChefSynchroniser>()!=null)
                {
                    var capsules=body.GetComponents<CapsuleCollider>();
                    if(capsules.Length!=1 || !capsules[0].enabled)
                        throw new InvalidOperationException("Expected one enabled capsule on every local chef.");
                    capsule=capsules[0];
                }
                var bounds=capsule==null?default(Bounds):capsule.bounds;
                var row=new BodyState{EntityId=(int)entry.m_Header.m_uEntityID,BodyId=body.GetInstanceID(),Body=body,Transform=transform,
                    BodyPosition=body.position,BodyRotation=body.rotation,LocalPosition=transform.localPosition,LocalRotation=transform.localRotation,
                    WorldPosition=transform.position,WorldRotation=transform.rotation,Velocity=body.velocity,AngularVelocity=body.angularVelocity,
                    Kinematic=body.isKinematic,Gravity=body.useGravity,DetectCollisions=body.detectCollisions,Sleeping=body.IsSleeping(),
                    Capsule=capsule,CapsuleId=capsule==null?0:capsule.GetInstanceID(),CapsuleCenter=capsule==null?default(Vector3):capsule.center,
                    CapsuleBoundsCenter=bounds.center,CapsuleBoundsExtents=bounds.extents,CapsuleRadius=capsule==null?0:capsule.radius,
                    CapsuleHeight=capsule==null?0:capsule.height,CapsuleContactOffset=capsule==null?0:capsule.contactOffset,
                    CapsuleDirection=capsule==null?0:capsule.direction,CapsuleEnabled=capsule!=null&&capsule.enabled,
                    CapsuleTrigger=capsule!=null&&capsule.isTrigger,CapsuleMaterial=capsule==null?null:capsule.sharedMaterial};
                NativeBodyPoseCheckpoint.Snapshot saved=null;
                if(checkpointTransforms!=null)
                {
                    for(int j=0;j<checkpointTransforms.Length;j++)
                    {
                        var candidate=checkpointTransforms[j];
                        if(candidate!=null&&candidate.EntityId==row.EntityId)
                        {if(saved!=null)throw new InvalidOperationException("Duplicate immutable Transform checkpoint for entity "+row.EntityId+".");saved=candidate;}
                    }
                    if(saved!=null)
                    {
                        if(!ReferenceEquals(saved.Body,body)||!ReferenceEquals(saved.Transform,transform)
                            ||!ReferenceEquals(saved.Object,obj)||saved.Body.GetInstanceID()!=row.BodyId)
                            throw new InvalidOperationException("Immutable Transform checkpoint identity changed for entity "+row.EntityId+".");
                        row.LocalPosition=saved.LocalPosition;row.LocalRotation=saved.LocalRotation;
                        row.WorldPosition=saved.WorldPosition;row.WorldRotation=saved.WorldRotation;
                        matched.Add(row.EntityId);
                    }
                }
                if(checkpointTransforms==null||saved!=null)rows.Add(row);
            }
            if(checkpointTransforms!=null && matched.Count!=checkpointTransforms.Length)
                throw new InvalidOperationException("Immutable fixed-body Transform checkpoint membership changed.");
            rows.Sort((a,b)=>a.EntityId.CompareTo(b.EntityId));return rows;
        }

        private static List<BodyState> CaptureTargets(List<BodyState> targets)
        {
            if(targets==null)throw new ArgumentNullException("targets");
            var ids=new HashSet<int>();
            for(int i=0;i<targets.Count;i++)
                if(targets[i]==null||!ids.Add(targets[i].EntityId))
                    throw new InvalidOperationException("Paused-boundary fixed Transform targets contain a null or duplicate entity.");
            var result=Capture().FindAll(row=>ids.Contains(row.EntityId));
            if(result.Count!=targets.Count)
                throw new InvalidOperationException("Fixed-body rigidbody membership changed before paused-boundary Transform correction.");
            return result;
        }

        private static int[] SortedIds(List<BodyState> rows)
        {
            var result=new int[rows.Count];
            for(int i=0;i<rows.Count;i++)result[i]=rows[i].EntityId;
            Array.Sort(result);return result;
        }

        private static int RefreshLocalChefCapsules(List<BodyState> rows)
        {
            int count=0;
            for(int i=0;i<rows.Count;i++)
            {
                var capsule=rows[i].Capsule;if(capsule==null)continue;
                capsule.enabled=false;
                capsule.enabled=true;
                count++;
            }
            return count;
        }

        private static void RequireSame(BodyState a,BodyState b)
        {
            if(a.EntityId!=b.EntityId || a.BodyId!=b.BodyId || !ReferenceEquals(a.Body,b.Body) || !ReferenceEquals(a.Transform,b.Transform)
                || !Exact(a.BodyPosition,b.BodyPosition) || !Exact(a.BodyRotation,b.BodyRotation) || !Exact(a.LocalPosition,b.LocalPosition)
                || !Exact(a.LocalRotation,b.LocalRotation) || !Exact(a.WorldPosition,b.WorldPosition) || !Exact(a.WorldRotation,b.WorldRotation)
                || !Exact(a.Velocity,b.Velocity) || !Exact(a.AngularVelocity,b.AngularVelocity) || a.Kinematic!=b.Kinematic
                || a.Gravity!=b.Gravity || a.DetectCollisions!=b.DetectCollisions || a.Sleeping!=b.Sleeping
                || !ReferenceEquals(a.Capsule,b.Capsule) || a.CapsuleId!=b.CapsuleId || !Exact(a.CapsuleCenter,b.CapsuleCenter)
                || !Exact(a.CapsuleBoundsCenter,b.CapsuleBoundsCenter) || !Exact(a.CapsuleBoundsExtents,b.CapsuleBoundsExtents)
                || a.CapsuleRadius!=b.CapsuleRadius || a.CapsuleHeight!=b.CapsuleHeight
                || a.CapsuleContactOffset!=b.CapsuleContactOffset || a.CapsuleDirection!=b.CapsuleDirection
                || a.CapsuleEnabled!=b.CapsuleEnabled || a.CapsuleTrigger!=b.CapsuleTrigger
                || !ReferenceEquals(a.CapsuleMaterial,b.CapsuleMaterial))
                throw new InvalidOperationException("Physics.SyncTransforms changed exposed rigidbody state for entity "+a.EntityId+".");
        }

        private static void RequireIdentity(BodyState a,BodyState b)
        {
            if(a.EntityId!=b.EntityId||a.BodyId!=b.BodyId||!ReferenceEquals(a.Body,b.Body)
                ||!ReferenceEquals(a.Transform,b.Transform)||!ReferenceEquals(a.Capsule,b.Capsule)||a.CapsuleId!=b.CapsuleId)
                throw new InvalidOperationException("Post-collection Transform target identity changed for entity "+a.EntityId+".");
        }

        private static void RequirePhysicsSame(BodyState a,BodyState b)
        {
            RequireIdentity(a,b);
            if(!Exact(a.BodyPosition,b.BodyPosition)||!Exact(a.BodyRotation,b.BodyRotation)||!Exact(a.Velocity,b.Velocity)
                ||!Exact(a.AngularVelocity,b.AngularVelocity)||a.Kinematic!=b.Kinematic||a.Gravity!=b.Gravity
                ||a.DetectCollisions!=b.DetectCollisions||a.Sleeping!=b.Sleeping||!Exact(a.CapsuleCenter,b.CapsuleCenter)
                ||a.CapsuleRadius!=b.CapsuleRadius||a.CapsuleHeight!=b.CapsuleHeight
                ||a.CapsuleContactOffset!=b.CapsuleContactOffset||a.CapsuleDirection!=b.CapsuleDirection
                ||a.CapsuleEnabled!=b.CapsuleEnabled||a.CapsuleTrigger!=b.CapsuleTrigger
                ||!ReferenceEquals(a.CapsuleMaterial,b.CapsuleMaterial))
                throw new InvalidOperationException("Post-collection Transform correction changed Rigidbody/collider state for entity "+a.EntityId+".");
        }

        private static void RequireTargetTransform(BodyState target,BodyState actual)
        {
            RequireIdentity(target,actual);
            if(!Exact(target.LocalPosition,actual.LocalPosition)||!Exact(target.LocalRotation,actual.LocalRotation)
                ||!Exact(target.WorldPosition,actual.WorldPosition)||!Exact(target.WorldRotation,actual.WorldRotation))
                throw new InvalidOperationException("Post-collection Transform correction readback differs for entity "+target.EntityId+".");
        }

        private static bool Exact(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        private static bool Exact(Quaternion a,Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}

        private static object Point(Vector3 value){return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z}};}
        private static object Rotation(Quaternion value){return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},{"w",value.w}};}

        private void Deactivate(){if(harmony!=null)harmony.UnpatchSelf();harmony=null;pending=false;pendingTransforms=null;if(ReferenceEquals(active,this))active=null;}
        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
