using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;
using Shape=SuperchargedPatch.NativeBodyColliderCheckpoint.Shape;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class BodyRestoreModule
    {
        private const int MaximumNativeShapePoses=64;

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeShapePoseRestoreReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor;
            internal uint Count,PoseChanged,GeometryChanged,ExpectedOrderHash,ObservedOrderHash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeShapeGeometry
        {
            internal uint Type;
            internal float Value0,Value1,Value2;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeCaptureShapePoses(UIntPtr unityBase,UIntPtr rigidbody,
            IntPtr shapes,IntPtr poses,IntPtr geometries,uint capacity,out uint count,out uint error);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NativeRestoreShapePoses(UIntPtr unityBase,UIntPtr rigidbody,
            IntPtr shapes,IntPtr poses,IntPtr geometries,uint count,out NativeShapePoseRestoreReceipt receipt);

        private sealed class NativeShapeCheckpoint
        {
            internal Rigidbody Body;
            internal UIntPtr RigidbodyPointer;
            internal UIntPtr[] Shapes;
            internal NativeRigidPose[] Poses;
            internal NativeShapeGeometry[] Geometries;
            internal Collider[] Colliders;
            internal UIntPtr[] ColliderShapes;
        }

        private sealed class WeakShapeEntry
        {
            internal WeakReference Snapshot;
            internal NativeShapeCheckpoint Checkpoint;
        }

        private sealed class PendingRecreatedShape
        {
            internal Shape HistoricalShape,CurrentShape;
            internal Collider HistoricalCollider,CurrentCollider;
        }

        // The Unity 2017 managed profile has no ConditionalWeakTable. Keep weak
        // keys explicitly so checkpoint-history eviction also releases sidecars.
        private sealed class WeakShapeTable
        {
            private readonly Dictionary<int,List<WeakShapeEntry>> buckets=
                new Dictionary<int,List<WeakShapeEntry>>();

            internal void Set(Snapshot snapshot,NativeShapeCheckpoint checkpoint)
            {
                int key=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(snapshot);
                List<WeakShapeEntry> entries;
                if(!buckets.TryGetValue(key,out entries)) {
                    entries=new List<WeakShapeEntry>();
                    buckets.Add(key,entries);
                }
                for(int i=entries.Count-1;i>=0;i--) {
                    object target=entries[i].Snapshot.Target;
                    if(target==null)entries.RemoveAt(i);
                    else if(ReferenceEquals(target,snapshot)) {
                        entries[i].Checkpoint=checkpoint;
                        return;
                    }
                }
                entries.Add(new WeakShapeEntry {Snapshot=new WeakReference(snapshot),Checkpoint=checkpoint});
            }

            internal bool TryGetValue(Snapshot snapshot,out NativeShapeCheckpoint checkpoint)
            {
                int key=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(snapshot);
                List<WeakShapeEntry> entries;
                if(buckets.TryGetValue(key,out entries)) {
                    for(int i=entries.Count-1;i>=0;i--) {
                        object target=entries[i].Snapshot.Target;
                        if(target==null)entries.RemoveAt(i);
                        else if(ReferenceEquals(target,snapshot)) {
                            checkpoint=entries[i].Checkpoint;
                            return true;
                        }
                    }
                    if(entries.Count==0)buckets.Remove(key);
                }
                checkpoint=null;
                return false;
            }

            internal void Clear(){buckets.Clear();}
        }

        private static BodyRestoreModule nativeShapeCaptureOwner;
        private Harmony nativeShapeCaptureHarmony;
        private readonly WeakShapeTable nativeShapeCheckpoints=new WeakShapeTable();
        private NativeCaptureShapePoses nativeCaptureShapePoses;
        private NativeRestoreShapePoses nativeRestoreShapePoses;
        private readonly List<object> nativeShapePoseRestores=new List<object>();
        private readonly List<object> nativeShapeTopologyMismatches=new List<object>();
        private readonly List<object> nativeShapeGeometryRebinds=new List<object>();
        private readonly List<PendingRecreatedShape> pendingRecreatedShapes=new List<PendingRecreatedShape>();
        private long nativeShapePoseCaptures;
        private string nativeShapePoseCaptureFailure;
        private bool nativeShapeGeometryRebindPoisoned;
        private string nativeShapeGeometryRebindFailure;

        private bool NativeShapeCaptureActive
        {
            get {return nativeShapeCaptureHarmony!=null&&ReferenceEquals(nativeShapeCaptureOwner,this);}
        }

        private void ActivateNativeShapeCapture()
        {
            if(nativeShapeCaptureHarmony!=null)return;
            if(nativeCaptureShapePoses==null||nativeRestoreShapePoses==null||cachedPtr==null)
                throw new InvalidOperationException("Native shape-state capture requires the version-6 native helper.");
            if(nativeShapeCaptureOwner!=null)
                throw new InvalidOperationException("Another native shape-pose capture module is active.");
            var capture=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "Capture",new[]{typeof(HashSet<int>)});
            var postfix=GetType().GetMethod("AfterNativeBodyCapture",BindingFlags.Public|BindingFlags.Static);
            var rebindShape=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyColliderCheckpoint),
                "RebindRecreatedOwnerShape",new[]{typeof(Shape),typeof(Shape),typeof(Transform),typeof(Transform),typeof(Rigidbody)});
            var rebindShapePostfix=GetType().GetMethod("AfterRebindRecreatedOwnerShape",BindingFlags.Public|BindingFlags.Static);
            var rebindBody=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "RebindSurvivingWithRecreatedColliders",new[]{typeof(Snapshot),typeof(Snapshot),typeof(Shape[])});
            var rebindBodyPostfix=GetType().GetMethod("AfterRebindSurvivingWithRecreatedColliders",BindingFlags.Public|BindingFlags.Static);
            if(capture==null||capture.ReturnType!=typeof(Snapshot[])||postfix==null
                ||rebindShape==null||rebindShape.ReturnType!=typeof(Shape)||rebindShapePostfix==null
                ||rebindBody==null||rebindBody.ReturnType!=typeof(Snapshot)||rebindBodyPostfix==null)
                throw new InvalidOperationException("Frozen native-body checkpoint capture contract differs.");
            nativeShapeCaptureHarmony=new Harmony("supercharged.authoring.body-native-shape-pose."+
                GetType().Assembly.GetName().Name);
            nativeShapeCaptureOwner=this;
            pendingRecreatedShapes.Clear();nativeShapeGeometryRebindPoisoned=false;nativeShapeGeometryRebindFailure=null;
            try {
                nativeShapeCaptureHarmony.Patch(capture,postfix:new HarmonyMethod(postfix));
                nativeShapeCaptureHarmony.Patch(rebindShape,postfix:new HarmonyMethod(rebindShapePostfix));
                nativeShapeCaptureHarmony.Patch(rebindBody,postfix:new HarmonyMethod(rebindBodyPostfix));
            }
            catch {DeactivateNativeShapeCapture();throw;}
        }

        private void DeactivateNativeShapeCapture()
        {
            if(nativeShapeCaptureHarmony!=null)nativeShapeCaptureHarmony.UnpatchSelf();
            nativeShapeCaptureHarmony=null;
            if(ReferenceEquals(nativeShapeCaptureOwner,this))nativeShapeCaptureOwner=null;
            nativeShapeCheckpoints.Clear();
            pendingRecreatedShapes.Clear();nativeShapeGeometryRebindPoisoned=false;nativeShapeGeometryRebindFailure=null;
        }

        public static void AfterNativeBodyCapture(Snapshot[] __result)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            try {owner.CaptureNativeShapeTargets(__result);}
            catch(Exception error) {
                // Observation must not change ordinary gameplay. A snapshot that
                // lacks this sidecar is rejected before any later rewind mutation.
                owner.nativeShapePoseCaptureFailure=error.ToString();
            }
        }

        public static void AfterRebindRecreatedOwnerShape(Shape __0,Shape __1,Transform __2,
            Transform __3,Rigidbody __4,Shape __result)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner!=null)owner.RecordRecreatedShape(__0,__1,__2,__3,__4,__result);
        }

        public static void AfterRebindSurvivingWithRecreatedColliders(Snapshot __0,Snapshot __1,
            Shape[] __2,Snapshot __result)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner!=null)owner.RebaseRecreatedShapeGeometry(__0,__1,__2,__result);
        }

        private void RecordRecreatedShape(Shape historical,Shape current,Transform oldOwner,
            Transform newOwner,Rigidbody body,Shape result)
        {
            try
            {
                if(nativeShapeGeometryRebindPoisoned)
                    throw new InvalidOperationException("Native shape geometry rebind is poisoned by an earlier failure.");
                if(Thread.CurrentThread.ManagedThreadId!=moduleThread||historical==null||current==null||result==null
                    ||ReferenceEquals(oldOwner,null)||ReferenceEquals(newOwner,null)||ReferenceEquals(body,null)||shapeCollider==null)
                    throw new InvalidOperationException("Native recreated shape callback lifecycle differs.");
                var historicalCollider=(Collider)shapeCollider.GetValue(historical);
                var currentCollider=(Collider)shapeCollider.GetValue(current);
                var resultCollider=(Collider)shapeCollider.GetValue(result);
                if(ReferenceEquals(historicalCollider,null)||ReferenceEquals(currentCollider,null)
                    ||ReferenceEquals(resultCollider,null)
                    ||!(historicalCollider is BoxCollider)||!(currentCollider is BoxCollider)
                    ||ReferenceEquals(historicalCollider,currentCollider)
                    ||!ReferenceEquals(currentCollider,resultCollider)
                    ||!ReferenceEquals(currentCollider.attachedRigidbody,body))
                    throw new InvalidOperationException("Native recreated shape callback identity/type differs.");
                foreach(var value in pendingRecreatedShapes)
                    if(ReferenceEquals(value.HistoricalCollider,historicalCollider)
                        ||ReferenceEquals(value.CurrentCollider,currentCollider))
                        throw new InvalidOperationException("Native recreated shape callback is not bijective.");
                if(pendingRecreatedShapes.Count>=2)
                    throw new InvalidOperationException("Native recreated shape callback exceeds the bounded two-BoxCollider scope.");
                pendingRecreatedShapes.Add(new PendingRecreatedShape {HistoricalShape=historical,CurrentShape=current,
                    HistoricalCollider=historicalCollider,CurrentCollider=currentCollider});
            }
            catch(Exception error)
            {
                nativeShapeGeometryRebindPoisoned=true;nativeShapeGeometryRebindFailure=error.ToString();throw;
            }
        }

        private void RebaseRecreatedShapeGeometry(Snapshot historical,Snapshot current,Shape[] rebound,
            Snapshot result)
        {
            var receipt=new Dictionary<string,object>{{"verified",false},{"pendingPairCount",pendingRecreatedShapes.Count}};
            nativeShapeGeometryRebinds.Add(receipt);
            if(nativeShapeGeometryRebinds.Count>32)nativeShapeGeometryRebinds.RemoveAt(0);
            try
            {
                if(nativeShapeGeometryRebindPoisoned)
                    throw new InvalidOperationException("Native shape geometry rebind is poisoned by an earlier failure.");
                if(Thread.CurrentThread.ManagedThreadId!=moduleThread||historical==null||current==null||result==null
                    ||rebound==null||!ReferenceEquals(current,result)||historical.EntityId!=current.EntityId
                    ||!ReferenceEquals(historical.Body,current.Body)||pendingRecreatedShapes.Count!=2)
                    throw new InvalidOperationException("Native shape geometry body callback lifecycle differs.");
                NativeShapeCheckpoint historicalNative,currentNative;
                if(!nativeShapeCheckpoints.TryGetValue(historical,out historicalNative)||historicalNative==null
                    ||!nativeShapeCheckpoints.TryGetValue(current,out currentNative)||currentNative==null)
                    throw new InvalidOperationException("Native shape geometry rebind sidecar is missing.");
                if(!ReferenceEquals(historicalNative.Body,currentNative.Body)
                    ||!historicalNative.RigidbodyPointer.Equals(currentNative.RigidbodyPointer)
                    ||historicalNative.Shapes==null||historicalNative.Poses==null||historicalNative.Geometries==null
                    ||historicalNative.Colliders==null||historicalNative.ColliderShapes==null
                    ||currentNative.Shapes==null||currentNative.Poses==null||currentNative.Geometries==null
                    ||currentNative.Colliders==null||currentNative.ColliderShapes==null
                    ||historicalNative.Shapes.Length==0||historicalNative.Shapes.Length>MaximumNativeShapePoses
                    ||historicalNative.Shapes.Length!=historicalNative.Poses.Length
                    ||historicalNative.Shapes.Length!=historicalNative.Geometries.Length
                    ||currentNative.Shapes.Length!=currentNative.Poses.Length
                    ||currentNative.Shapes.Length!=currentNative.Geometries.Length
                    ||historicalNative.Shapes.Length!=currentNative.Shapes.Length
                    ||rebound.Length!=currentNative.Colliders.Length)
                    throw new InvalidOperationException("Native shape geometry Rigidbody/count identity differs.");
                var reboundColliders=new Collider[rebound.Length];
                for(int i=0;i<rebound.Length;i++)
                {
                    reboundColliders[i]=(Collider)shapeCollider.GetValue(rebound[i]);
                    if(ReferenceEquals(reboundColliders[i],null)||i>=currentNative.Colliders.Length
                        ||!ReferenceEquals(reboundColliders[i],currentNative.Colliders[i]))
                        throw new InvalidOperationException("Native shape geometry rebound managed order differs.");
                }
                var pairs=new NativeShapeGeometryRebindPlan.RecreatedPair[pendingRecreatedShapes.Count];
                for(int i=0;i<pairs.Length;i++)pairs[i]=new NativeShapeGeometryRebindPlan.RecreatedPair {
                    Historical=pendingRecreatedShapes[i].HistoricalCollider,Current=pendingRecreatedShapes[i].CurrentCollider};
                var plan=NativeShapeGeometryRebindPlan.Build(ToObjects(historicalNative.Colliders),
                    historicalNative.ColliderShapes,historicalNative.Shapes,ToObjects(currentNative.Colliders),
                    currentNative.ColliderShapes,currentNative.Shapes,pairs,2);
                var poses=(NativeRigidPose[])currentNative.Poses.Clone();
                var geometries=(NativeShapeGeometry[])currentNative.Geometries.Clone();
                int differingPoseRows=0,differingGeometryRows=0,recreatedRows=0;
                for(int i=0;i<geometries.Length;i++)
                {
                    if(historicalNative.Geometries[i].Type!=currentNative.Geometries[i].Type)
                        throw new InvalidOperationException("Native shape geometry rebind type differs at actor index "+i+".");
                    if(plan.RecreatedByActorIndex[i])
                    {
                        int hi=plan.HistoricalManagedIndexByActorIndex[i],ci=plan.CurrentManagedIndexByActorIndex[i];
                        if(!(historicalNative.Colliders[hi] is BoxCollider)||!(currentNative.Colliders[ci] is BoxCollider)
                            ||ReferenceEquals(historicalNative.Colliders[hi],currentNative.Colliders[ci]))
                            throw new InvalidOperationException("Native shape geometry recreated actor role is not one BoxCollider reincarnation.");
                        recreatedRows++;
                        if(!SameBits(historicalNative.Poses[i],currentNative.Poses[i]))differingPoseRows++;
                        if(!SameBits(historicalNative.Geometries[i],currentNative.Geometries[i]))differingGeometryRows++;
                        poses[i]=historicalNative.Poses[i];
                        geometries[i]=historicalNative.Geometries[i];
                    }
                    else if(!historicalNative.Shapes[i].Equals(currentNative.Shapes[i])
                        ||!SameBits(historicalNative.Poses[i],currentNative.Poses[i])
                        ||!SameBits(historicalNative.Geometries[i],currentNative.Geometries[i]))
                        throw new InvalidOperationException("Native shape geometry surviving actor row differs at index "+i+".");
                }
                if(recreatedRows!=2)
                    throw new InvalidOperationException("Native shape geometry actor plan does not contain exactly two recreated rows.");
                var rebased=new NativeShapeCheckpoint {Body=currentNative.Body,RigidbodyPointer=currentNative.RigidbodyPointer,
                    Shapes=(UIntPtr[])currentNative.Shapes.Clone(),Poses=poses,
                    Geometries=geometries,Colliders=(Collider[])currentNative.Colliders.Clone(),
                    ColliderShapes=(UIntPtr[])currentNative.ColliderShapes.Clone()};
                nativeShapeCheckpoints.Set(result,rebased);
                receipt["entityId"]=result.EntityId;receipt["recreatedRows"]=recreatedRows;
                receipt["differingPoseRows"]=differingPoseRows;
                receipt["differingGeometryRows"]=differingGeometryRows;
                receipt["historicalShapes"]=Array.ConvertAll(historicalNative.Shapes,NativeHex);
                receipt["currentShapes"]=Array.ConvertAll(currentNative.Shapes,NativeHex);
                receipt["historicalColliderReferenceHashes"]=Array.ConvertAll(pendingRecreatedShapes.ToArray(),
                    value=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.HistoricalCollider));
                receipt["currentColliderIds"]=Array.ConvertAll(pendingRecreatedShapes.ToArray(),
                    value=>value.CurrentCollider.GetInstanceID());
                receipt["verified"]=true;nativeShapeGeometryRebindFailure=null;
            }
            catch(Exception error)
            {
                nativeShapeGeometryRebindPoisoned=true;nativeShapeGeometryRebindFailure=error.ToString();
                receipt["error"]=error.ToString();throw;
            }
            finally {pendingRecreatedShapes.Clear();}
        }

        private static object[] ToObjects(Collider[] values)
        {
            if(values==null)return null;
            var result=new object[values.Length];for(int i=0;i<values.Length;i++)result[i]=values[i];return result;
        }

        private static bool SameBits(float a,float b)
        {return BitConverter.ToInt32(BitConverter.GetBytes(a),0)==BitConverter.ToInt32(BitConverter.GetBytes(b),0);}
        private static bool SameBits(NativeRigidPose a,NativeRigidPose b)
        {return SameBits(a.Px,b.Px)&&SameBits(a.Py,b.Py)&&SameBits(a.Pz,b.Pz)&&SameBits(a.Qx,b.Qx)
            &&SameBits(a.Qy,b.Qy)&&SameBits(a.Qz,b.Qz)&&SameBits(a.Qw,b.Qw);}
        private static bool SameBits(NativeShapeGeometry a,NativeShapeGeometry b)
        {return a.Type==b.Type&&SameBits(a.Value0,b.Value0)&&SameBits(a.Value1,b.Value1)&&SameBits(a.Value2,b.Value2);}

        private void CaptureNativeShapeTargets(Snapshot[] snapshots)
        {
            if(snapshots==null)throw new ArgumentNullException("snapshots");
            var captured=new List<KeyValuePair<Snapshot,NativeShapeCheckpoint>>(snapshots.Length);
            foreach(var row in snapshots) {
                if(row==null||row.Body==null)throw new InvalidOperationException("Native shape checkpoint contains a null body.");
                captured.Add(new KeyValuePair<Snapshot,NativeShapeCheckpoint>(row,
                    CaptureNativeShapeCheckpoint(row.Body,row.Colliders)));
            }
            foreach(var pair in captured) {
                nativeShapeCheckpoints.Set(pair.Key,pair.Value);
            }
            nativeShapePoseCaptures+=captured.Count;
            nativeShapePoseCaptureFailure=null;
        }

        private NativeShapeCheckpoint CaptureNativeShapeCheckpoint(Rigidbody body,
            SuperchargedPatch.NativeBodyColliderCheckpoint.Shape[] managedShapes)
        {
            IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(body);
            if(bodyPointer==IntPtr.Zero)throw new InvalidOperationException("Native shape checkpoint Rigidbody pointer is null.");
            int poseSize=Marshal.SizeOf(typeof(NativeRigidPose));
            int geometrySize=Marshal.SizeOf(typeof(NativeShapeGeometry));
            IntPtr shapes=Marshal.AllocHGlobal(MaximumNativeShapePoses*IntPtr.Size);
            IntPtr poses=Marshal.AllocHGlobal(MaximumNativeShapePoses*poseSize);
            IntPtr geometries=Marshal.AllocHGlobal(MaximumNativeShapePoses*geometrySize);
            try {
                uint count,error;
                var nativePointer=new UIntPtr(unchecked((uint)bodyPointer.ToInt32()));
                int ok=nativeCaptureShapePoses(new UIntPtr(unityPlayerBase),nativePointer,
                    shapes,poses,geometries,MaximumNativeShapePoses,out count,out error);
                if(ok!=1||error!=0||count>MaximumNativeShapePoses)
                    throw new InvalidOperationException("Native shape-state capture failed: ok="+ok+" count="+count+" error="+error);
                var shapeCopy=new UIntPtr[count];
                var poseCopy=new NativeRigidPose[count];
                var geometryCopy=new NativeShapeGeometry[count];
                for(int i=0;i<count;i++) {
                    shapeCopy[i]=new UIntPtr(unchecked((uint)Marshal.ReadInt32(shapes,i*IntPtr.Size)));
                    poseCopy[i]=(NativeRigidPose)Marshal.PtrToStructure(new IntPtr(poses.ToInt32()+i*poseSize),typeof(NativeRigidPose));
                    geometryCopy[i]=(NativeShapeGeometry)Marshal.PtrToStructure(
                        new IntPtr(geometries.ToInt32()+i*geometrySize),typeof(NativeShapeGeometry));
                    if(shapeCopy[i].Equals(UIntPtr.Zero)||!Finite(poseCopy[i])||!Finite(geometryCopy[i]))
                        throw new InvalidOperationException("Native shape-state capture returned an invalid row.");
                }
                var checkpoint=new NativeShapeCheckpoint {Body=body,RigidbodyPointer=nativePointer,Shapes=shapeCopy,
                    Poses=poseCopy,Geometries=geometryCopy};
                CaptureNativeColliderBindings(managedShapes,checkpoint);
                return checkpoint;
            }
            finally {Marshal.FreeHGlobal(geometries);Marshal.FreeHGlobal(poses);Marshal.FreeHGlobal(shapes);}
        }

        private Dictionary<Snapshot,NativeShapeCheckpoint> RequireNativeShapeTargets(Snapshot[] snapshots)
        {
            if(!NativeShapeCaptureActive||nativeCaptureShapePoses==null||nativeRestoreShapePoses==null)
                throw new InvalidOperationException("Native shape-pose checkpoint capture is inactive.");
            var result=new Dictionary<Snapshot,NativeShapeCheckpoint>();
            foreach(var row in snapshots) {
                NativeShapeCheckpoint target;
                if(!nativeShapeCheckpoints.TryGetValue(row,out target)||target==null)
                    throw new InvalidOperationException("Native shape-pose sidecar is missing for entity "+row.EntityId+".");
                if(target.Shapes==null||target.Poses==null||target.Geometries==null||
                    target.Shapes.Length!=target.Poses.Length||target.Shapes.Length!=target.Geometries.Length)
                    throw new InvalidOperationException("Native shape-state sidecar arrays differ for entity "+row.EntityId+".");
                IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(row.Body);
                var nativePointer=new UIntPtr(unchecked((uint)bodyPointer.ToInt32()));
                if(!ReferenceEquals(target.Body,row.Body)||!target.RigidbodyPointer.Equals(nativePointer))
                    throw new InvalidOperationException("Native Rigidbody identity changed for entity "+row.EntityId+".");
                var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                if(current.Shapes.Length!=target.Shapes.Length) {
                    RecordNativeShapeTopologyMismatch(row,target,current,-1,"count");
                    throw new InvalidOperationException("Native shape count changed for entity "+row.EntityId+".");
                }
                for(int i=0;i<target.Shapes.Length;i++)
                    if(!current.Shapes[i].Equals(target.Shapes[i])) {
                        RecordNativeShapeTopologyMismatch(row,target,current,i,"identity-or-order");
                        throw new InvalidOperationException("Native shape identity/order changed for entity "+row.EntityId+" at index "+i+".");
                    }
                RequireNativeColliderBindingsSame(row.EntityId,target,current);
                result.Add(row,target);
            }
            return result;
        }

        private void RecordNativeShapeTopologyMismatch(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint current,int firstDifference,string reason)
        {
            var record=new Dictionary<string,object>{{"entityId",row.EntityId},{"reason",reason},
                {"firstDifference",firstDifference},{"rigidbody",NativeHex(target.RigidbodyPointer)},
                {"targetShapes",Array.ConvertAll(target.Shapes,NativeHex)},
                {"currentShapes",Array.ConvertAll(current.Shapes,NativeHex)},
                {"sameShapeSet",SameShapeSet(target.Shapes,current.Shapes)},
                {"targetPoses",Array.ConvertAll(target.Poses,NativePoseDiagnostic)},
                {"currentPoses",Array.ConvertAll(current.Poses,NativePoseDiagnostic)},
                {"targetGeometries",Array.ConvertAll(target.Geometries,NativeGeometryDiagnostic)},
                {"currentGeometries",Array.ConvertAll(current.Geometries,NativeGeometryDiagnostic)}};
            nativeShapeTopologyMismatches.Add(record);
            if(nativeShapeTopologyMismatches.Count>32)nativeShapeTopologyMismatches.RemoveAt(0);
        }

        private static bool SameShapeSet(UIntPtr[] a,UIntPtr[] b)
        {
            if(a.Length!=b.Length)return false;
            var counts=new Dictionary<UIntPtr,int>();
            foreach(var value in a){int count;counts.TryGetValue(value,out count);counts[value]=count+1;}
            foreach(var value in b){int count;if(!counts.TryGetValue(value,out count)||count==0)return false;
                if(count==1)counts.Remove(value);else counts[value]=count-1;}
            return counts.Count==0;
        }

        private static object NativePoseDiagnostic(NativeRigidPose value)
        {
            return new Dictionary<string,object>{{"position",new[]{value.Px,value.Py,value.Pz}},
                {"rotation",new[]{value.Qx,value.Qy,value.Qz,value.Qw}}};
        }

        private static object NativeGeometryDiagnostic(NativeShapeGeometry value)
        {
            return new Dictionary<string,object>{{"type",value.Type},{"values",new[]{value.Value0,value.Value1,value.Value2}}};
        }

        private void CaptureNativeColliderBindings(
            SuperchargedPatch.NativeBodyColliderCheckpoint.Shape[] managedShapes,
            NativeShapeCheckpoint checkpoint)
        {
            if(managedShapes==null||shapeCollider==null)
                throw new InvalidOperationException("Native Collider-to-PxShape capture is unavailable.");
            checkpoint.Colliders=new Collider[managedShapes.Length];
            checkpoint.ColliderShapes=new UIntPtr[managedShapes.Length];
            var uniqueColliders=new HashSet<Collider>();
            for(int i=0;i<managedShapes.Length;i++)
            {
                var collider=(Collider)shapeCollider.GetValue(managedShapes[i]);
                if(collider==null||!uniqueColliders.Add(collider))
                    throw new InvalidOperationException("Native Collider-to-PxShape capture found an invalid Collider.");
                checkpoint.Colliders[i]=collider;
                checkpoint.ColliderShapes[i]=ReadNativeColliderShape(collider);
            }
        }

        private UIntPtr ReadNativeColliderShape(Collider collider)
        {
            IntPtr pointer=(IntPtr)cachedPtr.GetValue(collider);
            if(pointer==IntPtr.Zero)
                throw new InvalidOperationException("Native Collider pointer is null: "+collider.GetInstanceID()+".");
            // Unity 2017.4.8f1 BoxCollider::PoseChanged reads the owned
            // PxShape pointer from the common Collider native object at +0x24.
            return new UIntPtr(unchecked((uint)Marshal.ReadInt32(pointer,0x24)));
        }

        private static void RequireNativeColliderBindingsSame(int entityId,
            NativeShapeCheckpoint target,NativeShapeCheckpoint current)
        {
            if(target.Colliders==null||target.ColliderShapes==null||current.Colliders==null||
                current.ColliderShapes==null||target.Colliders.Length!=target.ColliderShapes.Length||
                current.Colliders.Length!=current.ColliderShapes.Length||
                target.Colliders.Length!=current.Colliders.Length)
                throw new InvalidOperationException("Native Collider-to-PxShape binding arrays differ for entity "+entityId+".");
            for(int i=0;i<target.Colliders.Length;i++)
                if(!ReferenceEquals(target.Colliders[i],current.Colliders[i])||
                    !target.ColliderShapes[i].Equals(current.ColliderShapes[i]))
                    throw new InvalidOperationException("Native Collider-to-PxShape ownership changed for entity "+
                        entityId+" at managed index "+i+".");
        }

        private object[] CorrectReenabledNativeShapeOrder(Snapshot[] saved,
            Dictionary<int,List<Collider>> enabledAdds)
        {
            var receipts=new List<object>();
            foreach(var row in saved)
            {
                List<Collider> added;
                if(!enabledAdds.TryGetValue(row.EntityId,out added)||added.Count==0)continue;
                NativeShapeCheckpoint target;
                if(!nativeShapeCheckpoints.TryGetValue(row,out target)||target==null)
                    throw new InvalidOperationException("Native shape-order target is missing for entity "+row.EntityId+".");
                var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                if(SameNativeShapeOrder(target,current))
                {
                    RequireNativeColliderBindingsSame(row.EntityId,target,current);
                    receipts.Add(new Dictionary<string,object>{{"entityId",row.EntityId},{"changed",false},
                        {"reason","already-exact"}});
                    continue;
                }
                if(added.Count!=2||target.Shapes.Length<2||current.Shapes.Length!=target.Shapes.Length||
                    !SameShapeSet(target.Shapes,current.Shapes))
                    throw new InvalidOperationException("Native shape-order correction is outside the bounded two-Collider scope: "+row.EntityId+".");
                if(target.Colliders==null||target.ColliderShapes==null||
                    target.Colliders.Length!=target.Shapes.Length||target.ColliderShapes.Length!=target.Shapes.Length||
                    !SameShapeSet(target.ColliderShapes,target.Shapes))
                    throw new InvalidOperationException("Native shape-order correction requires an exact Collider/PxShape bijection: "+row.EntityId+".");
                RequireNativeColliderBindingsSame(row.EntityId,target,current);
                var targetOwners=new Collider[2];
                int first=target.Shapes.Length-2;
                for(int i=0;i<2;i++)
                {
                    UIntPtr wanted=target.Shapes[first+i];
                    int binding=IndexOf(target.ColliderShapes,wanted);
                    if(binding<0)throw new InvalidOperationException("Native shape-order correction cannot bind target suffix: "+row.EntityId+".");
                    targetOwners[i]=target.Colliders[binding];
                    if(!added.Contains(targetOwners[i])||!(targetOwners[i] is BoxCollider)||!targetOwners[i].enabled)
                        throw new InvalidOperationException("Native shape-order correction target Colliders differ: "+row.EntityId+".");
                }
                if(ReferenceEquals(targetOwners[0],targetOwners[1]))
                    throw new InvalidOperationException("Native shape-order correction target Colliders are not distinct: "+row.EntityId+".");
                for(int i=0;i<first;i++)
                    if(!current.Shapes[i].Equals(target.Shapes[i]))
                        throw new InvalidOperationException("Native shape-order correction would alter the actor prefix: "+row.EntityId+".");
                if(!current.Shapes[first].Equals(target.Shapes[first+1])||
                    !current.Shapes[first+1].Equals(target.Shapes[first]))
                    throw new InvalidOperationException("Native shape-order correction requires one exact reversed suffix: "+row.EntityId+".");

                // Detach in reverse target order so the LIFO native shape pool
                // exposes the first target pointer first; attach in target
                // order so the actor appends the exact saved sequence.
                for(int i=1;i>=0;i--){targetOwners[i].enabled=false;if(targetOwners[i].enabled)
                    throw new InvalidOperationException("Native shape-order correction disable did not read back: "+row.EntityId+".");}
                for(int i=0;i<2;i++){targetOwners[i].enabled=true;if(!targetOwners[i].enabled)
                    throw new InvalidOperationException("Native shape-order correction enable did not read back: "+row.EntityId+".");}

                var after=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                if(!SameNativeShapeOrder(target,after))
                    throw new InvalidOperationException("Native shape-order correction did not restore exact actor order: "+row.EntityId+".");
                RequireNativeColliderBindingsSame(row.EntityId,target,after);
                receipts.Add(new Dictionary<string,object>{{"entityId",row.EntityId},{"changed",true},
                    {"targetShapes",Array.ConvertAll(target.Shapes,NativeHex)},
                    {"beforeShapes",Array.ConvertAll(current.Shapes,NativeHex)},
                    {"afterShapes",Array.ConvertAll(after.Shapes,NativeHex)},
                    {"colliderIds",Array.ConvertAll(targetOwners,c=>c.GetInstanceID())},{"verified",true}});
            }
            return receipts.ToArray();
        }

        private static bool SameNativeShapeOrder(NativeShapeCheckpoint a,NativeShapeCheckpoint b)
        {
            if(a.Shapes.Length!=b.Shapes.Length)return false;
            for(int i=0;i<a.Shapes.Length;i++)if(!a.Shapes[i].Equals(b.Shapes[i]))return false;
            return true;
        }

        private static int IndexOf(UIntPtr[] values,UIntPtr wanted)
        {
            for(int i=0;i<values.Length;i++)if(values[i].Equals(wanted))return i;
            return -1;
        }

        private void RestoreNativeShapePoses(Snapshot row,NativeShapeCheckpoint target,long call,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            int count=target.Shapes.Length;
            int poseSize=Marshal.SizeOf(typeof(NativeRigidPose));
            int geometrySize=Marshal.SizeOf(typeof(NativeShapeGeometry));
            IntPtr shapes=Marshal.AllocHGlobal(Math.Max(1,count*IntPtr.Size));
            IntPtr poses=Marshal.AllocHGlobal(Math.Max(1,count*poseSize));
            IntPtr geometries=Marshal.AllocHGlobal(Math.Max(1,count*geometrySize));
            try {
                for(int i=0;i<count;i++) {
                    Marshal.WriteInt32(shapes,i*IntPtr.Size,unchecked((int)target.Shapes[i].ToUInt32()));
                    Marshal.StructureToPtr(target.Poses[i],new IntPtr(poses.ToInt32()+i*poseSize),false);
                    Marshal.StructureToPtr(target.Geometries[i],new IntPtr(geometries.ToInt32()+i*geometrySize),false);
                }
                NativeShapePoseRestoreReceipt receipt;
                int ok=nativeRestoreShapePoses(new UIntPtr(unityPlayerBase),target.RigidbodyPointer,
                    shapes,poses,geometries,(uint)count,out receipt);
                var record=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                    {"rigidbody",NativeHex(receipt.Rigidbody)},{"actor",NativeHex(receipt.Actor)},
                    {"count",receipt.Count},{"poseChanged",receipt.PoseChanged},{"geometryChanged",receipt.GeometryChanged},
                    {"expectedOrderHash",receipt.ExpectedOrderHash},
                    {"observedOrderHash",receipt.ObservedOrderHash},{"result",receipt.Result},
                    {"lastError",receipt.LastError},{"exact",false}};
                nativeShapePoseRestores.Add(record);
                if(nativeShapePoseRestores.Count>128)nativeShapePoseRestores.RemoveAt(0);
                if(ok!=1||receipt.ApiVersion!=6||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeShapePoseRestoreReceipt))
                    ||receipt.Result!=1||!receipt.Rigidbody.Equals(target.RigidbodyPointer)||receipt.Count!=(uint)count
                    ||receipt.ExpectedOrderHash!=receipt.ObservedOrderHash)
                    throw new InvalidOperationException("Native shape-state restore failed for "+row.EntityId+
                        ": result="+receipt.Result+" error="+receipt.LastError);
                RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                SuperchargedPatch.NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                record["exact"]=true;
            }
            finally {Marshal.FreeHGlobal(geometries);Marshal.FreeHGlobal(poses);Marshal.FreeHGlobal(shapes);}
        }

        private bool Finite(NativeRigidPose pose)
        {
            return Finite(pose.Px)&&Finite(pose.Py)&&Finite(pose.Pz)&&Finite(pose.Qx)&&Finite(pose.Qy)
                &&Finite(pose.Qz)&&Finite(pose.Qw);
        }

        private bool Finite(NativeShapeGeometry geometry)
        {
            if(geometry.Type==0)return Finite(geometry.Value0)&&geometry.Value0>0f;
            if(geometry.Type==2)return Finite(geometry.Value0)&&geometry.Value0>0f&&
                Finite(geometry.Value1)&&geometry.Value1>=0f;
            if(geometry.Type==3)return Finite(geometry.Value0)&&geometry.Value0>0f&&
                Finite(geometry.Value1)&&geometry.Value1>0f&&Finite(geometry.Value2)&&geometry.Value2>0f;
            return true;
        }

        private static string NativeHex(UIntPtr value)
        {
            return "0x"+value.ToUInt32().ToString("X8");
        }
    }
}
