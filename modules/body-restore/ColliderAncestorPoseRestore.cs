using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using SuperchargedPatch;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;
using Shape=SuperchargedPatch.NativeBodyColliderCheckpoint.Shape;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class BodyRestoreModule
    {
        private sealed class ColliderAncestorTarget
        {
            internal Transform Transform,Parent;
            internal Vector3 Position,Scale;
            internal Quaternion Rotation;
            internal int EntityId,ColliderId;
        }

        private sealed class ColliderEnabledTarget
        {
            internal Collider Collider;
            internal bool Enabled;
            internal int EntityId;
        }

        private static BodyRestoreModule colliderAncestorPoseRestoreOwner;
        private Harmony colliderAncestorPoseRestoreHarmony;
        private readonly List<object> colliderAncestorPoseRestores=new List<object>();
        private FieldInfo shapeCollider,shapeEnabled,shapeAncestors,shapeAncestorParents,shapeAncestorPositions,shapeAncestorRotations,shapeAncestorScales;

        private bool ColliderAncestorPoseRestoreActive
        {
            get {return colliderAncestorPoseRestoreHarmony!=null&&ReferenceEquals(colliderAncestorPoseRestoreOwner,this);}
        }

        private void ActivateColliderAncestorPoseRestore()
        {
            if(colliderAncestorPoseRestoreHarmony!=null)return;
            if(colliderAncestorPoseRestoreOwner!=null)
                throw new InvalidOperationException("Another collider-ancestor pose restore module is active.");
            shapeCollider=RequireShapeField("Collider",typeof(Collider));
            shapeEnabled=RequireShapeField("Enabled",typeof(bool));
            shapeAncestors=RequireShapeField("Ancestors",typeof(Transform[]));
            shapeAncestorParents=RequireShapeField("AncestorParents",typeof(Transform[]));
            shapeAncestorPositions=RequireShapeField("AncestorPositions",typeof(Vector3[]));
            shapeAncestorRotations=RequireShapeField("AncestorRotations",typeof(Quaternion[]));
            shapeAncestorScales=RequireShapeField("AncestorScales",typeof(Vector3[]));
            var restore=AccessTools.DeclaredMethod(typeof(NativeBodyPoseCheckpoint),"Restore",new[]{typeof(Snapshot[])});
            var prefix=GetType().GetMethod("BeforeNativeBodyRestore",BindingFlags.Public|BindingFlags.Static);
            if(restore==null||restore.ReturnType!=typeof(void)||prefix==null)
                throw new InvalidOperationException("Frozen native-body restore contract differs.");
            colliderAncestorPoseRestoreHarmony=new Harmony("supercharged.authoring.body-collider-ancestor-pose."+
                GetType().Assembly.GetName().Name);
            colliderAncestorPoseRestoreOwner=this;
            try {colliderAncestorPoseRestoreHarmony.Patch(restore,prefix:new HarmonyMethod(prefix));}
            catch {DeactivateColliderAncestorPoseRestore();throw;}
        }

        private static FieldInfo RequireShapeField(string name,Type type)
        {
            var field=AccessTools.Field(typeof(Shape),name);
            if(field==null||field.FieldType!=type)
                throw new InvalidOperationException("Frozen collider-shape checkpoint field differs: "+name);
            return field;
        }

        private void DeactivateColliderAncestorPoseRestore()
        {
            if(colliderAncestorPoseRestoreHarmony!=null)colliderAncestorPoseRestoreHarmony.UnpatchSelf();
            colliderAncestorPoseRestoreHarmony=null;
            if(ReferenceEquals(colliderAncestorPoseRestoreOwner,this))colliderAncestorPoseRestoreOwner=null;
            shapeCollider=null;shapeEnabled=null;shapeAncestors=null;shapeAncestorParents=null;shapeAncestorPositions=null;
            shapeAncestorRotations=null;shapeAncestorScales=null;
        }

        public static void BeforeNativeBodyRestore(Snapshot[] __0)
        {
            var owner=colliderAncestorPoseRestoreOwner;
            if(owner!=null)owner.RestoreColliderAncestorPoses(__0);
        }

        private void RestoreColliderAncestorPoses(Snapshot[] saved)
        {
            var receipt=new Dictionary<string,object>{{"bodyCount",saved==null?-1:saved.Length},{"verified",false}};
            colliderAncestorPoseRestores.Add(receipt);
            if(colliderAncestorPoseRestores.Count>32)colliderAncestorPoseRestores.RemoveAt(0);
            try
            {
                if(saved==null)throw new ArgumentNullException("saved");
                NativeBodyPoseCheckpoint.Validate(saved);
                var ordered=new List<ColliderAncestorTarget>();
                var unique=new Dictionary<Transform,ColliderAncestorTarget>();
                var colliderEnabledTargets=new List<ColliderEnabledTarget>();
                var uniqueColliders=new Dictionary<Collider,ColliderEnabledTarget>();
                var enabledAdds=new Dictionary<int,List<Collider>>();
                foreach(var row in saved)
                {
                    if(row==null||row.Body==null)throw new InvalidOperationException("Collider-ancestor restore has a null body checkpoint.");
                    Shape[] shapes=row.Colliders;
                    NativeBodyColliderCheckpoint.ValidateCaptured(shapes);
                    foreach(var shape in shapes)
                    {
                        var collider=(Collider)shapeCollider.GetValue(shape);
                        var ancestors=(Transform[])shapeAncestors.GetValue(shape);
                        var parents=(Transform[])shapeAncestorParents.GetValue(shape);
                        var positions=(Vector3[])shapeAncestorPositions.GetValue(shape);
                        var rotations=(Quaternion[])shapeAncestorRotations.GetValue(shape);
                        var scales=(Vector3[])shapeAncestorScales.GetValue(shape);
                        if(ancestors==null||parents==null||positions==null||rotations==null||scales==null||
                            ancestors.Length!=parents.Length||ancestors.Length!=positions.Length||
                            ancestors.Length!=rotations.Length||ancestors.Length!=scales.Length||ancestors.Length==0||
                            !ReferenceEquals(ancestors[ancestors.Length-1],row.Body.transform)||collider==null)
                            throw new InvalidOperationException("Collider-ancestor checkpoint arrays differ from the frozen contract.");
                        var enabledTarget=new ColliderEnabledTarget {Collider=collider,
                            Enabled=(bool)shapeEnabled.GetValue(shape),EntityId=row.EntityId};
                        ColliderEnabledTarget priorEnabled;
                        if(uniqueColliders.TryGetValue(collider,out priorEnabled))
                        {
                            if(priorEnabled.Enabled!=enabledTarget.Enabled)
                                throw new InvalidOperationException("Conflicting saved collider enabled state: "+collider.GetInstanceID());
                        }
                        else
                        {
                            uniqueColliders.Add(collider,enabledTarget);
                            colliderEnabledTargets.Add(enabledTarget);
                        }
                        // The chain is stored collider-to-body. Install parents before
                        // children, while leaving the Rigidbody root to the body restorer.
                        for(int i=ancestors.Length-2;i>=0;i--)
                        {
                            var target=new ColliderAncestorTarget {Transform=ancestors[i],Parent=parents[i],
                                Position=positions[i],Rotation=rotations[i],Scale=scales[i],
                                EntityId=row.EntityId,ColliderId=collider.GetInstanceID()};
                            ColliderAncestorTarget prior;
                            if(unique.TryGetValue(target.Transform,out prior))
                            {
                                if(!ReferenceEquals(prior.Parent,target.Parent)||!SameExact(prior.Position,target.Position)||
                                    !SameExact(prior.Rotation,target.Rotation)||!SameExact(prior.Scale,target.Scale))
                                    throw new InvalidOperationException("Conflicting saved collider-ancestor poses: "+target.Transform.GetInstanceID());
                                continue;
                            }
                            unique.Add(target.Transform,target);ordered.Add(target);
                        }
                    }
                }
                foreach(var target in ordered)
                    if(target.Transform==null||!ReferenceEquals(target.Transform.parent,target.Parent)||
                        !FiniteExact(target.Position)||!FiniteExact(target.Rotation)||!FiniteExact(target.Scale))
                        throw new InvalidOperationException("Collider-ancestor incarnation or target pose changed.");
                var mutations=new List<object>();
                foreach(var target in ordered)
                {
                    Vector3 beforePosition=target.Transform.localPosition,beforeScale=target.Transform.localScale;
                    Quaternion beforeRotation=target.Transform.localRotation;
                    bool changed=!SameExact(beforePosition,target.Position)||!SameExact(beforeRotation,target.Rotation)||
                        !SameExact(beforeScale,target.Scale);
                    if(!changed)continue;
                    if(!SameExact(beforeScale,target.Scale))target.Transform.localScale=target.Scale;
                    if(!SameExact(beforePosition,target.Position))target.Transform.localPosition=target.Position;
                    if(!SameExact(beforeRotation,target.Rotation))target.Transform.localRotation=target.Rotation;
                    if(!SameExact(target.Transform.localPosition,target.Position)||
                        !SameExact(target.Transform.localRotation,target.Rotation)||
                        !SameExact(target.Transform.localScale,target.Scale))
                        throw new InvalidOperationException("Collider-ancestor pose setter did not read back exactly: "+target.Transform.GetInstanceID());
                    mutations.Add(new Dictionary<string,object>{{"entityId",target.EntityId},{"colliderInstanceId",target.ColliderId},
                        {"transformInstanceId",target.Transform.GetInstanceID()},{"path",PathOfAncestor(target.Transform)},
                        {"beforePosition",PointExact(beforePosition)},{"targetPosition",PointExact(target.Position)},
                        {"beforeRotation",RotationExact(beforeRotation)},{"targetRotation",RotationExact(target.Rotation)},
                        {"beforeScale",PointExact(beforeScale)},{"targetScale",PointExact(target.Scale)}});
                }
                // Reparenting a disabled Collider does not register it with the
                // Rigidbody at its new ancestor. Restore the checkpoint's exact
                // enabled bit only after the saved hierarchy and local poses are
                // installed, so Unity creates/removes the shape under the target
                // actor rather than the post-checkpoint actor.
                var enabledMutations=new List<object>();
                foreach(var target in colliderEnabledTargets)
                {
                    if(target.Collider==null||target.Collider.GetInstanceID()==0)
                        throw new InvalidOperationException("Collider incarnation changed before enabled-state restore.");
                    bool before=target.Collider.enabled;
                    if(before==target.Enabled)continue;
                    target.Collider.enabled=target.Enabled;
                    if(target.Collider.enabled!=target.Enabled)
                        throw new InvalidOperationException("Collider enabled setter did not read back exactly: "+target.Collider.GetInstanceID());
                    if(!before&&target.Enabled)
                    {
                        List<Collider> additions;
                        if(!enabledAdds.TryGetValue(target.EntityId,out additions))
                        {additions=new List<Collider>();enabledAdds.Add(target.EntityId,additions);}
                        additions.Add(target.Collider);
                    }
                    var attached=target.Collider.attachedRigidbody;
                    enabledMutations.Add(new Dictionary<string,object>{{"entityId",target.EntityId},
                        {"colliderInstanceId",target.Collider.GetInstanceID()},{"before",before},{"target",target.Enabled},
                        {"attachedRigidbodyInstanceId",attached==null?(object)null:attached.GetInstanceID()}});
                }
                // Reparenting a PhysicalAttachment can update one Collider's
                // cached Rigidbody owner immediately while leaving a sibling
                // trigger on the same Transform pending until Unity flushes
                // Transform changes to PhysX.  Only flush when the captured
                // collider identities do not yet belong to their target body;
                // the strict postcondition below still rejects an incorrect
                // hierarchy, geometry, pose, or owner after the flush.
                var unsynchronisedBodies=new List<int>();
                foreach(var row in saved)
                    if(!SameColliderMembership(row.Body,row.Colliders))
                        unsynchronisedBodies.Add(row.EntityId);
                receipt["nativeTransformSyncRequired"]=unsynchronisedBodies.Count!=0;
                receipt["nativeTransformSyncBodies"]=unsynchronisedBodies.ToArray();
                if(unsynchronisedBodies.Count!=0)Physics.SyncTransforms();
                var remainingUnsynchronisedBodies=new List<int>();
                foreach(var row in saved)
                    if(!SameColliderMembership(row.Body,row.Colliders))
                        remainingUnsynchronisedBodies.Add(row.EntityId);
                receipt["nativeTransformSyncRemainingBodies"]=remainingUnsynchronisedBodies.ToArray();
                foreach(var row in saved)
                    NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,false);
                var nativeOrderCorrections=CorrectReenabledNativeShapeOrder(saved,enabledAdds);
                foreach(var row in saved)
                    NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,false);
                receipt["targetCount"]=ordered.Count;receipt["mutationCount"]=mutations.Count;
                receipt["mutations"]=mutations.ToArray();
                receipt["colliderEnabledTargetCount"]=colliderEnabledTargets.Count;
                receipt["colliderEnabledMutationCount"]=enabledMutations.Count;
                receipt["colliderEnabledMutations"]=enabledMutations.ToArray();
                receipt["nativeShapeOrderCorrections"]=nativeOrderCorrections;receipt["verified"]=true;
            }
            catch(Exception error){receipt["error"]=error.ToString();throw;}
        }

        private bool SameColliderMembership(Rigidbody body,Shape[] saved)
        {
            var current=NativeBodyColliderCheckpoint.Capture(body);
            if(current.Length!=saved.Length)return false;
            for(int i=0;i<saved.Length;i++)
                if(!ReferenceEquals(shapeCollider.GetValue(saved[i]),shapeCollider.GetValue(current[i])))return false;
            return true;
        }

        private static string PathOfAncestor(Transform value)
        {
            var names=new List<string>();
            for(var cursor=value;cursor!=null;cursor=cursor.parent)names.Add(cursor.name);
            names.Reverse();return string.Join("/",names.ToArray());
        }
        private static object PointExact(Vector3 value){return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z}};}
        private static object RotationExact(Quaternion value){return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},{"w",value.w}};}
        private static bool SameExact(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        private static bool SameExact(Quaternion a,Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}
        private static bool FiniteExact(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        private static bool FiniteExact(Vector3 value){return FiniteExact(value.x)&&FiniteExact(value.y)&&FiniteExact(value.z);}
        private static bool FiniteExact(Quaternion value){return FiniteExact(value.x)&&FiniteExact(value.y)&&FiniteExact(value.z)&&FiniteExact(value.w);}
    }
}
