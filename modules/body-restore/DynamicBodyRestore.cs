using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;
using BodyInvariants=SuperchargedPatch.NativeBodyPoseCheckpoint.BodyInvariants;
using Shape=SuperchargedPatch.NativeBodyColliderCheckpoint.Shape;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class BodyRestoreModule
    {
        // Empty PhysicalAttachment containers may be rebound across an exact
        // recreation.  A loose item moves its collider hierarchy under that
        // same container, so collider-bearing containers are admitted only
        // while the exact managed/native body and shape incarnation survives.
        // Cross-incarnation native shape rebinding remains deliberately absent.
        private sealed class DynamicBodyToken
        {
            internal BodyRestoreModule Owner;
            internal int EntityId;
            internal Snapshot Target;
            internal DynamicBodySignature Signature;
            internal NativeShapeCheckpoint NativeShapes;
            internal bool SameIncarnationOnly;
        }

        private sealed class DynamicBodySignature
        {
            internal string RootName;
            internal string[] RootComponents;
            internal DynamicParentSignature[] Parents;
        }

        private sealed class DynamicParentSignature
        {
            internal string Name;
            internal string[] Components;
            internal int? RegisteredEntityId;
        }

        // Capture exactly one current registered dynamic Rigidbody. The token
        // is opaque so another hot module cannot depend on this private schema
        // or rewrite its target values.
        public static object CaptureDynamicBody(int entityId)
        {
            var owner=RequireDynamicBodyOwner();
            if(entityId<=0)throw new ArgumentOutOfRangeException("entityId");
            var rows=NativeBodyPoseCheckpoint.Capture(new HashSet<int>{entityId});
            if(rows.Length!=1||rows[0].EntityId!=entityId)
                throw new InvalidOperationException("Dynamic body capture did not resolve exactly one requested entity.");
            var row=rows[0];
            NativeShapeCheckpoint native;
            if(!owner.RequireNativeShapeTargets(rows).TryGetValue(row,out native)||native==null)
                throw new InvalidOperationException("Dynamic body native shape checkpoint is missing: "+entityId);
            bool managedShapes=row.Colliders!=null&&row.Colliders.Length!=0;
            bool nativeShapes=native.Shapes!=null&&native.Shapes.Length!=0;
            if(managedShapes!=nativeShapes||managedShapes&&
                (row.Colliders.Length!=native.Shapes.Length||!row.HasAnalyticColliders))
                throw new InvalidOperationException("Dynamic body "+entityId+
                    " managed/native analytic shape signature differs.");
            if(!managedShapes)
            {
                RequireEmptyDynamicColliderSignature(row,entityId,"captured");
                RequireEmptyNativeShapeSignature(native,entityId,"captured");
            }
            return new DynamicBodyToken {Owner=owner,EntityId=entityId,Target=row,
                Signature=CaptureDynamicSignature(row),NativeShapes=CloneNativeShapes(native),
                SameIncarnationOnly=managedShapes};
        }

        // Rebind historical target values to a fresh current Snapshot, then use
        // the active r23 algorithm for its already-proved empty-proxy mass and
        // exact native pose restoration. Old Unity/native references are never
        // installed in the rebound checkpoint.
        public static void RestoreDynamicBody(object token,int entityId)
        {
            var owner=RequireDynamicBodyOwner();
            var saved=token as DynamicBodyToken;
            if(saved==null||!ReferenceEquals(saved.Owner,owner)||saved.EntityId!=entityId||saved.Target==null
                ||saved.Signature==null||saved.NativeShapes==null)
                throw new InvalidOperationException("Dynamic body token does not belong to the active body restore module/entity.");

            var currentRows=NativeBodyPoseCheckpoint.Capture(new HashSet<int>{entityId});
            if(currentRows.Length!=1||currentRows[0].EntityId!=entityId)
                throw new InvalidOperationException("Dynamic body restore did not resolve exactly one recreated entity.");
            var rebound=currentRows[0];
            if(saved.SameIncarnationOnly)
            {
                if(!SameDynamicIncarnation(saved.Target,rebound))
                    throw new InvalidOperationException("Collider-bearing dynamic body requires its exact surviving incarnation: "+entityId);
                RequireSameDynamicSignature(saved.Signature,CaptureDynamicSignature(rebound),entityId);
                RequireSameDynamicMotion(saved.Target,rebound,entityId);
                RequireSameDynamicColliderStaticSignature(saved.Target.Colliders,rebound.Colliders,entityId);
                NativeShapeCheckpoint targetNative;
                if(!owner.RequireNativeShapeTargets(new[]{saved.Target}).TryGetValue(saved.Target,out targetNative)||targetNative==null)
                    throw new InvalidOperationException("Surviving dynamic body native shape checkpoint is missing: "+entityId);
                RequireSameNativeShapeIdentity(saved.NativeShapes,targetNative,entityId);
                // The permanent restore path already handles exact collider
                // ancestors, native PxShape pose/geometry, mass frame, and body
                // pose while pinning the core-restored motion fields.
                NativeBodyPoseCheckpoint.Restore(new[]{saved.Target});
                NativeBodyPoseCheckpoint.VerifyRestored(new[]{saved.Target});
                return;
            }
            RequireEmptyDynamicColliderSignature(rebound,entityId,"recreated");
            RequireSameDynamicSignature(saved.Signature,CaptureDynamicSignature(rebound),entityId);
            RequireSameDynamicMotion(saved.Target,rebound,entityId);

            NativeShapeCheckpoint currentNative;
            if(!owner.RequireNativeShapeTargets(currentRows).TryGetValue(rebound,out currentNative)||currentNative==null)
                throw new InvalidOperationException("Recreated dynamic body native shape checkpoint is missing: "+entityId);
            RequireEmptyNativeShapeSignature(saved.NativeShapes,entityId,"captured");
            RequireEmptyNativeShapeSignature(currentNative,entityId,"recreated");

            CopySnapshotTargets(saved.Target,rebound);
            owner.nativeShapeCheckpoints.Set(rebound,new NativeShapeCheckpoint {Body=rebound.Body,
                RigidbodyPointer=currentNative.RigidbodyPointer,Shapes=new UIntPtr[0],
                Poses=new NativeRigidPose[0],Geometries=new NativeShapeGeometry[0],
                Colliders=new Collider[0],ColliderShapes=new UIntPtr[0]});

            // Every check above is observation-only. Dispatch through the
            // permanent core so the ordinary collider-ancestor prefix, active
            // strategy gate, motion pins, receipt and postconditions all apply.
            NativeBodyPoseCheckpoint.Restore(new[]{rebound});
            NativeBodyPoseCheckpoint.VerifyRestored(new[]{rebound});
        }

        private static BodyRestoreModule RequireDynamicBodyOwner()
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null||owner.disposed||owner.registration==null||!owner.NativeShapeCaptureActive
                ||!owner.ColliderAncestorPoseRestoreActive)
                throw new InvalidOperationException("Dynamic body restore requires the active body restore module.");
            if(Thread.CurrentThread.ManagedThreadId!=owner.moduleThread)
                throw new InvalidOperationException("Dynamic body restore requires the original Unity thread.");
            // New-frame capture runs before ControllerHandler applies a pause
            // request, and RestorePlan.Complete runs before the warp's outer
            // finally re-pauses. Both are normally unpaused. The original Unity
            // thread plus active, idle strategy ownership are the lifecycle
            // gate; identity/signature/motion checks remain operation-specific.
            var status=NativeBodyPoseCheckpoint.RestoreStrategyDiagnostics as Dictionary<string,object>;
            if(status==null||Convert.ToString(status["name"])!=owner.Name||
                Convert.ToString(status["assembly"])!=owner.GetType().Assembly.FullName||Convert.ToBoolean(status["active"]))
                throw new InvalidOperationException("Dynamic body restore owner is not the active idle restore strategy.");
            return owner;
        }

        private static void RequireEmptyDynamicColliderSignature(Snapshot row,int entityId,string phase)
        {
            if(row==null||row.Colliders==null||row.Colliders.Length!=0)
                throw new InvalidOperationException("Dynamic body "+entityId+" "+phase+
                    " collider/material signature is not the supported empty proxy.");
        }

        private static void RequireEmptyNativeShapeSignature(NativeShapeCheckpoint value,int entityId,string phase)
        {
            if(value==null||value.Shapes==null||value.Poses==null||value.Geometries==null||
                value.Shapes.Length!=0||value.Poses.Length!=0||value.Geometries.Length!=0)
                throw new InvalidOperationException("Dynamic body "+entityId+" "+phase+
                    " native shape signature is not the supported empty proxy.");
        }

        private static NativeShapeCheckpoint CloneNativeShapes(NativeShapeCheckpoint value)
        {
            return new NativeShapeCheckpoint {Body=value.Body,RigidbodyPointer=value.RigidbodyPointer,
                Shapes=(UIntPtr[])value.Shapes.Clone(),Poses=(NativeRigidPose[])value.Poses.Clone(),
                Geometries=(NativeShapeGeometry[])value.Geometries.Clone(),
                Colliders=(Collider[])value.Colliders.Clone(),ColliderShapes=(UIntPtr[])value.ColliderShapes.Clone()};
        }

        private static bool SameDynamicIncarnation(Snapshot target,Snapshot current)
        {
            return target!=null&&current!=null&&target.EntityId==current.EntityId
                &&ReferenceEquals(target.Object,current.Object)&&ReferenceEquals(target.Body,current.Body)
                &&ReferenceEquals(target.Transform,current.Transform);
        }

        private static void RequireSameNativeShapeIdentity(NativeShapeCheckpoint expected,
            NativeShapeCheckpoint observed,int entityId)
        {
            if(expected==null||observed==null||!ReferenceEquals(expected.Body,observed.Body)
                ||!expected.RigidbodyPointer.Equals(observed.RigidbodyPointer)
                ||expected.Shapes==null||observed.Shapes==null
                ||!expected.Shapes.SequenceEqual(observed.Shapes))
                throw new InvalidOperationException("Surviving dynamic body native shape identity/order changed: "+entityId);
        }

        private static void RequireSameDynamicColliderStaticSignature(Shape[] expected,Shape[] observed,int entityId)
        {
            if(expected==null||observed==null||expected.Length!=observed.Length)
                throw new InvalidOperationException("Surviving dynamic body managed collider count changed: "+entityId);
            string[] referenceFields={"Collider","Object","Transform","Parent","SharedMaterial","SharedMesh"};
            string[] valueFields={"Enabled","Trigger","Active","ActiveInHierarchy","Layer","Kind","Convex"};
            for(int i=0;i<expected.Length;i++)
            {
                if(expected[i]==null||observed[i]==null)
                    throw new InvalidOperationException("Surviving dynamic body managed collider row is null: "+entityId);
                foreach(var name in referenceFields)
                    if(!ReferenceEquals(ShapeField(expected[i],name),ShapeField(observed[i],name)))
                        throw new InvalidOperationException("Surviving dynamic body collider "+name+" changed: "+entityId+" index "+i);
                foreach(var name in valueFields)
                    if(!object.Equals(ShapeField(expected[i],name),ShapeField(observed[i],name)))
                        throw new InvalidOperationException("Surviving dynamic body collider "+name+" changed: "+entityId+" index "+i);
                if(!((float[])ShapeField(expected[i],"Geometry")).SequenceEqual((float[])ShapeField(observed[i],"Geometry"))
                    ||!((Transform[])ShapeField(expected[i],"Ancestors")).SequenceEqual((Transform[])ShapeField(observed[i],"Ancestors"))
                    ||!((Transform[])ShapeField(expected[i],"AncestorParents")).SequenceEqual((Transform[])ShapeField(observed[i],"AncestorParents"))
                    ||!((bool[])ShapeField(expected[i],"AncestorActive")).SequenceEqual((bool[])ShapeField(observed[i],"AncestorActive")))
                    throw new InvalidOperationException("Surviving dynamic body collider geometry/hierarchy changed: "+entityId+" index "+i);
                var expectedScales=(Vector3[])ShapeField(expected[i],"AncestorScales");
                var observedScales=(Vector3[])ShapeField(observed[i],"AncestorScales");
                if(expectedScales.Length!=observedScales.Length)
                    throw new InvalidOperationException("Surviving dynamic body collider ancestor scale topology changed: "+entityId+" index "+i);
                if(expectedScales.Length==0||!DynamicSame(expectedScales[expectedScales.Length-1],observedScales[observedScales.Length-1]))
                    throw new InvalidOperationException("Surviving dynamic body Rigidbody-root scale changed: "+entityId+" index "+i);
                // Ancestor scale is checkpointed pose, not immutable identity.
                // Attachment reparenting can preserve world scale while leaving a
                // slightly different local-scale preimage on the exact same
                // Transform chain. ColliderAncestorPoseRestore writes the saved
                // non-root scales before the body/native-shape restore, and the
                // RequireRestored/VerifyRestored postconditions require them
                // byte-exact. The final ancestor is the Rigidbody root, is not
                // written by that restore, and therefore remains an exact
                // precondition here. Identity, parents, active bits, analytic
                // geometry, material, and native PxShape identity/order were all
                // proven above and remain non-writable preconditions.
            }
        }

        private static object ShapeField(Shape value,string name)
        {
            var field=typeof(Shape).GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Frozen dynamic collider checkpoint field differs: "+name);
            return field.GetValue(value);
        }

        private static DynamicBodySignature CaptureDynamicSignature(Snapshot row)
        {
            if(row==null||row.Body==null||row.Transform==null||row.Object==null)
                throw new InvalidOperationException("Dynamic body signature has a null root.");
            return new DynamicBodySignature {RootName=row.Transform.name,
                RootComponents=ComponentSignature(row.Object),Parents=ParentSignature(row.Transform.parent)};
        }

        private static DynamicParentSignature[] ParentSignature(Transform parent)
        {
            var result=new List<DynamicParentSignature>();
            for(var cursor=parent;cursor!=null;cursor=cursor.parent)
            {
                if(result.Count>=64)throw new InvalidOperationException("Dynamic body parent hierarchy exceeds 64 nodes.");
                var entry=EntitySerialisationRegistry.GetEntry(cursor.gameObject);
                result.Add(new DynamicParentSignature {Name=cursor.name,Components=ComponentSignature(cursor.gameObject),
                    RegisteredEntityId=entry==null?(int?)null:(int)entry.m_Header.m_uEntityID});
            }
            return result.ToArray();
        }

        private static string[] ComponentSignature(GameObject obj)
        {
            if(obj==null)throw new InvalidOperationException("Dynamic hierarchy contains a null GameObject.");
            // NativeDynamicWarpPlan adds this authoring-only path marker to the
            // recreated owner. It has no gameplay methods/state and is already
            // ignored by the WorldSync scheduler signature. No other component
            // is omitted here.
            return obj.GetComponents<Component>().Where(c=>c==null||
                    c.GetType().FullName!="SuperchargedPatch.EntityPathReferenceMarker")
                .Select(c=>c==null?"<null>":c.GetType().FullName).ToArray();
        }

        private static void RequireSameDynamicSignature(DynamicBodySignature target,
            DynamicBodySignature current,int entityId)
        {
            if(target.RootName!=current.RootName||!target.RootComponents.SequenceEqual(current.RootComponents)||
                target.Parents.Length!=current.Parents.Length)
                throw new InvalidOperationException("Dynamic body component/hierarchy signature changed: "+entityId);
            for(int i=0;i<target.Parents.Length;i++)
                if(target.Parents[i].Name!=current.Parents[i].Name||
                    !target.Parents[i].Components.SequenceEqual(current.Parents[i].Components)||
                    target.Parents[i].RegisteredEntityId!=current.Parents[i].RegisteredEntityId)
                    throw new InvalidOperationException("Dynamic body parent hierarchy changed: "+entityId+" depth "+i);
        }

        private static void RequireSameDynamicMotion(Snapshot target,Snapshot current,int entityId)
        {
            if(!DynamicSame(target.RawVelocity,current.Body.velocity)||
                !DynamicSame(target.RawAngularVelocity,current.Body.angularVelocity)||
                target.RawIsKinematic!=current.Body.isKinematic||target.RawUseGravity!=current.Body.useGravity)
                throw new InvalidOperationException("Recreated dynamic body motion/mode differs before restore: "+entityId);
        }

        private static bool DynamicSame(Vector3 a,Vector3 b)
        {return a.x==b.x&&a.y==b.y&&a.z==b.z;}

        private static void CopySnapshotTargets(Snapshot target,Snapshot current)
        {
            foreach(var name in new[]{"BodyPosition","LocalPosition","WorldPosition","RawVelocity","RawAngularVelocity",
                "LocalScale","RawIsKinematic","RawUseGravity","BodyRotation","LocalRotation","WorldRotation"})
                SetSnapshotProperty(current,name,SnapshotProperty(name).GetValue(target,null));
            SetSnapshotProperty(current,"Invariants",CloneInvariants(target.Invariants));
            SetSnapshotProperty(current,"Colliders",new NativeBodyColliderCheckpoint.Shape[0]);
        }

        private static BodyInvariants CloneInvariants(BodyInvariants value)
        {
            if(value==null)throw new InvalidOperationException("Dynamic body invariants are absent.");
            var result=new BodyInvariants();
            foreach(var property in typeof(BodyInvariants).GetProperties(BindingFlags.Instance|BindingFlags.Public))
            {
                var setter=property.GetSetMethod(true);
                if(!property.CanRead||setter==null)
                    throw new InvalidOperationException("Frozen dynamic body invariant property differs: "+property.Name);
                setter.Invoke(result,new[]{property.GetValue(value,null)});
            }
            return result;
        }

        private static PropertyInfo SnapshotProperty(string name)
        {
            var property=typeof(Snapshot).GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(property==null||property.GetSetMethod(true)==null)
                throw new InvalidOperationException("Frozen dynamic body snapshot property differs: "+name);
            return property;
        }

        private static void SetSnapshotProperty(Snapshot row,string name,object value)
        {
            SnapshotProperty(name).SetValue(row,value,null);
        }
    }
}
