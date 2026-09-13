using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SuperchargedPatch
{
    public static class NativeBodyColliderCheckpoint
    {
        public sealed class Shape
        {
            internal Collider Collider;
            internal GameObject Object;
            internal Transform Transform,Parent;
            internal PhysicMaterial SharedMaterial;
            internal bool Enabled,Trigger,Active,ActiveInHierarchy;
            internal int Layer;
            internal Vector3 LocalPosition,LocalScale;
            internal Quaternion LocalRotation;
            internal string Kind;
            internal float[] Geometry;
            internal Mesh SharedMesh;
            internal bool Convex;
            internal Transform[] Ancestors,AncestorParents;
            internal string[] AncestorNames;
            internal int[] AncestorSiblingIndices;
            internal int ComponentIndex;
            internal Vector3[] AncestorPositions,AncestorScales;
            internal Quaternion[] AncestorRotations;
            internal bool[] AncestorActive;
        }
        public static Shape[] Capture(Rigidbody body)
        {
            return body.gameObject.GetComponentsInChildren<Collider>(true).Where(c=>c.attachedRigidbody==body)
                .OrderBy(c=>c.GetInstanceID()).Select(c=>CaptureShape(c,body.transform)).ToArray();
        }
        internal static Shape[] CaptureOwnerHierarchy(Rigidbody body,Transform owner)
        {
            if(body==null||owner==null)throw new ArgumentNullException(body==null?"body":"owner");
            bool beneath=false;
            for(var cursor=owner;cursor!=null;cursor=cursor.parent)
                if(ReferenceEquals(cursor,body.transform)){beneath=true;break;}
            if(!beneath)throw new InvalidOperationException("Recreated collider owner is outside the target body hierarchy.");
            return owner.gameObject.GetComponentsInChildren<Collider>(true).OrderBy(value=>value.GetInstanceID())
                .Select(value=>CaptureShape(value,body.transform)).ToArray();
        }
        private static Shape CaptureShape(Collider collider,Transform body)
        {
            var t=collider.transform;
            var row=new Shape {Collider=collider,Object=collider.gameObject,Transform=t,Parent=t.parent,
                SharedMaterial=collider.sharedMaterial,Enabled=collider.enabled,Trigger=collider.isTrigger,Active=collider.gameObject.activeSelf,
                ActiveInHierarchy=collider.gameObject.activeInHierarchy,
                Layer=collider.gameObject.layer,LocalPosition=t.localPosition,LocalRotation=t.localRotation,LocalScale=t.localScale};
            var chain=new List<Transform>();var cursor=t;
            while(cursor!=body) {
                if(cursor==null||chain.Count>=64)throw new InvalidOperationException("Native collider is outside its bounded body hierarchy.");
                chain.Add(cursor);cursor=cursor.parent;
            }
            chain.Add(body);row.Ancestors=chain.ToArray();row.AncestorParents=chain.Select(x=>x.parent).ToArray();
            row.AncestorNames=chain.Select(x=>x.gameObject.name).ToArray();
            row.AncestorSiblingIndices=chain.Select(x=>x.GetSiblingIndex()).ToArray();
            row.ComponentIndex=Array.FindIndex(collider.gameObject.GetComponents<Collider>(),value=>ReferenceEquals(value,collider));
            if(row.ComponentIndex<0)throw new InvalidOperationException("Native collider component order is absent.");
            row.AncestorPositions=chain.Select(x=>x.localPosition).ToArray();row.AncestorRotations=chain.Select(x=>x.localRotation).ToArray();
            row.AncestorScales=chain.Select(x=>x.localScale).ToArray();
            row.AncestorActive=chain.Select(x=>x.gameObject.activeSelf).ToArray();
            var box=collider as BoxCollider;var sphere=collider as SphereCollider;var capsule=collider as CapsuleCollider;var mesh=collider as MeshCollider;
            if(box!=null){row.Kind="BoxCollider";row.Geometry=V(box.center).Concat(V(box.size)).ToArray();}
            else if(sphere!=null){row.Kind="SphereCollider";row.Geometry=V(sphere.center).Concat(new[]{sphere.radius}).ToArray();}
            else if(capsule!=null){row.Kind="CapsuleCollider";row.Geometry=V(capsule.center).Concat(new[]{capsule.radius,capsule.height,(float)capsule.direction}).ToArray();}
            else if(mesh!=null){row.Kind="MeshCollider";row.Geometry=new float[0];row.SharedMesh=mesh.sharedMesh;row.Convex=mesh.convex;}
            else throw new InvalidOperationException("Unsupported native fixed-body collider type: "+collider.GetType().FullName);
            if(row.Geometry.Concat(row.AncestorPositions.SelectMany(V)).Concat(row.AncestorScales.SelectMany(V))
                .Concat(row.AncestorRotations.SelectMany(Q)).Any(v=>float.IsNaN(v)||float.IsInfinity(v)))
                throw new InvalidOperationException("Nonfinite native collider geometry/pose.");
            return row;
        }
        public static void ValidateCaptured(Shape[] saved)
        {
            if(saved==null)throw new ArgumentNullException("saved");
            foreach(var row in saved)
                if(row==null||row.Collider==null||row.Object==null||row.Transform==null||row.Collider.gameObject!=row.Object
                    ||row.Object.transform!=row.Transform||!row.Object.GetComponents<Collider>().Contains(row.Collider)
                    ||(!ReferenceEquals(row.Parent,null)&&row.Parent==null)||row.Ancestors==null||row.AncestorParents==null
                    ||row.AncestorNames==null||row.AncestorSiblingIndices==null||row.AncestorPositions==null
                    ||row.AncestorRotations==null||row.AncestorScales==null||row.AncestorActive==null
                    ||row.Ancestors.Length==0||row.Ancestors.Length!=row.AncestorParents.Length
                    ||row.Ancestors.Length!=row.AncestorNames.Length||row.Ancestors.Length!=row.AncestorSiblingIndices.Length
                    ||row.Ancestors.Length!=row.AncestorPositions.Length||row.Ancestors.Length!=row.AncestorRotations.Length
                    ||row.Ancestors.Length!=row.AncestorScales.Length||row.Ancestors.Length!=row.AncestorActive.Length
                    ||row.ComponentIndex<0||row.Ancestors.Any(t=>t==null))
                    throw new InvalidOperationException("Captured native collider incarnation no longer exists.");
        }
        internal static bool HasAncestor(Shape shape,Transform ancestor)
        {
            if(shape==null||shape.Ancestors==null||ReferenceEquals(ancestor,null))return false;
            return shape.Ancestors.Any(value=>ReferenceEquals(value,ancestor));
        }
        internal static bool HasUniqueNonBodyAncestor(Shape shape,Transform ancestor)
        {
            if(!ValidArrays(shape)||ReferenceEquals(ancestor,null))return false;
            int count=shape.Ancestors.Count(value=>ReferenceEquals(value,ancestor));
            return count==1&&!ReferenceEquals(shape.Ancestors[shape.Ancestors.Length-1],ancestor);
        }
        internal static bool Destroyed(Shape shape)
        {
            return shape!=null&&IsDestroyedUnityWrapper(shape.Collider)
                &&IsDestroyedUnityWrapper(shape.Object)&&IsDestroyedUnityWrapper(shape.Transform);
        }
        internal static bool IsDestroyedUnityWrapper(UnityEngine.Object value)
        { return !ReferenceEquals(value,null)&&value==null; }
        internal static void RequireExactMembership(Rigidbody body,Shape[] saved)
        {
            if(body==null||saved==null)throw new ArgumentNullException(body==null?"body":"saved");
            ValidateCaptured(saved);var current=Capture(body);
            if(current.Length!=saved.Length)throw new InvalidOperationException("Native body collider survivor membership cardinality differs.");
            for(int i=0;i<saved.Length;i++)if(!ReferenceEquals(saved[i].Collider,current[i].Collider))
                throw new InvalidOperationException("Native body collider survivor membership/order differs at index "+i+".");
        }
        internal static bool SameRecreatedOwnerTopology(Shape target,Shape current,
            Transform oldOwner,Transform newOwner)
        {
            int oldIndex=OwnerIndex(target,oldOwner),newIndex=OwnerIndex(current,newOwner);
            if(!ValidArrays(target)||!ValidArrays(current)||oldIndex<0||newIndex<0||oldIndex!=newIndex||target.Kind!=current.Kind
                ||target.ComponentIndex!=current.ComponentIndex)return false;
            for(int i=0;i<=oldIndex;i++)
                if(target.AncestorNames[i]!=current.AncestorNames[i]
                    ||target.AncestorSiblingIndices[i]!=current.AncestorSiblingIndices[i])return false;
            return true;
        }
        internal static Shape RebindRecreatedOwnerShape(Shape target,Shape current,
            Transform oldOwner,Transform newOwner,Rigidbody body)
        {
            int oldIndex=OwnerIndex(target,oldOwner),newIndex=OwnerIndex(current,newOwner);
            if(oldIndex<0||newIndex<0||oldIndex!=newIndex||target.Ancestors.Length!=current.Ancestors.Length
                ||!SameRecreatedOwnerTopology(target,current,oldOwner,newOwner))
                throw new InvalidOperationException("Recreated attachment collider owner-relative path/type/component order differs.");
            if(!ReferenceEquals(current.Ancestors[current.Ancestors.Length-1],body.transform)
                ||current.Collider.attachedRigidbody!=body)
                throw new InvalidOperationException("Recreated attachment collider belongs to the wrong Rigidbody.");
            RequireRecreatedStaticContract(target,current,oldIndex);
            return new Shape {Collider=current.Collider,Object=current.Object,Transform=current.Transform,Parent=current.Parent,
                SharedMaterial=target.SharedMaterial,Enabled=target.Enabled,Trigger=target.Trigger,Active=target.Active,
                ActiveInHierarchy=target.ActiveInHierarchy,Layer=target.Layer,LocalPosition=target.LocalPosition,
                LocalScale=target.LocalScale,LocalRotation=target.LocalRotation,Kind=target.Kind,
                Geometry=(float[])target.Geometry.Clone(),SharedMesh=target.SharedMesh,Convex=target.Convex,
                Ancestors=(Transform[])current.Ancestors.Clone(),AncestorParents=(Transform[])current.AncestorParents.Clone(),
                AncestorNames=(string[])target.AncestorNames.Clone(),
                AncestorSiblingIndices=(int[])target.AncestorSiblingIndices.Clone(),ComponentIndex=target.ComponentIndex,
                AncestorPositions=(Vector3[])target.AncestorPositions.Clone(),AncestorScales=(Vector3[])target.AncestorScales.Clone(),
                AncestorRotations=(Quaternion[])target.AncestorRotations.Clone(),AncestorActive=(bool[])target.AncestorActive.Clone()};
        }
        private sealed class RecreatedPoseTarget
        {
            internal Transform Transform,Parent;
            internal Vector3 Position,Scale;
            internal Quaternion Rotation;
            internal int Depth;
        }
        internal static bool RestoreRecreatedOwnerPoses(IEnumerable<KeyValuePair<Shape,Shape>> mappings,
            Transform oldOwner,Transform newOwner,Rigidbody body)
        {
            if(mappings==null||ReferenceEquals(oldOwner,null)||newOwner==null||body==null)
                throw new ArgumentNullException("recreated collider pose mapping");
            var rows=mappings.ToArray();
            if(rows.Length==0)throw new InvalidOperationException("Recreated attachment has no collider pose mappings.");
            var targets=new Dictionary<Transform,RecreatedPoseTarget>();
            foreach(var pair in rows)
            {
                var target=pair.Key;var current=pair.Value;
                int oldIndex=OwnerIndex(target,oldOwner),newIndex=OwnerIndex(current,newOwner);
                if(oldIndex<0||newIndex<0||oldIndex!=newIndex||target.Ancestors.Length!=current.Ancestors.Length
                    ||!SameRecreatedOwnerTopology(target,current,oldOwner,newOwner)
                    ||!ReferenceEquals(current.Ancestors[current.Ancestors.Length-1],body.transform))
                    throw new InvalidOperationException("Recreated attachment collider pose topology differs.");
                RequireRecreatedStaticContract(target,current,oldIndex);
                for(int i=0;i<target.Ancestors.Length-1;i++)
                {
                    var value=new RecreatedPoseTarget {Transform=current.Ancestors[i],Parent=current.AncestorParents[i],
                        Position=target.AncestorPositions[i],Rotation=target.AncestorRotations[i],
                        Scale=target.AncestorScales[i],Depth=i};
                    RecreatedPoseTarget prior;
                    if(targets.TryGetValue(value.Transform,out prior))
                    {
                        if(!ReferenceEquals(prior.Parent,value.Parent)||!Same(prior.Position,value.Position)
                            ||!Same(prior.Rotation,value.Rotation)||!Same(prior.Scale,value.Scale))
                            throw new InvalidOperationException("Recreated attachment collider poses conflict on a shared ancestor.");
                        if(value.Depth>prior.Depth)prior.Depth=value.Depth;
                    }
                    else targets.Add(value.Transform,value);
                }
            }
            foreach(var value in targets.Values)
                if(value.Transform==null||!ReferenceEquals(value.Transform.parent,value.Parent))
                    throw new InvalidOperationException("Recreated attachment collider parent changed before pose restoration.");
            bool changed=false;
            foreach(var value in targets.Values.OrderByDescending(item=>item.Depth))
            {
                if(!Same(value.Transform.localScale,value.Scale)){value.Transform.localScale=value.Scale;changed=true;}
                if(!Same(value.Transform.localPosition,value.Position)){value.Transform.localPosition=value.Position;changed=true;}
                if(!Same(value.Transform.localRotation,value.Rotation)){value.Transform.localRotation=value.Rotation;changed=true;}
            }
            foreach(var value in targets.Values)
                if(!Same(value.Transform.localPosition,value.Position)||!Same(value.Transform.localRotation,value.Rotation)
                    ||!Same(value.Transform.localScale,value.Scale))
                    throw new InvalidOperationException("Recreated attachment collider pose setter did not read back exactly.");
            return changed;
        }
        private static void RequireRecreatedStaticContract(Shape target,Shape current,int oldOwnerIndex)
        {
            for(int i=oldOwnerIndex+1;i<target.Ancestors.Length;i++)
                if(!ReferenceEquals(target.Ancestors[i],current.Ancestors[i])
                    ||!ReferenceEquals(target.AncestorParents[i],current.AncestorParents[i]))
                    throw new InvalidOperationException("Recreated attachment collider changed surviving ancestor topology.");
            if(target.SharedMaterial!=current.SharedMaterial||target.Enabled!=current.Enabled
                ||target.Trigger!=current.Trigger||target.Active!=current.Active
                ||target.ActiveInHierarchy!=current.ActiveInHierarchy||target.Layer!=current.Layer
                ||target.Kind!=current.Kind||!target.Geometry.SequenceEqual(current.Geometry)
                ||target.SharedMesh!=current.SharedMesh||target.Convex!=current.Convex
                ||!target.AncestorActive.SequenceEqual(current.AncestorActive))
                throw new InvalidOperationException("Recreated attachment collider geometry/material/flags differ.");
        }
        private static int OwnerIndex(Shape shape,Transform owner)
        {
            if(shape==null||shape.Ancestors==null||ReferenceEquals(owner,null))return -1;
            int result=-1;
            for(int i=0;i<shape.Ancestors.Length;i++)if(ReferenceEquals(shape.Ancestors[i],owner))
            {if(result>=0)return -1;result=i;}
            return result;
        }
        private static bool ValidArrays(Shape shape)
        {
            if(shape==null||shape.Ancestors==null||shape.AncestorParents==null||shape.AncestorNames==null
                ||shape.AncestorSiblingIndices==null||shape.AncestorPositions==null||shape.AncestorRotations==null
                ||shape.AncestorScales==null||shape.AncestorActive==null||shape.Geometry==null||shape.Ancestors.Length==0)
                return false;
            int count=shape.Ancestors.Length;
            return shape.AncestorParents.Length==count&&shape.AncestorNames.Length==count
                &&shape.AncestorSiblingIndices.Length==count&&shape.AncestorPositions.Length==count
                &&shape.AncestorRotations.Length==count&&shape.AncestorScales.Length==count
                &&shape.AncestorActive.Length==count&&shape.ComponentIndex>=0;
        }
        public static void RequireRestored(Rigidbody body,Shape[] saved,bool includeBodyPose)
        {
            ValidateCaptured(saved);var current=Capture(body);
            if(current.Length!=saved.Length)throw new InvalidOperationException("Restored native body collider membership differs.");
            for(int i=0;i<saved.Length;i++) {
                var a=saved[i];var b=current[i];
                if(a.Collider!=b.Collider||a.Parent!=b.Parent||a.SharedMaterial!=b.SharedMaterial||a.Enabled!=b.Enabled||a.Trigger!=b.Trigger
                    ||a.Active!=b.Active||a.ActiveInHierarchy!=b.ActiveInHierarchy||a.Layer!=b.Layer||a.Kind!=b.Kind||!a.Geometry.SequenceEqual(b.Geometry)
                    ||a.SharedMesh!=b.SharedMesh||a.Convex!=b.Convex||!V(a.LocalScale).SequenceEqual(V(b.LocalScale))
                    ||((includeBodyPose||a.Transform!=body.transform)&&(!V(a.LocalPosition).SequenceEqual(V(b.LocalPosition))||!Q(a.LocalRotation).SequenceEqual(Q(b.LocalRotation)))))
                    throw new InvalidOperationException("Restored native collider geometry/parenting differs: "+a.Collider.GetInstanceID());
                if(!a.Ancestors.SequenceEqual(b.Ancestors)||!a.AncestorParents.SequenceEqual(b.AncestorParents)||!a.AncestorActive.SequenceEqual(b.AncestorActive))
                    throw new InvalidOperationException("Restored native collider ancestor identity differs.");
                for(int j=0;j<a.Ancestors.Length;j++)
                    if(!V(a.AncestorScales[j]).SequenceEqual(V(b.AncestorScales[j]))
                        ||((includeBodyPose||a.Ancestors[j]!=body.transform)
                            &&(!V(a.AncestorPositions[j]).SequenceEqual(V(b.AncestorPositions[j]))
                                ||!Q(a.AncestorRotations[j]).SequenceEqual(Q(b.AncestorRotations[j])))))
                        throw new InvalidOperationException("Restored native collider ancestor pose differs.");
            }
        }
        public static object Diagnostics(Shape[] saved)
        {
            return saved.Select(r=>(object)new Dictionary<string,object>{{"colliderInstanceId",r.Collider.GetInstanceID()},
                {"objectInstanceId",r.Object.GetInstanceID()},{"parentInstanceId",r.Parent==null?(object)null:r.Parent.GetInstanceID()},
                {"kind",r.Kind},{"geometry",r.Geometry},{"localPosition",V(r.LocalPosition)},{"localRotation",Q(r.LocalRotation)},
                {"localScale",V(r.LocalScale)},{"enabled",r.Enabled},{"trigger",r.Trigger},{"activeSelf",r.Active},
                {"activeInHierarchy",r.ActiveInHierarchy},{"layer",r.Layer},
                {"sharedMaterialInstanceId",r.SharedMaterial==null?(object)null:r.SharedMaterial.GetInstanceID()},
                {"sharedMeshInstanceId",r.SharedMesh==null?(object)null:r.SharedMesh.GetInstanceID()},{"convex",r.Convex},
                {"ancestorIds",r.Ancestors.Select(t=>t.GetInstanceID()).ToArray()},
                {"ancestorNames",r.AncestorNames},{"ancestorSiblingIndices",r.AncestorSiblingIndices},
                {"componentIndex",r.ComponentIndex},
                {"ancestorLocalPositions",r.AncestorPositions.Select(V).ToArray()},
                {"ancestorLocalRotations",r.AncestorRotations.Select(Q).ToArray()},
                {"ancestorActiveSelf",r.AncestorActive},
                {"ancestorLocalScales",r.AncestorScales.Select(V).ToArray()}}).ToArray();
        }
        private static float[] V(Vector3 v){return new[]{v.x,v.y,v.z};}
        private static float[] Q(Quaternion q){return new[]{q.x,q.y,q.z,q.w};}
        private static bool Same(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        private static bool Same(Quaternion a,Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}
    }
}
