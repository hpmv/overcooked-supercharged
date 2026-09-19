using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
            internal NativeRigidPose ActorPose;
            internal NativeRigidPose Body2Actor;
            internal NativeRigidPose Body2World;
            internal uint WakeCounterBits;
            internal uint BufferedIsSleeping;
            internal uint BodySimActive;
            internal NativeKinematicTargetReceipt KinematicTarget;
            internal NativeBody2WorldCaptureReceipt Lifecycle;
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

        public sealed class NativeFreezeObservationState
        {
            internal Snapshot Row;
            internal Rigidbody Body;
            internal object Before;
            internal int Layer;
        }

        private sealed class NativeFreezeSetterObservationState
        {
            internal Snapshot Row;
            internal Rigidbody Body;
            internal object Before,ManagedBefore,RequestedValue;
            internal string Stage,Setter;
        }

        // The Unity 2017 managed profile has no ConditionalWeakTable. Keep weak
        // keys explicitly so checkpoint-history eviction also releases sidecars.
        private sealed class WeakShapeTable
        {
            private const uint SweepInterval=4096;
            private readonly Dictionary<int,List<WeakShapeEntry>> buckets=
                new Dictionary<int,List<WeakShapeEntry>>();
            private uint setsSinceSweep;

            internal void Sweep()
            {
                foreach(int key in new List<int>(buckets.Keys))
                {
                    var entries=buckets[key];
                    for(int i=entries.Count-1;i>=0;i--)
                        if(entries[i].Snapshot.Target==null)entries.RemoveAt(i);
                    if(entries.Count==0)buckets.Remove(key);
                }
            }

            internal void Set(Snapshot snapshot,NativeShapeCheckpoint checkpoint)
            {
                if(++setsSinceSweep>=SweepInterval){setsSinceSweep=0;Sweep();}
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

            internal void Remove(Snapshot snapshot)
            {
                int key=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(snapshot);
                List<WeakShapeEntry> entries;
                if(!buckets.TryGetValue(key,out entries))return;
                for(int i=entries.Count-1;i>=0;i--)
                {
                    object target=entries[i].Snapshot.Target;
                    if(target==null||ReferenceEquals(target,snapshot))entries.RemoveAt(i);
                }
                if(entries.Count==0)buckets.Remove(key);
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

            internal void Clear(){buckets.Clear();setsSinceSweep=0;}
        }

        private static BodyRestoreModule nativeShapeCaptureOwner;
        private Harmony nativeShapeCaptureHarmony;
        private readonly WeakShapeTable nativeShapeCheckpoints=new WeakShapeTable();
        private readonly List<object> nativeBody2WorldCaptures=new List<object>();
        private NativeCaptureShapePoses nativeCaptureShapePoses;
        private NativeRestoreShapePoses nativeRestoreShapePoses;
        private readonly List<object> nativeShapePoseRestores=new List<object>();
        private readonly List<object> nativeSleepingKinematicPoseRestores=new List<object>();
        private readonly List<object> nativeShapeTopologyMismatches=new List<object>();
        private readonly List<object> nativeKinematicWakeMismatches=new List<object>();
        private readonly List<object> nativeKinematicStageObservations=new List<object>();
        private readonly List<object> nativePostMaintenanceComparisons=new List<object>();
        private readonly List<object> nativeKinematicFreezeObservations=new List<object>();
        private readonly List<object> nativeKinematicFreezeSetterObservations=new List<object>();
        private readonly Dictionary<Rigidbody,Snapshot> latestNativeRowsByBody=new Dictionary<Rigidbody,Snapshot>();
        private readonly List<object> nativeShapeGeometryRebinds=new List<object>();
        private readonly List<object> nativeDestroyedBodyRebinds=new List<object>();
        private readonly List<object> nativeDestroyedBodyModeRestores=new List<object>();
        private readonly List<PendingRecreatedShape> pendingRecreatedShapes=new List<PendingRecreatedShape>();
        private long nativeShapePoseCaptures;
        private string nativeShapePoseCaptureFailure;
        private bool nativeShapeGeometryRebindPoisoned;
        private string nativeShapeGeometryRebindFailure;
        private int nativeKinematicStageFrame=-1,nativeKinematicStagesDiscarded;
        private string currentNativeKinematicStage;
        private Snapshot[] pendingNativePostMaintenanceTargets,currentNativePostMaintenanceTargets;
        private readonly HashSet<int> deferredKinematicSleepEntities=new HashSet<int>();
        private readonly HashSet<int> deferredKinematicTargetEntities=new HashSet<int>();
        private readonly Dictionary<int,NativeShapeCheckpoint> deferredKinematicSleepPreimages=
            new Dictionary<int,NativeShapeCheckpoint>();
        private HashSet<int> pendingNativePostMaintenanceDeferredEntities=new HashSet<int>();
        private HashSet<int> pendingNativePostMaintenanceTargetEntities=new HashSet<int>();
        private int pendingNativePostMaintenanceArmUnityFrame=-1;
        private string nativePostMaintenanceFailure;
        private FieldInfo frozenPhysicsBody,frozenPhysicsIsKinematic;
        private NativeFreezeObservationState nativeKinematicFreezeSetterScope;
        private string nativeKinematicFreezeSetterStage;
        private int nativeKinematicFreezeSetterTranspilers;

        private object NativeKinematicStageDiagnostics
        {
            get {return new Dictionary<string,object>{
                {"scope","read-only-native-kinematic-wake-and-target-stage-observations"},
                {"frame",nativeKinematicStageFrame},{"maximumStages",32},
                {"discardedOlderStages",nativeKinematicStagesDiscarded},
                {"stages",nativeKinematicStageObservations.ToArray()}};}
        }

        private object NativePostMaintenanceDiagnostics
        {
            get {return new Dictionary<string,object>{
                {"scope","read-only-pre-pause-target-versus-immediate-post-maintenance-native-body-comparison"},
                {"pending",pendingNativePostMaintenanceTargets!=null},
                {"deferredKinematicSleepEntities",deferredKinematicSleepEntities.ToArray()},
                {"deferredKinematicTargetEntities",deferredKinematicTargetEntities.ToArray()},
                {"pendingDeferredKinematicSleepEntities",pendingNativePostMaintenanceDeferredEntities.ToArray()},
                {"pendingDeferredKinematicTargetEntities",pendingNativePostMaintenanceTargetEntities.ToArray()},
                {"armUnityFrame",pendingNativePostMaintenanceArmUnityFrame},
                {"failure",nativePostMaintenanceFailure},
                {"comparisons",nativePostMaintenanceComparisons.ToArray()}};}
        }

        private object NativeKinematicFreezeDiagnostics
        {
            get {return new Dictionary<string,object>{
                {"scope","read-only-native-lifecycle-before-and-after-TimeManager-FrozenPhysicsData-for-already-kinematic-checkpoint-bodies"},
                {"transpiledMethods",nativeKinematicFreezeSetterTranspilers},
                {"observations",nativeKinematicFreezeObservations.ToArray()},
                {"setterObservations",nativeKinematicFreezeSetterObservations.ToArray()}};}
        }

        private bool NativeShapeCaptureActive
        {
            get {return nativeShapeCaptureHarmony!=null&&ReferenceEquals(nativeShapeCaptureOwner,this);}
        }

        private void ActivateNativeShapeCapture()
        {
            if(nativeShapeCaptureHarmony!=null)return;
            if(nativeCaptureShapePoses==null||nativeRestoreShapePoses==null||nativeCaptureBody2World==null||
                nativeRestoreBody2World==null||nativeRestoreWakeState==null||nativeGetKinematicTarget==null||
                nativeSetKinematicTarget==null||nativeInvalidateKinematicTarget==null||cachedPtr==null)
                throw new InvalidOperationException("Native shape/body-state capture requires the version-9 native helper.");
            if(nativeShapeCaptureOwner!=null)
                throw new InvalidOperationException("Another native shape-pose capture module is active.");
            var capture=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "Capture",new[]{typeof(HashSet<int>)});
            var postfix=GetType().GetMethod("AfterNativeBodyCapture",BindingFlags.Public|BindingFlags.Static);
            var beginStage=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "BeginStageObservations",new[]{typeof(int)});
            var beginStagePostfix=GetType().GetMethod("AfterBeginNativeBodyStageObservations",BindingFlags.Public|BindingFlags.Static);
            var observeStage=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "ObserveStage",new[]{typeof(Snapshot[]),typeof(string)});
            var observeStagePrefix=GetType().GetMethod("BeforeNativeBodyStageObservation",BindingFlags.Public|BindingFlags.Static);
            var observeStagePostfix=GetType().GetMethod("AfterNativeBodyStageObservation",BindingFlags.Public|BindingFlags.Static);
            var controller=typeof(SuperchargedPatch.Bridge.NativeSessionBridge).Assembly.GetType(
                "SuperchargedPatch.ControllerHandler",true);
            var update=AccessTools.DeclaredMethod(controller,"Update",Type.EmptyTypes);
            var postMaintenance=GetType().GetMethod("ObserveNativePostMaintenance",BindingFlags.Public|BindingFlags.Static);
            var frozen=typeof(TimeManager).GetNestedType("FrozenPhysicsData",BindingFlags.NonPublic);
            var freezeConstructor=frozen==null?null:AccessTools.Constructor(frozen,new[]{typeof(Rigidbody),typeof(int)});
            var unfreeze=frozen==null?null:AccessTools.DeclaredMethod(frozen,"Unfreeze",Type.EmptyTypes);
            frozenPhysicsBody=frozen==null?null:frozen.GetField("m_frozenBody",BindingFlags.Instance|BindingFlags.NonPublic);
            frozenPhysicsIsKinematic=frozen==null?null:frozen.GetField("m_isKinematic",BindingFlags.Instance|BindingFlags.NonPublic);
            var beforeFreeze=GetType().GetMethod("BeforeNativeKinematicFreeze",BindingFlags.Public|BindingFlags.Static);
            var afterFreeze=GetType().GetMethod("AfterNativeKinematicFreeze",BindingFlags.Public|BindingFlags.Static);
            var beforeUnfreeze=GetType().GetMethod("BeforeNativeKinematicUnfreeze",BindingFlags.Public|BindingFlags.Static);
            var afterUnfreeze=GetType().GetMethod("AfterNativeKinematicUnfreeze",BindingFlags.Public|BindingFlags.Static);
            var transpileSetters=GetType().GetMethod("TranspileNativeKinematicFreezeSetters",BindingFlags.Public|BindingFlags.Static);
            var rebindShape=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyColliderCheckpoint),
                "RebindRecreatedOwnerShape",new[]{typeof(Shape),typeof(Shape),typeof(Transform),typeof(Transform),typeof(Rigidbody)});
            var rebindShapePostfix=GetType().GetMethod("AfterRebindRecreatedOwnerShape",BindingFlags.Public|BindingFlags.Static);
            var rebindBody=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "RebindSurvivingWithRecreatedColliders",new[]{typeof(Snapshot),typeof(Snapshot),typeof(Shape[])});
            var rebindBodyPostfix=GetType().GetMethod("AfterRebindSurvivingWithRecreatedColliders",BindingFlags.Public|BindingFlags.Static);
            var rebindDestroyedBody=AccessTools.DeclaredMethod(typeof(SuperchargedPatch.NativeBodyPoseCheckpoint),
                "RebindDestroyedColliderless",new[]{typeof(Snapshot),typeof(Team17.Online.Multiplayer.Messaging.EntitySerialisationEntry)});
            var rebindDestroyedBodyPrefix=GetType().GetMethod("BeforeRebindDestroyedColliderless",BindingFlags.Public|BindingFlags.Static);
            var rebindDestroyedBodyPostfix=GetType().GetMethod("AfterRebindDestroyedColliderless",BindingFlags.Public|BindingFlags.Static);
            if(capture==null||capture.ReturnType!=typeof(Snapshot[])||postfix==null
                ||beginStage==null||beginStage.ReturnType!=typeof(void)||beginStagePostfix==null
                ||observeStage==null||observeStage.ReturnType!=typeof(void)||observeStagePrefix==null||observeStagePostfix==null
                ||update==null||update.ReturnType!=typeof(void)||postMaintenance==null
                ||freezeConstructor==null||unfreeze==null||unfreeze.ReturnType!=typeof(void)
                ||frozenPhysicsBody==null||frozenPhysicsBody.FieldType!=typeof(Rigidbody)
                ||frozenPhysicsIsKinematic==null||frozenPhysicsIsKinematic.FieldType!=typeof(bool)
                ||beforeFreeze==null||afterFreeze==null||beforeUnfreeze==null||afterUnfreeze==null||transpileSetters==null
                ||rebindShape==null||rebindShape.ReturnType!=typeof(Shape)||rebindShapePostfix==null
                ||rebindBody==null||rebindBody.ReturnType!=typeof(Snapshot)||rebindBodyPostfix==null
                ||rebindDestroyedBody==null||rebindDestroyedBody.ReturnType!=typeof(Snapshot)||
                rebindDestroyedBodyPrefix==null||rebindDestroyedBodyPostfix==null)
                throw new InvalidOperationException("Frozen native-body checkpoint capture contract differs.");
            nativeShapeCaptureHarmony=new Harmony("supercharged.authoring.body-native-shape-pose."+
                GetType().Assembly.GetName().Name);
            nativeShapeCaptureOwner=this;
            pendingRecreatedShapes.Clear();nativeShapeGeometryRebindPoisoned=false;nativeShapeGeometryRebindFailure=null;
            try {
                nativeShapeCaptureHarmony.Patch(capture,postfix:new HarmonyMethod(postfix));
                nativeShapeCaptureHarmony.Patch(beginStage,postfix:new HarmonyMethod(beginStagePostfix));
                nativeShapeCaptureHarmony.Patch(observeStage,prefix:new HarmonyMethod(observeStagePrefix),
                    postfix:new HarmonyMethod(observeStagePostfix));
                var postMaintenancePatch=new HarmonyMethod(postMaintenance);postMaintenancePatch.priority=Priority.Last;
                nativeShapeCaptureHarmony.Patch(update,prefix:postMaintenancePatch);
                var setterTranspilerPatch=new HarmonyMethod(transpileSetters);
                nativeShapeCaptureHarmony.Patch(freezeConstructor,prefix:new HarmonyMethod(beforeFreeze),
                    postfix:new HarmonyMethod(afterFreeze),transpiler:setterTranspilerPatch);
                nativeShapeCaptureHarmony.Patch(unfreeze,prefix:new HarmonyMethod(beforeUnfreeze),
                    postfix:new HarmonyMethod(afterUnfreeze),transpiler:setterTranspilerPatch);
                if(nativeKinematicFreezeSetterTranspilers!=2)
                    throw new InvalidOperationException("FrozenPhysicsData setter transpiler count differs.");
                nativeShapeCaptureHarmony.Patch(rebindShape,postfix:new HarmonyMethod(rebindShapePostfix));
                nativeShapeCaptureHarmony.Patch(rebindBody,postfix:new HarmonyMethod(rebindBodyPostfix));
                nativeShapeCaptureHarmony.Patch(rebindDestroyedBody,
                    prefix:new HarmonyMethod(rebindDestroyedBodyPrefix),
                    postfix:new HarmonyMethod(rebindDestroyedBodyPostfix));
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
            currentNativeKinematicStage=null;nativeKinematicStageObservations.Clear();
            nativeKinematicStageFrame=-1;nativeKinematicStagesDiscarded=0;
            pendingNativePostMaintenanceTargets=null;currentNativePostMaintenanceTargets=null;
            deferredKinematicSleepEntities.Clear();deferredKinematicTargetEntities.Clear();
            deferredKinematicSleepPreimages.Clear();
            pendingNativePostMaintenanceDeferredEntities.Clear();
            pendingNativePostMaintenanceTargetEntities.Clear();
            pendingNativePostMaintenanceArmUnityFrame=-1;nativePostMaintenanceComparisons.Clear();
            nativePostMaintenanceFailure=null;
            nativeKinematicFreezeObservations.Clear();nativeKinematicFreezeSetterObservations.Clear();
            latestNativeRowsByBody.Clear();nativeKinematicFreezeSetterScope=null;
            nativeKinematicFreezeSetterStage=null;nativeKinematicFreezeSetterTranspilers=0;
            frozenPhysicsBody=null;frozenPhysicsIsKinematic=null;
        }

        public static void AfterNativeBodyCapture(Snapshot[] __result)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            try {
                owner.CaptureNativeShapeTargets(__result);
                if(owner.currentNativeKinematicStage!=null) {
                    try {
                        if(owner.currentNativeKinematicStage=="after-final-maintenance")
                            owner.RecordNativePostMaintenanceComparison(
                                owner.currentNativePostMaintenanceTargets,__result);
                        else owner.RecordNativeKinematicStage(__result,owner.currentNativeKinematicStage);
                    }
                    finally {if(__result!=null)foreach(var row in __result)if(row!=null)owner.nativeShapeCheckpoints.Remove(row);}
                }
            }
            catch(Exception error) {
                // Observation must not change ordinary gameplay. A snapshot that
                // lacks this sidecar is rejected before any later rewind mutation.
                owner.nativeShapePoseCaptureFailure=error.ToString();
                if(owner.currentNativeKinematicStage!=null)
                    owner.RecordNativeKinematicStageFailure(owner.currentNativeKinematicStage,error);
                if(owner.currentNativeKinematicStage=="after-final-maintenance"&&
                    (owner.pendingNativePostMaintenanceDeferredEntities.Count!=0||
                    owner.pendingNativePostMaintenanceTargetEntities.Count!=0))
                    owner.FailNativePostMaintenance(
                        "Deferred kinematic maintenance comparison failed: "+error.Message);
            }
        }

        public static void AfterBeginNativeBodyStageObservations(int __0)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            owner.nativeKinematicStageFrame=__0;owner.nativeKinematicStagesDiscarded=0;
            owner.nativeKinematicStageObservations.Clear();owner.currentNativeKinematicStage=null;
        }

        public static void BeforeNativeBodyStageObservation(Snapshot[] __0,string __1)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            owner.currentNativeKinematicStage=__1??"<null>";
            if(__1=="after-final-pause"&&__0!=null) {
                owner.pendingNativePostMaintenanceTargets=(Snapshot[])__0.Clone();
                owner.pendingNativePostMaintenanceDeferredEntities=
                    new HashSet<int>(owner.deferredKinematicSleepEntities);
                owner.pendingNativePostMaintenanceTargetEntities=
                    new HashSet<int>(owner.deferredKinematicTargetEntities);
                owner.pendingNativePostMaintenanceArmUnityFrame=Time.frameCount;
            }
            if(__1=="after-final-maintenance")owner.currentNativePostMaintenanceTargets=__0;
        }

        public static void AfterNativeBodyStageObservation()
        {
            var owner=nativeShapeCaptureOwner;
            if(owner!=null) {
                owner.currentNativeKinematicStage=null;
                owner.currentNativePostMaintenanceTargets=null;
            }
        }

        public static void ObserveNativePostMaintenance()
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null||owner.pendingNativePostMaintenanceTargets==null||
                Time.frameCount<=owner.pendingNativePostMaintenanceArmUnityFrame||
                !SuperchargedPatch.Bridge.NativeSessionBridge.KitchenReady||
                !TimeManager.IsPaused(TimeManager.PauseLayer.Main)||Physics.autoSimulation)return;
            var target=owner.pendingNativePostMaintenanceTargets;
            owner.pendingNativePostMaintenanceTargets=null;
            try {SuperchargedPatch.NativeBodyPoseCheckpoint.ObserveStage(target,"after-final-maintenance");}
            catch(Exception error) {
                owner.AddNativePostMaintenanceComparison(new Dictionary<string,object>{
                    {"unityFrame",Time.frameCount},{"captured",false},{"error",error.ToString()}});
                if(owner.pendingNativePostMaintenanceDeferredEntities.Count!=0||
                    owner.pendingNativePostMaintenanceTargetEntities.Count!=0)
                    owner.FailNativePostMaintenance("Deferred kinematic maintenance capture failed: "+error.Message);
            }
        }

        public static void BeforeNativeKinematicFreeze(Rigidbody __0,int __1,
            out NativeFreezeObservationState __state)
        {
            __state=null;var owner=nativeShapeCaptureOwner;
            if(owner!=null){owner.nativeKinematicFreezeSetterScope=null;owner.nativeKinematicFreezeSetterStage=null;}
            if(owner==null||__0==null||!__0.isKinematic)return;
            Snapshot row;
            if(!owner.latestNativeRowsByBody.TryGetValue(__0,out row)||row==null)return;
            try {
                __state=new NativeFreezeObservationState {Row=row,Body=__0,Layer=__1,
                    Before=owner.CaptureNativeShapeCheckpoint(__0,row.Colliders)};
                owner.nativeKinematicFreezeSetterScope=__state;
                owner.nativeKinematicFreezeSetterStage="freeze-setter";
            }
            catch(Exception error){owner.AddNativeKinematicFreezeFailure("freeze-before",row,error);}
        }

        public static void AfterNativeKinematicFreeze(NativeFreezeObservationState __state)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            try {if(__state!=null)owner.RecordNativeKinematicFreezeTransition("freeze",__state);}
            finally {owner.nativeKinematicFreezeSetterScope=null;owner.nativeKinematicFreezeSetterStage=null;}
        }

        public static void BeforeNativeKinematicUnfreeze(object __instance,
            out NativeFreezeObservationState __state)
        {
            __state=null;var owner=nativeShapeCaptureOwner;
            if(owner!=null){owner.nativeKinematicFreezeSetterScope=null;owner.nativeKinematicFreezeSetterStage=null;}
            if(owner==null||__instance==null)return;
            try {
                if(!(bool)owner.frozenPhysicsIsKinematic.GetValue(__instance))return;
                var body=(Rigidbody)owner.frozenPhysicsBody.GetValue(__instance);Snapshot row;
                if(body==null||!owner.latestNativeRowsByBody.TryGetValue(body,out row)||row==null)return;
                __state=new NativeFreezeObservationState {Row=row,Body=body,Layer=-1,
                    Before=owner.CaptureNativeShapeCheckpoint(body,row.Colliders)};
                owner.nativeKinematicFreezeSetterScope=__state;
                owner.nativeKinematicFreezeSetterStage="unfreeze-setter";
            }
            catch(Exception error){owner.AddNativeKinematicFreezeFailure("unfreeze-before",null,error);}
        }

        public static void AfterNativeKinematicUnfreeze(NativeFreezeObservationState __state)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null)return;
            try {if(__state!=null)owner.RecordNativeKinematicFreezeTransition("unfreeze",__state);}
            finally {owner.nativeKinematicFreezeSetterScope=null;owner.nativeKinematicFreezeSetterStage=null;}
        }

        public static IEnumerable<CodeInstruction> TranspileNativeKinematicFreezeSetters(
            IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var velocity=AccessTools.PropertySetter(typeof(Rigidbody),"velocity");
            var angular=AccessTools.PropertySetter(typeof(Rigidbody),"angularVelocity");
            var kinematic=AccessTools.PropertySetter(typeof(Rigidbody),"isKinematic");
            var gravity=AccessTools.PropertySetter(typeof(Rigidbody),"useGravity");
            var wrappedVelocity=AccessTools.Method(typeof(BodyRestoreModule),
                "SetVelocityWithNativeFreezeObservation");
            var wrappedAngular=AccessTools.Method(typeof(BodyRestoreModule),
                "SetAngularVelocityWithNativeFreezeObservation");
            var wrappedKinematic=AccessTools.Method(typeof(BodyRestoreModule),
                "SetIsKinematicWithNativeFreezeObservation");
            var wrappedGravity=AccessTools.Method(typeof(BodyRestoreModule),
                "SetUseGravityWithNativeFreezeObservation");
            if(velocity==null||angular==null||kinematic==null||gravity==null||wrappedVelocity==null
                ||wrappedAngular==null||wrappedKinematic==null||wrappedGravity==null)
                throw new InvalidOperationException("FrozenPhysicsData Rigidbody setter contract differs.");
            int velocityCount=0,angularCount=0,kinematicCount=0,gravityCount=0;
            var result=new List<CodeInstruction>();
            foreach(var instruction in instructions)
            {
                if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)
                    &&Equals(instruction.operand,velocity))
                {instruction.opcode=OpCodes.Call;instruction.operand=wrappedVelocity;velocityCount++;}
                else if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)
                    &&Equals(instruction.operand,angular))
                {instruction.opcode=OpCodes.Call;instruction.operand=wrappedAngular;angularCount++;}
                else if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)
                    &&Equals(instruction.operand,kinematic))
                {instruction.opcode=OpCodes.Call;instruction.operand=wrappedKinematic;kinematicCount++;}
                else if((instruction.opcode==OpCodes.Call||instruction.opcode==OpCodes.Callvirt)
                    &&Equals(instruction.operand,gravity))
                {instruction.opcode=OpCodes.Call;instruction.operand=wrappedGravity;gravityCount++;}
                result.Add(instruction);
            }
            if(velocityCount!=1||angularCount!=1||kinematicCount!=1||gravityCount!=1)
                throw new InvalidOperationException("FrozenPhysicsData setter call count differs in "+
                    (__originalMethod==null?"<unknown>":__originalMethod.Name)+": velocity="+velocityCount+
                    " angular="+angularCount+" kinematic="+kinematicCount+" gravity="+gravityCount+".");
            var owner=nativeShapeCaptureOwner;
            if(owner==null)throw new InvalidOperationException("FrozenPhysicsData setter observer has no active owner.");
            owner.nativeKinematicFreezeSetterTranspilers++;
            return result;
        }

        public static void SetVelocityWithNativeFreezeObservation(Rigidbody body,Vector3 value)
        {
            var state=BeginNativeKinematicFreezeSetterObservation(body,"velocity",VectorDiagnostic(value));
            string suppression;
            if(ShouldSuppressRedundantSleepingKinematicMotion(
                state,body,value,false,out suppression)) {
                EndNativeKinematicFreezeSetterObservation(state,false,suppression);
                return;
            }
            body.velocity=value;
            EndNativeKinematicFreezeSetterObservation(state,true,null);
        }

        public static void SetAngularVelocityWithNativeFreezeObservation(Rigidbody body,Vector3 value)
        {
            var state=BeginNativeKinematicFreezeSetterObservation(body,"angularVelocity",VectorDiagnostic(value));
            string suppression;
            if(ShouldSuppressRedundantSleepingKinematicMotion(
                state,body,value,true,out suppression)) {
                EndNativeKinematicFreezeSetterObservation(state,false,suppression);
                return;
            }
            body.angularVelocity=value;
            EndNativeKinematicFreezeSetterObservation(state,true,null);
        }

        public static void SetIsKinematicWithNativeFreezeObservation(Rigidbody body,bool value)
        {
            var state=BeginNativeKinematicFreezeSetterObservation(body,"isKinematic",value);
            body.isKinematic=value;
            EndNativeKinematicFreezeSetterObservation(state,true,null);
        }

        public static void SetUseGravityWithNativeFreezeObservation(Rigidbody body,bool value)
        {
            var state=BeginNativeKinematicFreezeSetterObservation(body,"useGravity",value);
            body.useGravity=value;
            EndNativeKinematicFreezeSetterObservation(state,true,null);
        }

        private static bool ShouldSuppressRedundantSleepingKinematicMotion(
            NativeFreezeSetterObservationState state,Rigidbody body,Vector3 value,
            bool angularSetter,out string reason)
        {
            reason=null;
            if(state==null||body==null||state.Row==null||state.Before==null||
                (state.Stage!="freeze-setter"&&state.Stage!="unfreeze-setter"))return false;
            var native=(NativeShapeCheckpoint)state.Before;
            Vector3 linear=body.velocity,angular=body.angularVelocity;
            Vector3 current=angularSetter?angular:linear;
            if(!state.Row.RawIsKinematic||!body.isKinematic||!body.IsSleeping()||
                linear.x!=0f||linear.y!=0f||linear.z!=0f||
                angular.x!=0f||angular.y!=0f||angular.z!=0f||
                value.x!=current.x||value.y!=current.y||value.z!=current.z||
                native.WakeCounterBits!=0||native.BufferedIsSleeping!=1||native.BodySimActive!=0||
                native.KinematicTarget.UnityIsKinematic!=1||native.KinematicTarget.PublicTargetValid!=0||
                native.KinematicTarget.BufferedTargetValid!=0||native.KinematicTarget.SimStateIsKinematic!=1||
                native.KinematicTarget.CoreTargetValid!=0||native.Lifecycle.LifecycleStable!=1)return false;
            reason=angularSetter
                ?"authoring-freeze-redundant-zero-angular-velocity-on-stable-targetless-sleeping-kinematic"
                :"authoring-freeze-redundant-zero-velocity-on-stable-targetless-sleeping-kinematic";
            return true;
        }

        private static NativeFreezeSetterObservationState BeginNativeKinematicFreezeSetterObservation(
            Rigidbody body,string setter,object requestedValue)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null||owner.nativeKinematicFreezeSetterScope==null
                ||!ReferenceEquals(owner.nativeKinematicFreezeSetterScope.Body,body))return null;
            var scope=owner.nativeKinematicFreezeSetterScope;
            try {
                return new NativeFreezeSetterObservationState {Row=scope.Row,Body=body,
                    Stage=owner.nativeKinematicFreezeSetterStage,Setter=setter,RequestedValue=requestedValue,
                    ManagedBefore=ManagedRigidbodyDiagnostic(body),
                    Before=owner.CaptureNativeShapeCheckpoint(body,scope.Row.Colliders)};
            }
            catch(Exception error) {
                owner.AddNativeKinematicFreezeSetterObservation(new Dictionary<string,object>{
                    {"stage",owner.nativeKinematicFreezeSetterStage??"<null>"},{"setter",setter},
                    {"unityFrame",Time.frameCount},{"entityId",scope.Row==null?-1:scope.Row.EntityId},
                    {"requested",requestedValue},{"captured",false},{"phase","before"},
                    {"error",error.ToString()}});
                return null;
            }
        }

        private static void EndNativeKinematicFreezeSetterObservation(NativeFreezeSetterObservationState state,
            bool setterInvoked,string suppressionReason)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner==null||state==null)return;
            try {
                var after=owner.CaptureNativeShapeCheckpoint(state.Body,state.Row.Colliders);
                owner.AddNativeKinematicFreezeSetterObservation(new Dictionary<string,object>{
                    {"stage",state.Stage??"<null>"},{"setter",state.Setter},{"unityFrame",Time.frameCount},
                    {"entityId",state.Row.EntityId},{"requested",state.RequestedValue},{"captured",true},
                    {"setterInvoked",setterInvoked},{"suppressionReason",suppressionReason},
                    {"managedBefore",state.ManagedBefore},{"managedAfter",ManagedRigidbodyDiagnostic(state.Body)},
                    {"before",owner.NativeBodyCheckpointDiagnostic(state.Row,
                        (NativeShapeCheckpoint)state.Before)},
                    {"after",owner.NativeBodyCheckpointDiagnostic(state.Row,after)}});
            }
            catch(Exception error) {
                owner.AddNativeKinematicFreezeSetterObservation(new Dictionary<string,object>{
                    {"stage",state.Stage??"<null>"},{"setter",state.Setter},{"unityFrame",Time.frameCount},
                    {"entityId",state.Row==null?-1:state.Row.EntityId},{"requested",state.RequestedValue},
                    {"setterInvoked",setterInvoked},{"suppressionReason",suppressionReason},
                    {"captured",false},{"phase","after"},{"error",error.ToString()}});
            }
        }

        private static object ManagedRigidbodyDiagnostic(Rigidbody body)
        {
            return new Dictionary<string,object>{{"velocity",VectorDiagnostic(body.velocity)},
                {"angularVelocity",VectorDiagnostic(body.angularVelocity)},
                {"isKinematic",body.isKinematic},{"useGravity",body.useGravity},
                {"isSleeping",body.IsSleeping()}};
        }

        private static object VectorDiagnostic(Vector3 value)
        {
            return new[]{value.x,value.y,value.z};
        }

        private void AddNativeKinematicFreezeSetterObservation(object value)
        {
            nativeKinematicFreezeSetterObservations.Add(value);
            if(nativeKinematicFreezeSetterObservations.Count>1024)
                nativeKinematicFreezeSetterObservations.RemoveAt(0);
        }

        private void RecordNativeKinematicFreezeTransition(string stage,NativeFreezeObservationState state)
        {
            try {
                var after=CaptureNativeShapeCheckpoint(state.Body,state.Row.Colliders);
                AddNativeKinematicFreezeObservation(new Dictionary<string,object>{{"stage",stage},
                    {"unityFrame",Time.frameCount},{"entityId",state.Row.EntityId},{"layer",state.Layer},
                    {"before",NativeBodyCheckpointDiagnostic(state.Row,(NativeShapeCheckpoint)state.Before)},
                    {"after",NativeBodyCheckpointDiagnostic(state.Row,after)}});
            }
            catch(Exception error){AddNativeKinematicFreezeFailure(stage+"-after",state.Row,error);}
        }

        private void AddNativeKinematicFreezeFailure(string stage,Snapshot row,Exception error)
        {
            AddNativeKinematicFreezeObservation(new Dictionary<string,object>{{"stage",stage},
                {"unityFrame",Time.frameCount},{"entityId",row==null?-1:row.EntityId},
                {"captured",false},{"error",error.ToString()}});
        }

        private void AddNativeKinematicFreezeObservation(object value)
        {
            nativeKinematicFreezeObservations.Add(value);
            if(nativeKinematicFreezeObservations.Count>256)nativeKinematicFreezeObservations.RemoveAt(0);
        }

        private void RecordNativeKinematicStage(Snapshot[] snapshots,string stage)
        {
            var bodies=new List<object>();
            if(snapshots==null)throw new ArgumentNullException("snapshots");
            foreach(var row in snapshots)
            {
                NativeShapeCheckpoint value;
                if(row==null||row.Body==null||!row.Body.isKinematic)continue;
                if(!nativeShapeCheckpoints.TryGetValue(row,out value)||value==null)
                    throw new InvalidOperationException("Native kinematic stage sidecar is missing for entity "+row.EntityId+".");
                bodies.Add(new Dictionary<string,object>{{"entityId",row.EntityId},
                    {"bodyInstanceId",row.Body.GetInstanceID()},{"rigidbody",NativeHex(value.RigidbodyPointer)},
                    {"wakeCounterBits",value.WakeCounterBits},{"bufferedIsSleeping",value.BufferedIsSleeping},
                    {"bodySimActive",value.BodySimActive},{"publicIsSleeping",row.Body.IsSleeping()},
                    {"actorPose",NativePoseDiagnostic(value.ActorPose)},
                    {"kinematicTarget",NativeKinematicTargetDiagnostic(value.KinematicTarget)},
                    {"lifecycle",NativeLifecycleDiagnostic(value.Lifecycle)}});
            }
            AddNativeKinematicStageObservation(new Dictionary<string,object>{{"stage",stage},
                {"bodyCount",bodies.Count},{"bodies",bodies.ToArray()},{"captureValidated",true}});
        }

        private void RecordNativePostMaintenanceComparison(Snapshot[] targets,Snapshot[] current)
        {
            if(targets==null||current==null)throw new ArgumentNullException(targets==null?"targets":"current");
            var byEntity=new Dictionary<int,Snapshot>();
            foreach(var row in targets) {
                if(row==null||byEntity.ContainsKey(row.EntityId))
                    throw new InvalidOperationException("Post-maintenance target body identity is invalid.");
                byEntity.Add(row.EntityId,row);
            }
            var bodies=new List<object>();bool allExact=targets.Length==current.Length;
            var deferredSeen=new HashSet<int>();bool deferredExact=true;
            var deferredTargetSeen=new HashSet<int>();bool deferredTargetExact=true;
            foreach(var row in current) {
                Snapshot targetRow;NativeShapeCheckpoint targetValue,currentValue;
                if(row==null||!byEntity.TryGetValue(row.EntityId,out targetRow)||
                    !nativeShapeCheckpoints.TryGetValue(targetRow,out targetValue)||targetValue==null||
                    !nativeShapeCheckpoints.TryGetValue(row,out currentValue)||currentValue==null)
                    throw new InvalidOperationException("Post-maintenance native sidecar is missing for a captured body.");
                bool deferred=pendingNativePostMaintenanceDeferredEntities.Contains(row.EntityId);
                bool deferredTarget=pendingNativePostMaintenanceTargetEntities.Contains(row.EntityId);
                bool identityExact=deferredTarget
                    ?SameRecreatedPendingKinematicIdentity(targetValue,currentValue)
                    :targetValue.RigidbodyPointer.Equals(currentValue.RigidbodyPointer)&&
                        targetValue.Lifecycle.Actor.Equals(currentValue.Lifecycle.Actor)&&
                        targetValue.Lifecycle.BodySim.Equals(currentValue.Lifecycle.BodySim)&&
                        targetValue.Lifecycle.BodyCore.Equals(currentValue.Lifecycle.BodyCore);
                bool poseExact=SameBits(targetValue.ActorPose,currentValue.ActorPose)&&
                    SameBits(targetValue.Body2Actor,currentValue.Body2Actor)&&
                    SameBits(targetValue.Body2World,currentValue.Body2World);
                bool wakeExact=targetValue.WakeCounterBits==currentValue.WakeCounterBits&&
                    targetValue.BufferedIsSleeping==currentValue.BufferedIsSleeping&&
                    targetValue.BodySimActive==currentValue.BodySimActive;
                bool lifecycleExact=deferred
                    ?SameDormantTargetlessKinematicLifecycleAfterAuthoringFreeze(
                        targetValue.Lifecycle,currentValue.Lifecycle,
                        targetRow.RawUseGravity,row.RawUseGravity)
                    :deferredTarget
                        ?SameRecreatedPendingToSettlingKinematicLifecycle(
                            targetValue,currentValue,targetRow.RawUseGravity,row.RawUseGravity)
                    :SameNativeLifecycle(targetValue.Lifecycle,currentValue.Lifecycle);
                bool kinematicTargetExact=targetRow.RawIsKinematic==row.RawIsKinematic&&
                    targetRow.RawUseGravity==row.RawUseGravity&&
                    (!targetRow.RawIsKinematic||(deferredTarget
                        ?PendingAtCurrentPoseKinematic(targetValue,targetRow.RawUseGravity)&&
                            SettlingTargetlessKinematic(currentValue,row.RawUseGravity)
                        :SameNativeKinematicTarget(
                            targetValue.KinematicTarget,currentValue.KinematicTarget)));
                bool shapeExact=SameNativeShapeState(targetValue,currentValue);
                bool exact=identityExact&&poseExact&&wakeExact&&lifecycleExact&&
                    kinematicTargetExact&&shapeExact;
                allExact&=exact;
                if(deferred){deferredSeen.Add(row.EntityId);deferredExact&=exact;}
                if(deferredTarget){deferredTargetSeen.Add(row.EntityId);deferredTargetExact&=exact;}
                bodies.Add(new Dictionary<string,object>{{"entityId",row.EntityId},{"exact",exact},
                    {"deferredKinematicSleep",deferred},
                    {"deferredKinematicTarget",deferredTarget},
                    {"lifecycleComparison",deferred
                        ?"actor-local-after-authoring-freeze":deferredTarget
                            ?"recreated-pending-target-after-one-maintenance":"whole-scene-exact"},
                    {"identityExact",identityExact},{"poseExact",poseExact},{"wakeExact",wakeExact},
                    {"lifecycleExact",lifecycleExact},{"kinematicTargetExact",kinematicTargetExact},
                    {"shapeExact",shapeExact},
                    {"target",NativeBodyCheckpointDiagnostic(targetRow,targetValue)},
                    {"current",NativeBodyCheckpointDiagnostic(row,currentValue)}});
            }
            AddNativePostMaintenanceComparison(new Dictionary<string,object>{
                {"unityFrame",Time.frameCount},{"targetLogicalFrame",nativeKinematicStageFrame},
                {"captured",true},{"allExact",allExact},{"bodyCount",bodies.Count},
                {"deferredKinematicSleepExact",deferredExact&&
                    deferredSeen.SetEquals(pendingNativePostMaintenanceDeferredEntities)},
                {"deferredKinematicTargetExact",deferredTargetExact&&
                    deferredTargetSeen.SetEquals(pendingNativePostMaintenanceTargetEntities)},
                {"bodies",bodies.ToArray()}});
            if(pendingNativePostMaintenanceDeferredEntities.Count!=0&&
                (!deferredExact||!deferredSeen.SetEquals(pendingNativePostMaintenanceDeferredEntities)))
                FailNativePostMaintenance("Deferred targetless kinematic actors did not converge exactly after one maintenance step.");
            else if(pendingNativePostMaintenanceTargetEntities.Count!=0&&
                (!deferredTargetExact||
                    !deferredTargetSeen.SetEquals(pendingNativePostMaintenanceTargetEntities)))
                FailNativePostMaintenance("Recreated pending-target kinematic actors did not converge exactly after one maintenance step.");
            else nativePostMaintenanceFailure=null;
            pendingNativePostMaintenanceDeferredEntities.Clear();
            pendingNativePostMaintenanceTargetEntities.Clear();
            deferredKinematicSleepEntities.Clear();
            deferredKinematicTargetEntities.Clear();
            deferredKinematicSleepPreimages.Clear();
        }

        private void FailNativePostMaintenance(string failure)
        {
            nativePostMaintenanceFailure=failure;
            StateInvalidityManager.InvalidReason="AUTHORING_BODY_POST_MAINTENANCE_FAILED: "+failure;
        }

        private object NativeBodyCheckpointDiagnostic(Snapshot row,NativeShapeCheckpoint value)
        {
            var result=new Dictionary<string,object>{{"rawIsKinematic",row.RawIsKinematic},
                {"bodyInstanceId",row.Body.GetInstanceID()},{"rigidbody",NativeHex(value.RigidbodyPointer)},
                {"wakeCounterBits",value.WakeCounterBits},{"bufferedIsSleeping",value.BufferedIsSleeping},
                {"bodySimActive",value.BodySimActive},{"publicIsSleeping",row.Body.IsSleeping()},
                {"actorPose",NativePoseDiagnostic(value.ActorPose)},
                {"body2Actor",NativePoseDiagnostic(value.Body2Actor)},
                {"body2World",NativePoseDiagnostic(value.Body2World)},
                {"lifecycle",NativeLifecycleDiagnostic(value.Lifecycle)}};
            if(row.RawIsKinematic)result["kinematicTarget"]=NativeKinematicTargetDiagnostic(value.KinematicTarget);
            return result;
        }

        private static bool SameNativeKinematicTarget(NativeKinematicTargetReceipt a,NativeKinematicTargetReceipt b)
        {
            return a.Actor.Equals(b.Actor)&&a.UnityIsKinematic==b.UnityIsKinematic&&
                a.PublicTargetValid==b.PublicTargetValid&&a.ScbBodyBufferFlags==b.ScbBodyBufferFlags&&
                a.BufferedTargetValid==b.BufferedTargetValid&&a.SimStateData.Equals(b.SimStateData)&&
                a.SimStateIsKinematic==b.SimStateIsKinematic&&a.CoreTargetValid==b.CoreTargetValid&&
                SameBits(a.Target,b.Target);
        }

        private static bool SameNativeLifecycle(NativeBody2WorldCaptureReceipt a,NativeBody2WorldCaptureReceipt b)
        {
            return a.Actor.Equals(b.Actor)&&a.Scene.Equals(b.Scene)&&a.ControlState==b.ControlState&&
                a.BodyBufferFlags==b.BodyBufferFlags&&a.SimulationRunning==b.SimulationRunning&&
                a.PhysicsBuffering==b.PhysicsBuffering&&a.BodySim.Equals(b.BodySim)&&
                a.BodyCore.Equals(b.BodyCore)&&a.BodyCoreBodySim.Equals(b.BodyCoreBodySim)&&
                a.BodyCoreFlags==b.BodyCoreFlags&&a.SimStateData.Equals(b.SimStateData)&&
                a.SimStateTargetValid==b.SimStateTargetValid&&a.InteractionScene.Equals(b.InteractionScene)&&
                a.ScScene.Equals(b.ScScene)&&a.SceneArrayIndex==b.SceneArrayIndex&&
                a.BodySimInternalFlags==b.BodySimInternalFlags&&a.VelocityModState==b.VelocityModState&&
                a.IslandHook==b.IslandHook&&a.ActiveBodiesData.Equals(b.ActiveBodiesData)&&
                a.ActiveBodiesCount==b.ActiveBodiesCount&&a.ActiveBodiesCapacity==b.ActiveBodiesCapacity&&
                a.ActiveTwoWayStart==b.ActiveTwoWayStart&&a.ActiveBodyAtSceneIndex.Equals(b.ActiveBodyAtSceneIndex)&&
                a.ActiveBodiesHash==b.ActiveBodiesHash&&a.IslandManager.Equals(b.IslandManager)&&
                a.IslandNodeData.Equals(b.IslandNodeData)&&a.IslandNodeOwner.Equals(b.IslandNodeOwner)&&
                a.IslandNodeIslandId==b.IslandNodeIslandId&&a.IslandNodeFlags==b.IslandNodeFlags&&
                a.KinematicBitmap.Equals(b.KinematicBitmap)&&a.KinematicChangeBitmap.Equals(b.KinematicChangeBitmap)&&
                a.NotReadyBitmap.Equals(b.NotReadyBitmap)&&a.NotReadyChangeBitmap.Equals(b.NotReadyChangeBitmap)&&
                a.KinematicBitmapMap.Equals(b.KinematicBitmapMap)&&
                a.KinematicChangeBitmapMap.Equals(b.KinematicChangeBitmapMap)&&
                a.NotReadyBitmapMap.Equals(b.NotReadyBitmapMap)&&
                a.NotReadyChangeBitmapMap.Equals(b.NotReadyChangeBitmapMap)&&
                a.KinematicBitmapWordCount==b.KinematicBitmapWordCount&&
                a.KinematicChangeBitmapWordCount==b.KinematicChangeBitmapWordCount&&
                a.NotReadyBitmapWordCount==b.NotReadyBitmapWordCount&&
                a.NotReadyChangeBitmapWordCount==b.NotReadyChangeBitmapWordCount&&
                a.KinematicBitmapWord==b.KinematicBitmapWord&&
                a.KinematicChangeBitmapWord==b.KinematicChangeBitmapWord&&
                a.NotReadyBitmapWord==b.NotReadyBitmapWord&&
                a.NotReadyChangeBitmapWord==b.NotReadyChangeBitmapWord&&
                a.KinematicBitmapBit==b.KinematicBitmapBit&&
                a.KinematicChangeBitmapBit==b.KinematicChangeBitmapBit&&
                a.NotReadyBitmapBit==b.NotReadyBitmapBit&&
                a.NotReadyChangeBitmapBit==b.NotReadyChangeBitmapBit&&
                a.IslandManagerFlags==b.IslandManagerFlags&&a.SleepBodiesData.Equals(b.SleepBodiesData)&&
                a.SleepBodiesCount==b.SleepBodiesCount&&a.SleepBodiesCapacity==b.SleepBodiesCapacity&&
                a.SleepBodiesHash==b.SleepBodiesHash&&a.SleepBodiesIndex==b.SleepBodiesIndex&&
                a.WokeBodiesData.Equals(b.WokeBodiesData)&&a.WokeBodiesCount==b.WokeBodiesCount&&
                a.WokeBodiesCapacity==b.WokeBodiesCapacity&&a.WokeBodiesHash==b.WokeBodiesHash&&
                a.WokeBodiesIndex==b.WokeBodiesIndex&&a.WokeBodyListValid==b.WokeBodyListValid&&
                a.SleepBodyListValid==b.SleepBodyListValid&&a.LifecycleStable==b.LifecycleStable;
        }

        private static bool SameDormantTargetlessKinematicLifecycleAfterAuthoringFreeze(
            NativeBody2WorldCaptureReceipt target,NativeBody2WorldCaptureReceipt current,
            bool targetRawUseGravity,bool currentRawUseGravity)
        {
            // The pause gate's one admitted maintenance step runs while all
            // other Rigidbody instances are temporarily frozen.  Therefore
            // scene-wide active/sleep/wake counts, hashes, dense-array words,
            // and two-way boundaries cannot equal the advancing checkpoint.
            // Prove the deferred body itself instead: exact owner/storage
            // identity, inactive scene slot, exact island node and membership
            // bits, no notification-list membership, and only the documented
            // BF_DISABLE_GRAVITY bit introduced by the authoring freeze.
            uint currentFlags=current.BodySimInternalFlags;
            bool flagsExact=currentFlags==target.BodySimInternalFlags||
                (!targetRawUseGravity&&!currentRawUseGravity&&
                    currentFlags==(target.BodySimInternalFlags|1u));
            return targetRawUseGravity==currentRawUseGravity&&flagsExact&&
                target.Actor.Equals(current.Actor)&&target.Scene.Equals(current.Scene)&&
                target.ControlState==current.ControlState&&target.BodyBufferFlags==current.BodyBufferFlags&&
                target.SimulationRunning==current.SimulationRunning&&
                target.PhysicsBuffering==current.PhysicsBuffering&&
                target.BodySim.Equals(current.BodySim)&&target.BodyCore.Equals(current.BodyCore)&&
                target.BodyCoreBodySim.Equals(current.BodyCoreBodySim)&&
                target.BodyCoreFlags==current.BodyCoreFlags&&
                target.SimStateData.Equals(current.SimStateData)&&
                target.SimStateTargetValid==current.SimStateTargetValid&&
                target.InteractionScene.Equals(current.InteractionScene)&&
                target.ScScene.Equals(current.ScScene)&&
                target.SceneArrayIndex==current.SceneArrayIndex&&
                current.SceneArrayIndex==uint.MaxValue-1&&
                target.VelocityModState==current.VelocityModState&&
                target.IslandHook==current.IslandHook&&
                current.ActiveBodyAtSceneIndex.Equals(UIntPtr.Zero)&&
                target.IslandManager.Equals(current.IslandManager)&&
                target.IslandNodeData.Equals(current.IslandNodeData)&&
                target.IslandNodeOwner.Equals(current.IslandNodeOwner)&&
                target.IslandNodeIslandId==current.IslandNodeIslandId&&
                target.IslandNodeFlags==current.IslandNodeFlags&&
                target.KinematicBitmap.Equals(current.KinematicBitmap)&&
                target.KinematicChangeBitmap.Equals(current.KinematicChangeBitmap)&&
                target.NotReadyBitmap.Equals(current.NotReadyBitmap)&&
                target.NotReadyChangeBitmap.Equals(current.NotReadyChangeBitmap)&&
                target.KinematicBitmapMap.Equals(current.KinematicBitmapMap)&&
                target.KinematicChangeBitmapMap.Equals(current.KinematicChangeBitmapMap)&&
                target.NotReadyBitmapMap.Equals(current.NotReadyBitmapMap)&&
                target.NotReadyChangeBitmapMap.Equals(current.NotReadyChangeBitmapMap)&&
                target.KinematicBitmapWordCount==current.KinematicBitmapWordCount&&
                target.KinematicChangeBitmapWordCount==current.KinematicChangeBitmapWordCount&&
                target.NotReadyBitmapWordCount==current.NotReadyBitmapWordCount&&
                target.NotReadyChangeBitmapWordCount==current.NotReadyChangeBitmapWordCount&&
                target.KinematicBitmapBit==current.KinematicBitmapBit&&
                target.KinematicChangeBitmapBit==current.KinematicChangeBitmapBit&&
                target.NotReadyBitmapBit==current.NotReadyBitmapBit&&
                target.NotReadyChangeBitmapBit==current.NotReadyChangeBitmapBit&&
                target.SleepBodiesIndex==current.SleepBodiesIndex&&
                target.WokeBodiesIndex==current.WokeBodiesIndex&&
                target.SleepBodyListValid==current.SleepBodyListValid&&
                target.WokeBodyListValid==current.WokeBodyListValid&&
                current.SleepBodiesIndex==uint.MaxValue&&current.WokeBodiesIndex==uint.MaxValue&&
                target.LifecycleStable==1&&current.LifecycleStable==1;
        }

        private static bool SameRecreatedPendingKinematicIdentity(
            NativeShapeCheckpoint target,NativeShapeCheckpoint current)
        {
            // This case deliberately compares two incarnations of one logical
            // colliderless proxy. Native addresses may differ, but each
            // incarnation must be internally self-consistent and owned by the
            // same PhysX scene. This does not admit cross-scene replacement.
            return target!=null&&current!=null&&
                !target.RigidbodyPointer.Equals(UIntPtr.Zero)&&
                !current.RigidbodyPointer.Equals(UIntPtr.Zero)&&
                !target.Lifecycle.Actor.Equals(UIntPtr.Zero)&&
                !current.Lifecycle.Actor.Equals(UIntPtr.Zero)&&
                target.KinematicTarget.Actor.Equals(target.Lifecycle.Actor)&&
                current.KinematicTarget.Actor.Equals(current.Lifecycle.Actor)&&
                target.Lifecycle.BodyCoreBodySim.Equals(target.Lifecycle.BodySim)&&
                current.Lifecycle.BodyCoreBodySim.Equals(current.Lifecycle.BodySim)&&
                target.Lifecycle.IslandNodeOwner.Equals(target.Lifecycle.BodySim)&&
                current.Lifecycle.IslandNodeOwner.Equals(current.Lifecycle.BodySim)&&
                target.Lifecycle.ActiveBodyAtSceneIndex.Equals(target.Lifecycle.BodySim)&&
                current.Lifecycle.ActiveBodyAtSceneIndex.Equals(current.Lifecycle.BodySim)&&
                target.KinematicTarget.SimStateData.Equals(target.Lifecycle.SimStateData)&&
                current.KinematicTarget.SimStateData.Equals(current.Lifecycle.SimStateData)&&
                target.Lifecycle.Scene.Equals(current.Lifecycle.Scene)&&
                target.Lifecycle.InteractionScene.Equals(current.Lifecycle.InteractionScene)&&
                target.Lifecycle.ScScene.Equals(current.Lifecycle.ScScene)&&
                target.Lifecycle.ControlState==current.Lifecycle.ControlState;
        }

        private static bool SameRecreatedPendingToSettlingKinematicLifecycle(
            NativeShapeCheckpoint target,NativeShapeCheckpoint current,
            bool targetRawUseGravity,bool currentRawUseGravity)
        {
            // setKinematicTarget reconstructs MOVED on the fresh actor. The
            // pause gate's single PhysX maintenance consumes that target and
            // leaves SETTLING, exactly as PhysX 3.3 does for the historical
            // MOVED|SETTLING checkpoint. Scene-wide dense arrays can have a
            // different order while the replacement actor is frozen, so prove
            // shared storage plus the complete actor-local lifecycle.
            var a=target.Lifecycle;var b=current.Lifecycle;
            return targetRawUseGravity==currentRawUseGravity&&
                PendingAtCurrentPoseKinematic(target,targetRawUseGravity)&&
                SettlingTargetlessKinematic(current,currentRawUseGravity)&&
                a.Scene.Equals(b.Scene)&&a.InteractionScene.Equals(b.InteractionScene)&&
                a.ScScene.Equals(b.ScScene)&&a.ControlState==b.ControlState&&
                a.BodyBufferFlags==b.BodyBufferFlags&&
                a.SimulationRunning==0&&b.SimulationRunning==0&&
                a.PhysicsBuffering==0&&b.PhysicsBuffering==0&&
                a.BodyCoreFlags==b.BodyCoreFlags&&a.VelocityModState==b.VelocityModState&&
                a.ActiveBodiesData.Equals(b.ActiveBodiesData)&&
                a.ActiveBodiesCapacity==b.ActiveBodiesCapacity&&
                a.IslandManager.Equals(b.IslandManager)&&
                a.IslandNodeData.Equals(b.IslandNodeData)&&
                a.KinematicBitmap.Equals(b.KinematicBitmap)&&
                a.KinematicChangeBitmap.Equals(b.KinematicChangeBitmap)&&
                a.NotReadyBitmap.Equals(b.NotReadyBitmap)&&
                a.NotReadyChangeBitmap.Equals(b.NotReadyChangeBitmap)&&
                a.KinematicBitmapMap.Equals(b.KinematicBitmapMap)&&
                a.KinematicChangeBitmapMap.Equals(b.KinematicChangeBitmapMap)&&
                a.NotReadyBitmapMap.Equals(b.NotReadyBitmapMap)&&
                a.NotReadyChangeBitmapMap.Equals(b.NotReadyChangeBitmapMap)&&
                a.KinematicBitmapWordCount==b.KinematicBitmapWordCount&&
                a.KinematicChangeBitmapWordCount==b.KinematicChangeBitmapWordCount&&
                a.NotReadyBitmapWordCount==b.NotReadyBitmapWordCount&&
                a.NotReadyChangeBitmapWordCount==b.NotReadyChangeBitmapWordCount&&
                a.SleepBodiesData.Equals(b.SleepBodiesData)&&
                a.SleepBodiesCapacity==b.SleepBodiesCapacity&&
                a.WokeBodiesData.Equals(b.WokeBodiesData)&&
                a.WokeBodiesCapacity==b.WokeBodiesCapacity&&
                a.SleepBodiesIndex==uint.MaxValue&&b.SleepBodiesIndex==uint.MaxValue&&
                a.WokeBodiesIndex==uint.MaxValue&&b.WokeBodiesIndex==uint.MaxValue&&
                a.SleepBodyListValid==1&&b.SleepBodyListValid==1&&
                a.WokeBodyListValid==1&&b.WokeBodyListValid==1&&
                a.LifecycleStable==1&&b.LifecycleStable==1;
        }

        private void AddNativePostMaintenanceComparison(object receipt)
        {
            nativePostMaintenanceComparisons.Add(receipt);
            if(nativePostMaintenanceComparisons.Count>8)nativePostMaintenanceComparisons.RemoveAt(0);
        }

        private void RecordNativeKinematicStageFailure(string stage,Exception error)
        {
            AddNativeKinematicStageObservation(new Dictionary<string,object>{{"stage",stage},
                {"captureValidated",false},{"error",error.ToString()}});
        }

        private void AddNativeKinematicStageObservation(object receipt)
        {
            nativeKinematicStageObservations.Add(receipt);
            if(nativeKinematicStageObservations.Count>32)
            {nativeKinematicStageObservations.RemoveAt(0);nativeKinematicStagesDiscarded++;}
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

        public static void BeforeRebindDestroyedColliderless(Snapshot __0,
            Team17.Online.Multiplayer.Messaging.EntitySerialisationEntry __1)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner!=null)owner.RestoreDestroyedColliderlessMode(__0,__1);
        }

        private void RestoreDestroyedColliderlessMode(Snapshot target,
            Team17.Online.Multiplayer.Messaging.EntitySerialisationEntry currentEntry)
        {
            var receipt=new Dictionary<string,object>{{"verified",false},
                {"entityId",target==null?-1:target.EntityId}};
            nativeDestroyedBodyModeRestores.Add(receipt);
            if(nativeDestroyedBodyModeRestores.Count>64)nativeDestroyedBodyModeRestores.RemoveAt(0);
            try {
                if(Thread.CurrentThread.ManagedThreadId!=moduleThread||target==null||currentEntry==null||
                    currentEntry.m_GameObject==null||currentEntry.m_Header.m_uEntityID!=target.EntityId||
                    target.Colliders==null||target.Colliders.Length!=0||!target.RawIsKinematic||
                    !Same(target.RawVelocity,Vector3.zero)||!Same(target.RawAngularVelocity,Vector3.zero))
                    throw new InvalidOperationException("Destroyed colliderless mode-restore target differs.");
                var body=currentEntry.m_GameObject.GetComponent<Rigidbody>();
                if(body==null||ReferenceEquals(body,target.Body)||body.isKinematic||
                    NativeBodyColliderCheckpoint.Capture(body).Length!=0||
                    !Same(body.velocity,Vector3.zero)||!Same(body.angularVelocity,Vector3.zero)||
                    !currentEntry.m_GameObject.GetComponents<Component>().Any(value=>
                        value!=null&&value.GetType().Name=="ObjectContainer"))
                    throw new InvalidOperationException("Destroyed colliderless replacement mode preimage differs.");
                var before=CaptureNativeShapeCheckpoint(body,new Shape[0]);
                receipt["before"]=NativeLiveBodyCheckpointDiagnostic(body,before);
                bool gravityBefore=body.useGravity;
                body.isKinematic=true;
                if(body.useGravity!=target.RawUseGravity)body.useGravity=target.RawUseGravity;
                var after=CaptureNativeShapeCheckpoint(body,new Shape[0]);
                receipt["after"]=NativeLiveBodyCheckpointDiagnostic(body,after);
                if(!body.isKinematic||body.useGravity!=target.RawUseGravity||
                    !Same(body.velocity,Vector3.zero)||!Same(body.angularVelocity,Vector3.zero)||
                    !before.RigidbodyPointer.Equals(after.RigidbodyPointer)||
                    !before.Lifecycle.Actor.Equals(after.Lifecycle.Actor)||
                    !before.Lifecycle.BodyCore.Equals(after.Lifecycle.BodyCore)||
                    !before.Lifecycle.BodySim.Equals(after.Lifecycle.BodySim)||
                    !SameNativeShapeState(before,after)||
                    !SameBits(before.ActorPose,after.ActorPose)||
                    !SameBits(before.Body2Actor,after.Body2Actor)||
                    !SameBits(before.Body2World,after.Body2World)||
                    after.KinematicTarget.UnityIsKinematic!=1||
                    after.KinematicTarget.PublicTargetValid!=0||
                    after.KinematicTarget.CoreTargetValid!=0)
                    throw new InvalidOperationException("Destroyed colliderless replacement mode restore changed an unowned invariant.");
                receipt["gravityBefore"]=gravityBefore;
                receipt["targetUseGravity"]=target.RawUseGravity;
                receipt["verified"]=true;
            }
            catch(Exception error){receipt["error"]=error.ToString();throw;}
        }

        private object NativeLiveBodyCheckpointDiagnostic(Rigidbody body,NativeShapeCheckpoint value)
        {
            var result=new Dictionary<string,object>{{"rawIsKinematic",body.isKinematic},
                {"rawUseGravity",body.useGravity},{"bodyInstanceId",body.GetInstanceID()},
                {"rigidbody",NativeHex(value.RigidbodyPointer)},
                {"wakeCounterBits",value.WakeCounterBits},{"bufferedIsSleeping",value.BufferedIsSleeping},
                {"bodySimActive",value.BodySimActive},{"publicIsSleeping",body.IsSleeping()},
                {"actorPose",NativePoseDiagnostic(value.ActorPose)},
                {"body2Actor",NativePoseDiagnostic(value.Body2Actor)},
                {"body2World",NativePoseDiagnostic(value.Body2World)},
                {"lifecycle",NativeLifecycleDiagnostic(value.Lifecycle)}};
            if(body.isKinematic)result["kinematicTarget"]=NativeKinematicTargetDiagnostic(value.KinematicTarget);
            return result;
        }

        public static void AfterRebindDestroyedColliderless(Snapshot __0,
            Team17.Online.Multiplayer.Messaging.EntitySerialisationEntry __1,Snapshot __result)
        {
            var owner=nativeShapeCaptureOwner;
            if(owner!=null)owner.RebindDestroyedColliderlessSidecar(__0,__result);
        }

        private void RebindDestroyedColliderlessSidecar(Snapshot historicalRow,Snapshot currentRow)
        {
            var receipt=new Dictionary<string,object>{{"verified",false},
                {"entityId",historicalRow==null?-1:historicalRow.EntityId}};
            nativeDestroyedBodyRebinds.Add(receipt);
            if(nativeDestroyedBodyRebinds.Count>64)nativeDestroyedBodyRebinds.RemoveAt(0);
            try {
                if(historicalRow==null||currentRow==null||ReferenceEquals(historicalRow,currentRow)||
                    historicalRow.EntityId!=currentRow.EntityId||ReferenceEquals(historicalRow.Body,null)||currentRow.Body==null||
                    ReferenceEquals(historicalRow.Body,currentRow.Body)||historicalRow.Colliders.Length!=0||
                    currentRow.Colliders.Length!=0)
                    throw new InvalidOperationException("Destroyed colliderless native sidecar rebind identity differs.");
                NativeShapeCheckpoint historical,current;
                if(!nativeShapeCheckpoints.TryGetValue(historicalRow,out historical)||historical==null||
                    !nativeShapeCheckpoints.TryGetValue(currentRow,out current)||current==null)
                    throw new InvalidOperationException("Destroyed colliderless native sidecar pair is missing.");
                if(!ReferenceEquals(historical.Body,historicalRow.Body)||
                    !ReferenceEquals(current.Body,currentRow.Body)||
                    historical.Shapes==null||historical.Poses==null||historical.Geometries==null||
                    historical.Colliders==null||historical.ColliderShapes==null||
                    current.Shapes==null||current.Poses==null||current.Geometries==null||
                    current.Colliders==null||current.ColliderShapes==null||
                    historical.Shapes.Length!=0||historical.Poses.Length!=0||historical.Geometries.Length!=0||
                    historical.Colliders.Length!=0||historical.ColliderShapes.Length!=0||
                    current.Shapes.Length!=0||current.Poses.Length!=0||current.Geometries.Length!=0||
                    current.Colliders.Length!=0||current.ColliderShapes.Length!=0)
                    throw new InvalidOperationException("Destroyed colliderless native sidecar topology differs.");
                IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(currentRow.Body);
                var expectedPointer=new UIntPtr(unchecked((uint)bodyPointer.ToInt32()));
                var expectedHistoricalActor=new NativeRigidPose {
                    Px=historicalRow.BodyPosition.x,Py=historicalRow.BodyPosition.y,Pz=historicalRow.BodyPosition.z,
                    Qx=historicalRow.BodyRotation.x,Qy=historicalRow.BodyRotation.y,
                    Qz=historicalRow.BodyRotation.z,Qw=historicalRow.BodyRotation.w};
                var freshLiveActor=new NativeRigidPose {
                    Px=currentRow.Body.position.x,Py=currentRow.Body.position.y,Pz=currentRow.Body.position.z,
                    Qx=currentRow.Body.rotation.x,Qy=currentRow.Body.rotation.y,
                    Qz=currentRow.Body.rotation.z,Qw=currentRow.Body.rotation.w};
                if(bodyPointer==IntPtr.Zero||!current.RigidbodyPointer.Equals(expectedPointer)||
                    !SameBits(historical.ActorPose,expectedHistoricalActor)||
                    !SameBits(current.ActorPose,freshLiveActor)||
                    !Finite(historical.ActorPose)||!Finite(historical.Body2Actor)||!Finite(historical.Body2World))
                    throw new InvalidOperationException("Destroyed colliderless native sidecar provenance differs.");
                var rebound=new NativeShapeCheckpoint {Body=currentRow.Body,
                    RigidbodyPointer=current.RigidbodyPointer,Shapes=new UIntPtr[0],
                    Poses=new NativeRigidPose[0],Geometries=new NativeShapeGeometry[0],
                    Colliders=new Collider[0],ColliderShapes=new UIntPtr[0],
                    ActorPose=historical.ActorPose,Body2Actor=historical.Body2Actor,
                    Body2World=historical.Body2World,WakeCounterBits=historical.WakeCounterBits,
                    BufferedIsSleeping=historical.BufferedIsSleeping,BodySimActive=historical.BodySimActive,
                    KinematicTarget=current.KinematicTarget,Lifecycle=current.Lifecycle};
                nativeShapeCheckpoints.Set(currentRow,rebound);
                receipt["historicalRigidbody"]=NativeHex(historical.RigidbodyPointer);
                receipt["currentRigidbody"]=NativeHex(current.RigidbodyPointer);
                receipt["nativeRigidbodyPointerReused"]=historical.RigidbodyPointer.Equals(current.RigidbodyPointer);
                receipt["historicalActorPose"]=NativePoseDiagnostic(historical.ActorPose);
                receipt["freshActorPoseBeforeRebind"]=NativePoseDiagnostic(current.ActorPose);
                receipt["historicalBody2Actor"]=NativePoseDiagnostic(historical.Body2Actor);
                receipt["historicalBody2World"]=NativePoseDiagnostic(historical.Body2World);
                receipt["verified"]=true;
            }
            catch(Exception error){receipt["error"]=error.ToString();throw;}
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
                    ||!ReferenceEquals(historical.Body,current.Body)||pendingRecreatedShapes.Count<1
                    ||pendingRecreatedShapes.Count>2)
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
                    currentNative.ColliderShapes,currentNative.Shapes,pairs,pairs.Length);
                var poses=(NativeRigidPose[])currentNative.Poses.Clone();
                var geometries=(NativeShapeGeometry[])currentNative.Geometries.Clone();
                int differingPoseRows=0,differingGeometryRows=0,recreatedRows=0;
                int differingSurvivingPoseRows=0,differingSurvivingGeometryRows=0;
                var differingSurvivingPoseIndices=new List<int>();
                var differingSurvivingGeometryIndices=new List<int>();
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
                    }
                    else
                    {
                        if(!historicalNative.Shapes[i].Equals(currentNative.Shapes[i]))
                            throw new InvalidOperationException("Native shape geometry surviving actor identity differs at index "+i+".");
                        if(!SameBits(historicalNative.Poses[i],currentNative.Poses[i]))
                        {differingSurvivingPoseRows++;differingSurvivingPoseIndices.Add(i);}
                        if(!SameBits(historicalNative.Geometries[i],currentNative.Geometries[i]))
                        {differingSurvivingGeometryRows++;differingSurvivingGeometryIndices.Add(i);}
                    }
                    // The current PxShape identities are retained, while the
                    // complete checkpointed native state is carried across the
                    // managed Collider reincarnation.  Surviving shapes may
                    // legitimately have changed during the abandoned future;
                    // RestoreNativeShapePoses writes and verifies these targets
                    // for every actor row before simulation resumes.
                    poses[i]=historicalNative.Poses[i];
                    geometries[i]=historicalNative.Geometries[i];
                }
                if(recreatedRows!=pairs.Length)
                    throw new InvalidOperationException("Native shape geometry actor plan does not contain every bounded recreated row.");
                var rebased=new NativeShapeCheckpoint {Body=currentNative.Body,RigidbodyPointer=currentNative.RigidbodyPointer,
                    Shapes=(UIntPtr[])currentNative.Shapes.Clone(),Poses=poses,
                    Geometries=geometries,Colliders=(Collider[])currentNative.Colliders.Clone(),
                    ColliderShapes=(UIntPtr[])currentNative.ColliderShapes.Clone(),
                    ActorPose=historicalNative.ActorPose,Body2Actor=historicalNative.Body2Actor,
                    Body2World=historicalNative.Body2World,WakeCounterBits=historicalNative.WakeCounterBits,
                    BufferedIsSleeping=historicalNative.BufferedIsSleeping,BodySimActive=historicalNative.BodySimActive,
                    KinematicTarget=historicalNative.KinematicTarget,Lifecycle=historicalNative.Lifecycle};
                nativeShapeCheckpoints.Set(result,rebased);
                receipt["entityId"]=result.EntityId;receipt["recreatedRows"]=recreatedRows;
                receipt["differingPoseRows"]=differingPoseRows;
                receipt["differingGeometryRows"]=differingGeometryRows;
                receipt["differingSurvivingPoseRows"]=differingSurvivingPoseRows;
                receipt["differingSurvivingGeometryRows"]=differingSurvivingGeometryRows;
                receipt["differingSurvivingPoseIndices"]=differingSurvivingPoseIndices.ToArray();
                receipt["differingSurvivingGeometryIndices"]=differingSurvivingGeometryIndices.ToArray();
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

        private NativeShapeCheckpoint PrepareSleepingKinematicNativePoseRestore(
            Snapshot row,NativeShapeCheckpoint target,out bool deferSleep)
        {
            deferSleep=false;
            if(row==null||target==null||row.Body==null||!row.RawIsKinematic||!row.Body.isKinematic||
                Same(row.Body.position,row.BodyPosition)||nativeRestoreBody2World==null||
                !SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))return null;
            var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
            if(!SameNativeShapeState(target,current)||
                !SleepingTargetlessKinematic(target)||
                row.Body.useGravity!=row.RawUseGravity||
                (!SleepingTargetlessKinematic(current)&&
                    !SettlingTargetlessKinematic(current,row.RawUseGravity))||
                !SameNativeKinematicTarget(target.KinematicTarget,current.KinematicTarget)||
                !target.RigidbodyPointer.Equals(current.RigidbodyPointer)||
                !target.Lifecycle.Actor.Equals(current.Lifecycle.Actor)||
                !target.Lifecycle.BodySim.Equals(current.Lifecycle.BodySim)||
                !target.Lifecycle.BodyCore.Equals(current.Lifecycle.BodyCore))return null;
            deferSleep=SettlingTargetlessKinematic(current,row.RawUseGravity);
            // Whole-scene active/sleep/wake queues and dirty bitmap words can
            // legitimately differ while preceding bodies in this restore pass
            // are being processed. They are not an admission condition for
            // moving this already stable, sleeping, targetless actor. The
            // mutation checks below still require its complete current
            // lifecycle receipt to remain bit-exact after every native/public
            // pose step and after synthetic-target invalidation.
            return current;
        }

        private bool TryRestoreSleepingKinematicPoseNatively(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint preTransform,bool deferSleep,Vector3 velocity,Vector3 angular,bool kinematic,
            bool gravity,long call)
        {
            if(preTransform==null)return false;
            var record=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                {"phase",deferSleep?"initial-settling-kinematic-body2world":"initial-sleeping-kinematic-body2world"},
                {"deferredSleep",deferSleep},{"exact",false},
                {"preTransform",NativeBodyCheckpointDiagnostic(row,preTransform)}};
            nativeSleepingKinematicPoseRestores.Add(record);
            if(nativeSleepingKinematicPoseRestores.Count>128)
                nativeSleepingKinematicPoseRestores.RemoveAt(0);
            try {
                // Move the targetless sleeping actor first. This can also make
                // Unity's Transform storage exact without a public setter. If a
                // setter is still required, Unity treats it as a new kinematic
                // target and wakes the public view. Admit only those two exact
                // outcomes and verify every native lifecycle field around them.
                RestoreCheckpointBody2World(row,target,velocity,angular,kinematic,gravity,
                    "initial-sleeping-kinematic-native-first",false);
                var afterNative=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                record["afterNativePose"]=NativeBodyCheckpointDiagnostic(row,afterNative);
                if(!SameNativeShapeState(target,afterNative)||
                    !SameBits(target.ActorPose,afterNative.ActorPose)||
                    !SameBits(target.Body2Actor,afterNative.Body2Actor)||
                    !SameBits(target.Body2World,afterNative.Body2World)||
                    preTransform.WakeCounterBits!=afterNative.WakeCounterBits||
                    preTransform.BufferedIsSleeping!=afterNative.BufferedIsSleeping||
                    preTransform.BodySimActive!=afterNative.BodySimActive||
                    !SameNativeKinematicTarget(preTransform.KinematicTarget,afterNative.KinematicTarget)||
                    !SameNativeLifecycle(preTransform.Lifecycle,afterNative.Lifecycle))
                    throw new InvalidOperationException("Native sleeping kinematic pose restore changed lifecycle state for "+row.EntityId+".");

                if(!Same(row.Transform.localPosition,row.LocalPosition))row.Transform.localPosition=row.LocalPosition;
                if(!Same(row.Transform.localRotation,row.LocalRotation))row.Transform.localRotation=row.LocalRotation;
                // Capture target diagnostics even if Unity's public sleep view
                // transiently differs; the explicit checks below remain strict.
                var afterTransform=CaptureNativeShapeCheckpoint(row.Body,row.Colliders,false);
                record["afterTransform"]=NativeBodyCheckpointDiagnostic(row,afterTransform);
                bool publicSleepingAfterTransform=row.Body.IsSleeping();
                bool transformTargetUnchanged=SameNativeKinematicTarget(
                    afterNative.KinematicTarget,afterTransform.KinematicTarget);
                bool syntheticTransformTarget=SyntheticTransformKinematicTargetOnly(
                    afterNative.KinematicTarget,afterTransform.KinematicTarget,target.ActorPose);
                bool expectedSleeping=preTransform.BufferedIsSleeping!=0;
                bool transformStorageNoOp=transformTargetUnchanged&&
                    publicSleepingAfterTransform==expectedSleeping;
                bool transformStorageCreatedTarget=syntheticTransformTarget&&!publicSleepingAfterTransform;
                record["transformStorageOutcome"]=transformStorageNoOp
                    ?"already-exact-targetless-sleeping"
                    :transformStorageCreatedTarget?"synthetic-target-created":"invalid";
                if(!SameNativeShapeState(afterNative,afterTransform)||
                    !SameBits(afterNative.ActorPose,afterTransform.ActorPose)||
                    !SameBits(afterNative.Body2Actor,afterTransform.Body2Actor)||
                    !SameBits(afterNative.Body2World,afterTransform.Body2World)||
                    afterNative.WakeCounterBits!=afterTransform.WakeCounterBits||
                    afterNative.BufferedIsSleeping!=afterTransform.BufferedIsSleeping||
                    afterNative.BodySimActive!=afterTransform.BodySimActive||
                    !SameNativeLifecycle(afterNative.Lifecycle,afterTransform.Lifecycle)||
                    (!transformStorageNoOp&&!transformStorageCreatedTarget)||
                    !Same(row.Body.position,row.BodyPosition)||!Same(row.Body.rotation,row.BodyRotation)||
                    !Same(row.Transform.localPosition,row.LocalPosition)||
                    !Same(row.Transform.localRotation,row.LocalRotation)||
                    !Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                    throw new InvalidOperationException("Transform storage restore changed native-first sleeping kinematic state for "+row.EntityId+".");

                if(transformStorageNoOp) {
                    record["targetInvalidationSkipped"]="Transform storage was already exact and no synthetic kinematic target existed.";
                    record["exact"]=true;return true;
                }

                NativeInvalidateKinematicTargetReceipt invalidate;
                int invalidateOk=nativeInvalidateKinematicTarget(new UIntPtr(unityPlayerBase),
                    target.RigidbodyPointer,out invalidate);
                var invalidateRecord=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                    {"rigidbody",NativeHex(invalidate.Rigidbody)},{"actor",NativeHex(invalidate.Actor)},
                    {"bodyCore",NativeHex(invalidate.BodyCore)},{"simStateData",NativeHex(invalidate.SimStateData)},
                    {"unityIsKinematic",invalidate.UnityIsKinematic},
                    {"simStateIsKinematic",invalidate.SimStateIsKinematic},
                    {"targetValidBefore",invalidate.TargetValidBefore},
                    {"targetValidAfter",invalidate.TargetValidAfter},{"result",invalidate.Result},
                    {"lastError",invalidate.LastError},{"exact",false}};
                nativeKinematicTargetInvalidations.Add(invalidateRecord);
                if(nativeKinematicTargetInvalidations.Count>128)
                    nativeKinematicTargetInvalidations.RemoveAt(0);
                record["targetInvalidation"]=invalidateRecord;
                if(invalidateOk!=1||invalidate.ApiVersion!=NativeHelperApiVersion||
                    invalidate.StructSize!=(uint)Marshal.SizeOf(typeof(NativeInvalidateKinematicTargetReceipt))||
                    invalidate.Result!=1||!invalidate.Rigidbody.Equals(target.RigidbodyPointer)||
                    !invalidate.Actor.Equals(afterNative.Lifecycle.Actor)||
                    !invalidate.BodyCore.Equals(afterNative.Lifecycle.BodyCore)||
                    !invalidate.SimStateData.Equals(afterNative.KinematicTarget.SimStateData)||
                    invalidate.UnityIsKinematic!=1||invalidate.SimStateIsKinematic!=1||
                    invalidate.TargetValidBefore!=1||invalidate.TargetValidAfter!=0)
                    throw new InvalidOperationException("Native synthetic kinematic-target invalidation failed for "+
                        row.EntityId+": result="+invalidate.Result+" error="+invalidate.LastError+".");
                var afterInvalidate=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                record["afterTargetInvalidation"]=NativeBodyCheckpointDiagnostic(row,afterInvalidate);
                if(!SameNativeShapeState(afterNative,afterInvalidate)||
                    !SameBits(afterNative.ActorPose,afterInvalidate.ActorPose)||
                    !SameBits(afterNative.Body2Actor,afterInvalidate.Body2Actor)||
                    !SameBits(afterNative.Body2World,afterInvalidate.Body2World)||
                    afterNative.WakeCounterBits!=afterInvalidate.WakeCounterBits||
                    afterNative.BufferedIsSleeping!=afterInvalidate.BufferedIsSleeping||
                    afterNative.BodySimActive!=afterInvalidate.BodySimActive||
                    !SameNativeKinematicTarget(afterNative.KinematicTarget,afterInvalidate.KinematicTarget)||
                    !SameNativeLifecycle(afterNative.Lifecycle,afterInvalidate.Lifecycle)||
                    row.Body.IsSleeping()!=expectedSleeping)
                    throw new InvalidOperationException("Native synthetic kinematic-target invalidation changed sleeping lifecycle for "+row.EntityId+".");
                invalidateRecord["exact"]=true;
                record["exact"]=true;return true;
            }
            catch(Exception error){record["error"]=error.ToString();throw;}
        }

        private static bool PendingAtCurrentPoseKinematic(
            NativeShapeCheckpoint value,bool rawUseGravity)
        {
            uint flags=value==null?0u:value.Lifecycle.BodySimInternalFlags;
            return value!=null&&value.WakeCounterBits==0x3ECCCCCCu&&
                value.BufferedIsSleeping==0&&value.BodySimActive==1&&
                value.KinematicTarget.UnityIsKinematic==1&&
                value.KinematicTarget.PublicTargetValid==1&&
                value.KinematicTarget.ScbBodyBufferFlags==0&&
                value.KinematicTarget.BufferedTargetValid==0&&
                value.KinematicTarget.SimStateIsKinematic==1&&
                value.KinematicTarget.CoreTargetValid==1&&
                SameBits(value.KinematicTarget.Target,value.ActorPose)&&
                value.KinematicTarget.Actor.Equals(value.Lifecycle.Actor)&&
                value.KinematicTarget.SimStateData.Equals(value.Lifecycle.SimStateData)&&
                value.Lifecycle.SimStateTargetValid==1&&
                (flags==0x204u||(!rawUseGravity&&flags==0x205u))&&
                value.Lifecycle.SceneArrayIndex<uint.MaxValue-1&&
                value.Lifecycle.ActiveBodyAtSceneIndex.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.BodyCoreBodySim.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.IslandNodeOwner.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.KinematicBitmapBit==1&&
                value.Lifecycle.KinematicChangeBitmapBit==0&&
                value.Lifecycle.NotReadyBitmapBit==1&&
                value.Lifecycle.NotReadyChangeBitmapBit==0&&
                (value.Lifecycle.IslandNodeFlags&0x19)==0x09&&
                value.Lifecycle.LifecycleStable==1;
        }

        private static bool FreshSleepingTargetlessKinematic(
            NativeShapeCheckpoint value,bool rawUseGravity)
        {
            uint flags=value==null?0u:value.Lifecycle.BodySimInternalFlags;
            return SleepingTargetlessKinematic(value)&&
                (flags==0x70u||(!rawUseGravity&&flags==0x71u))&&
                value.Lifecycle.SceneArrayIndex==uint.MaxValue-1&&
                value.Lifecycle.ActiveBodyAtSceneIndex.Equals(UIntPtr.Zero)&&
                value.Lifecycle.BodyCoreBodySim.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.IslandNodeOwner.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.KinematicBitmapBit==1&&
                value.Lifecycle.KinematicChangeBitmapBit==0&&
                value.Lifecycle.NotReadyBitmapBit==0&&
                value.Lifecycle.NotReadyChangeBitmapBit==0&&
                (value.Lifecycle.IslandNodeFlags&0x51)==0x51;
        }

        private static bool InstalledPendingKinematicTarget(
            NativeShapeCheckpoint value,bool rawUseGravity)
        {
            uint flags=value==null?0u:value.Lifecycle.BodySimInternalFlags;
            return value!=null&&value.WakeCounterBits==0x3ECCCCCCu&&
                value.BufferedIsSleeping==0&&value.BodySimActive==1&&
                value.KinematicTarget.UnityIsKinematic==1&&
                value.KinematicTarget.PublicTargetValid==1&&
                value.KinematicTarget.ScbBodyBufferFlags==0&&
                value.KinematicTarget.BufferedTargetValid==0&&
                value.KinematicTarget.SimStateIsKinematic==1&&
                value.KinematicTarget.CoreTargetValid==1&&
                value.KinematicTarget.Actor.Equals(value.Lifecycle.Actor)&&
                value.KinematicTarget.SimStateData.Equals(value.Lifecycle.SimStateData)&&
                value.Lifecycle.SimStateTargetValid==1&&
                (flags&0x4u)!=0&&(rawUseGravity?(flags&1u)==0:true)&&
                (flags&0x40u)==0&&
                value.Lifecycle.SceneArrayIndex<uint.MaxValue-1&&
                value.Lifecycle.ActiveBodyAtSceneIndex.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.BodyCoreBodySim.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.IslandNodeOwner.Equals(value.Lifecycle.BodySim)&&
                value.Lifecycle.KinematicBitmapBit==1&&
                value.Lifecycle.KinematicChangeBitmapBit==0&&
                value.Lifecycle.NotReadyBitmapBit==1&&
                value.Lifecycle.NotReadyChangeBitmapBit==0&&
                (value.Lifecycle.IslandNodeFlags&0x19)==0x09&&
                value.Lifecycle.LifecycleStable==1;
        }

        private void RestoreCheckpointKinematicTarget(
            Snapshot row,NativeShapeCheckpoint target,long call)
        {
            var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
            var record=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                {"phase","recreated-colliderless-pending-target"},{"exact",false},
                {"target",NativeBodyCheckpointDiagnostic(row,target)},
                {"before",NativeBodyCheckpointDiagnostic(row,current)}};
            nativeKinematicTargetRestores.Add(record);
            if(nativeKinematicTargetRestores.Count>128)nativeKinematicTargetRestores.RemoveAt(0);
            try {
                if(!PendingAtCurrentPoseKinematic(target,row.RawUseGravity)||
                    !SleepingTargetlessKinematic(current)||
                    !SameNativeShapeState(target,current)||
                    !SameBits(target.ActorPose,current.ActorPose)||
                    !SameBits(target.Body2Actor,current.Body2Actor)||
                    !SameBits(target.Body2World,current.Body2World)||
                    !target.Lifecycle.Scene.Equals(current.Lifecycle.Scene)||
                    !target.Lifecycle.InteractionScene.Equals(current.Lifecycle.InteractionScene)||
                    !target.Lifecycle.ScScene.Equals(current.Lifecycle.ScScene)||
                    row.Body.useGravity!=row.RawUseGravity||
                    !SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                    throw new InvalidOperationException(
                        "Pending kinematic-target restore preimage differs for "+row.EntityId+".");
                var pose=target.KinematicTarget.Target;
                NativeKinematicTargetReceipt receipt;
                int ok=nativeSetKinematicTarget(new UIntPtr(unityPlayerBase),
                    target.RigidbodyPointer,ref pose,out receipt);
                record["nativeReceipt"]=NativeKinematicTargetDiagnostic(receipt);
                if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||
                    receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeKinematicTargetReceipt))||
                    receipt.Result!=1||!receipt.Rigidbody.Equals(target.RigidbodyPointer)||
                    receipt.PublicTargetValid!=1||receipt.CoreTargetValid!=1||
                    !SameBits(receipt.Target,target.KinematicTarget.Target))
                    throw new InvalidOperationException(
                        "Native pending kinematic-target restore failed for "+row.EntityId+
                        ": result="+receipt.Result+" error="+receipt.LastError+".");
                var after=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
                record["after"]=NativeBodyCheckpointDiagnostic(row,after);
                if(!InstalledPendingKinematicTarget(after,row.RawUseGravity)||
                    !SameBits(after.KinematicTarget.Target,target.KinematicTarget.Target)||
                    after.WakeCounterBits!=target.WakeCounterBits||
                    !SameNativeShapeState(target,after)||
                    !SameBits(target.ActorPose,after.ActorPose)||
                    !SameBits(target.Body2Actor,after.Body2Actor)||
                    !SameBits(target.Body2World,after.Body2World)||
                    !target.Lifecycle.Scene.Equals(after.Lifecycle.Scene)||
                    !target.Lifecycle.InteractionScene.Equals(after.Lifecycle.InteractionScene)||
                    !target.Lifecycle.ScScene.Equals(after.Lifecycle.ScScene)||
                    !SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                    throw new InvalidOperationException(
                        "Pending kinematic-target restore readback differs for "+row.EntityId+".");
                record["exact"]=true;
            }
            catch(Exception error){record["error"]=error.ToString();throw;}
        }

        private static bool SleepingTargetlessKinematic(NativeShapeCheckpoint value)
        {
            return value!=null&&value.WakeCounterBits==0&&value.BufferedIsSleeping==1&&
                value.BodySimActive==0&&value.KinematicTarget.UnityIsKinematic==1&&
                value.KinematicTarget.PublicTargetValid==0&&value.KinematicTarget.ScbBodyBufferFlags==0&&
                value.KinematicTarget.BufferedTargetValid==0&&value.KinematicTarget.SimStateIsKinematic==1&&
                value.KinematicTarget.CoreTargetValid==0&&value.Lifecycle.LifecycleStable==1;
        }

        private static bool SettlingTargetlessKinematic(NativeShapeCheckpoint value,bool rawUseGravity)
        {
            // TimeManager's authoring freeze writes useGravity=false after the
            // advancing simulation has already entered PhysX's one-step
            // BF_KINEMATIC_SETTLING state.  PhysX represents that public
            // setting as BF_DISABLE_GRAVITY (bit 0), so the same settling
            // lifecycle can be observed as either 0x200 or 0x201.  Admit the
            // latter only for a body whose saved public setting is already
            // false; no forward gameplay property is changed here.
            uint expectedFlags=512u;
            uint currentFlags=value==null?0u:value.Lifecycle.BodySimInternalFlags;
            bool flagsExact=currentFlags==expectedFlags||
                (!rawUseGravity&&currentFlags==(expectedFlags|1u));
            return value!=null&&value.WakeCounterBits!=0&&value.BufferedIsSleeping==0&&
                value.BodySimActive==1&&value.KinematicTarget.UnityIsKinematic==1&&
                value.KinematicTarget.PublicTargetValid==0&&value.KinematicTarget.ScbBodyBufferFlags==0&&
                value.KinematicTarget.BufferedTargetValid==0&&value.KinematicTarget.SimStateIsKinematic==1&&
                value.KinematicTarget.CoreTargetValid==0&&value.Lifecycle.LifecycleStable==1&&
                value.Lifecycle.SceneArrayIndex<uint.MaxValue-1&&
                value.Lifecycle.ActiveBodyAtSceneIndex.Equals(value.Lifecycle.BodySim)&&
                flagsExact&&value.Lifecycle.NotReadyBitmapBit==1&&
                value.Lifecycle.NotReadyChangeBitmapBit==0&&
                value.Lifecycle.KinematicBitmapBit==1&&
                value.Lifecycle.KinematicChangeBitmapBit==0&&
                (value.Lifecycle.IslandNodeFlags&0x19)==0x09;
        }

        private void RequireDeferredKinematicMaintenance(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint preTransform,long call)
        {
            var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
            var receipt=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                {"phase","await-one-final-maintenance-step"},{"exact",false},
                {"target",NativeBodyCheckpointDiagnostic(row,target)},
                {"preTransform",NativeBodyCheckpointDiagnostic(row,preTransform)},
                {"current",NativeBodyCheckpointDiagnostic(row,current)}};
            nativeSleepingKinematicPoseRestores.Add(receipt);
            if(nativeSleepingKinematicPoseRestores.Count>128)
                nativeSleepingKinematicPoseRestores.RemoveAt(0);
            if(!SleepingTargetlessKinematic(target)||
                !SettlingTargetlessKinematic(current,row.RawUseGravity)||
                !SameNativeShapeState(target,current)||
                !SameBits(target.ActorPose,current.ActorPose)||
                !SameBits(target.Body2Actor,current.Body2Actor)||
                !SameBits(target.Body2World,current.Body2World)||
                !SameNativeKinematicTarget(target.KinematicTarget,current.KinematicTarget)||
                preTransform.WakeCounterBits!=current.WakeCounterBits||
                preTransform.BufferedIsSleeping!=current.BufferedIsSleeping||
                preTransform.BodySimActive!=current.BodySimActive||
                !SameNativeLifecycle(preTransform.Lifecycle,current.Lifecycle))
                throw new InvalidOperationException("Deferred targetless kinematic pre-maintenance state differs for "+row.EntityId+".");
            deferredKinematicSleepEntities.Add(row.EntityId);
            receipt["exact"]=true;
        }

        private void RequireDeferredKinematicMaintenanceAfterMass(
            Snapshot row,NativeShapeCheckpoint target,long call)
        {
            NativeShapeCheckpoint preimage;
            if(!deferredKinematicSleepPreimages.TryGetValue(row.EntityId,out preimage)||preimage==null)
                throw new InvalidOperationException(
                    "Deferred mass-changing kinematic preimage is missing for "+row.EntityId+".");
            var current=CaptureNativeShapeCheckpoint(row.Body,row.Colliders);
            var receipt=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                {"phase","after-mass-await-one-final-maintenance-step"},{"exact",false},
                {"target",NativeBodyCheckpointDiagnostic(row,target)},
                {"preTransform",NativeBodyCheckpointDiagnostic(row,preimage)},
                {"current",NativeBodyCheckpointDiagnostic(row,current)}};
            nativeSleepingKinematicPoseRestores.Add(receipt);
            if(nativeSleepingKinematicPoseRestores.Count>128)
                nativeSleepingKinematicPoseRestores.RemoveAt(0);
            if(!SleepingTargetlessKinematic(target)||
                !SettlingTargetlessKinematic(preimage,row.RawUseGravity)||
                !SettlingTargetlessKinematic(current,row.RawUseGravity)||
                current.WakeCounterBits!=preimage.WakeCounterBits||
                current.BufferedIsSleeping!=preimage.BufferedIsSleeping||
                current.BodySimActive!=preimage.BodySimActive||
                !SameNativeShapeState(target,current)||
                !SameBits(target.ActorPose,current.ActorPose)||
                !SameBits(target.Body2Actor,current.Body2Actor)||
                !SameBits(target.Body2World,current.Body2World)||
                !SameNativeKinematicTarget(target.KinematicTarget,current.KinematicTarget)||
                !target.RigidbodyPointer.Equals(current.RigidbodyPointer)||
                !target.Lifecycle.Actor.Equals(current.Lifecycle.Actor)||
                !target.Lifecycle.BodySim.Equals(current.Lifecycle.BodySim)||
                !target.Lifecycle.BodyCore.Equals(current.Lifecycle.BodyCore)||
                !target.Lifecycle.SimStateData.Equals(current.Lifecycle.SimStateData)||
                !target.Lifecycle.Scene.Equals(current.Lifecycle.Scene)||
                !target.Lifecycle.InteractionScene.Equals(current.Lifecycle.InteractionScene)||
                !target.Lifecycle.ScScene.Equals(current.Lifecycle.ScScene)||
                target.Lifecycle.ControlState!=current.Lifecycle.ControlState||
                target.Lifecycle.BodyBufferFlags!=current.Lifecycle.BodyBufferFlags||
                current.Lifecycle.SimulationRunning!=0||current.Lifecycle.PhysicsBuffering!=0||
                !SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                throw new InvalidOperationException(
                    "Deferred mass-changing targetless kinematic state differs for "+row.EntityId+".");
            receipt["exact"]=true;
        }

        private static bool SyntheticTransformKinematicTargetOnly(NativeKinematicTargetReceipt before,
            NativeKinematicTargetReceipt after,NativeRigidPose expectedTarget)
        {
            return before.UnityIsKinematic==1&&before.PublicTargetValid==0&&
                before.ScbBodyBufferFlags==0&&before.BufferedTargetValid==0&&
                before.SimStateIsKinematic==1&&before.CoreTargetValid==0&&
                after.Actor.Equals(before.Actor)&&after.UnityIsKinematic==1&&
                after.PublicTargetValid==1&&after.ScbBodyBufferFlags==0&&
                after.BufferedTargetValid==0&&after.SimStateData.Equals(before.SimStateData)&&
                after.SimStateIsKinematic==1&&after.CoreTargetValid==1&&
                SameBits(after.Target,expectedTarget);
        }

        private static bool SameNativeShapeState(NativeShapeCheckpoint a,NativeShapeCheckpoint b)
        {
            if(a==null||b==null||a.Shapes==null||b.Shapes==null||a.Poses==null||b.Poses==null||
                a.Geometries==null||b.Geometries==null||a.Shapes.Length!=b.Shapes.Length||
                a.Poses.Length!=b.Poses.Length||a.Geometries.Length!=b.Geometries.Length||
                a.Shapes.Length!=a.Poses.Length||a.Shapes.Length!=a.Geometries.Length)return false;
            for(int i=0;i<a.Shapes.Length;i++)
                if(!a.Shapes[i].Equals(b.Shapes[i])||!SameBits(a.Poses[i],b.Poses[i])||
                    !SameBits(a.Geometries[i],b.Geometries[i]))return false;
            return true;
        }

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
                latestNativeRowsByBody[pair.Key.Body]=pair.Key;
            }
            nativeShapePoseCaptures+=captured.Count;
            nativeShapePoseCaptureFailure=null;
        }

        private NativeShapeCheckpoint CaptureNativeShapeCheckpoint(Rigidbody body,
            SuperchargedPatch.NativeBodyColliderCheckpoint.Shape[] managedShapes,
            bool requirePublicStateExact=true)
        {
            IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(body);
            if(bodyPointer==IntPtr.Zero)throw new InvalidOperationException("Native shape checkpoint Rigidbody pointer is null.");
            var nativePointer=new UIntPtr(unchecked((uint)bodyPointer.ToInt32()));
            NativeBody2WorldCaptureReceipt bodyReceipt;
            int bodyOk=nativeCaptureBody2World(new UIntPtr(unityPlayerBase),nativePointer,out bodyReceipt);
            var bodyRecord=new Dictionary<string,object>{{"rigidbody",NativeHex(bodyReceipt.Rigidbody)},
                {"actor",NativeHex(bodyReceipt.Actor)},{"scene",NativeHex(bodyReceipt.Scene)},
                {"controlState",bodyReceipt.ControlState},{"bodyBufferFlags",bodyReceipt.BodyBufferFlags},
                {"simulationRunning",bodyReceipt.SimulationRunning},{"physicsBuffering",bodyReceipt.PhysicsBuffering},
                {"actorPose",NativePoseDiagnostic(bodyReceipt.ActorPose)},
                {"body2Actor",NativePoseDiagnostic(bodyReceipt.Body2Actor)},
                {"bufferedBody2World",NativePoseDiagnostic(bodyReceipt.BufferedBody2World)},
                {"coreBody2World",NativePoseDiagnostic(bodyReceipt.CoreBody2World)},
                {"bodyInstanceId",body.GetInstanceID()},{"bodySim",NativeHex(bodyReceipt.BodySim)},
                {"wakeCounterBufferedBits",bodyReceipt.WakeCounterBufferedBits},
                {"wakeCounterCoreBits",bodyReceipt.WakeCounterCoreBits},
                {"bufferedIsSleeping",bodyReceipt.BufferedIsSleeping},
                {"bodySimActive",bodyReceipt.BodySimActive},{"publicIsSleeping",body.IsSleeping()},
                {"lifecycle",NativeLifecycleDiagnostic(bodyReceipt)},
                {"result",bodyReceipt.Result},{"lastError",bodyReceipt.LastError},
                {"requirePublicStateExact",requirePublicStateExact},{"exact",false}};
            nativeBody2WorldCaptures.Add(bodyRecord);
            if(nativeBody2WorldCaptures.Count>128)nativeBody2WorldCaptures.RemoveAt(0);
            var publicPose=new NativeRigidPose {Px=body.position.x,Py=body.position.y,Pz=body.position.z,
                Qx=body.rotation.x,Qy=body.rotation.y,Qz=body.rotation.z,Qw=body.rotation.w};
            bodyRecord["publicPose"]=NativePoseDiagnostic(publicPose);
            bodyRecord["publicPoseExact"]=SameBits(bodyReceipt.ActorPose,publicPose);
            var captureProblems=new List<string>();
            if(bodyOk!=1)captureProblems.Add("call-result="+bodyOk);
            if(bodyReceipt.ApiVersion!=NativeHelperApiVersion)captureProblems.Add("api-version");
            if(bodyReceipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeBody2WorldCaptureReceipt)))
                captureProblems.Add("struct-size");
            if(bodyReceipt.Result!=1)captureProblems.Add("native-result="+bodyReceipt.Result);
            if(!bodyReceipt.Rigidbody.Equals(nativePointer))captureProblems.Add("rigidbody-identity");
            if(bodyReceipt.ControlState!=2)captureProblems.Add("control-state="+bodyReceipt.ControlState);
            if(bodyReceipt.BodyBufferFlags!=0)captureProblems.Add("body-buffer-flags="+bodyReceipt.BodyBufferFlags);
            if(bodyReceipt.SimulationRunning!=0)captureProblems.Add("simulation-running");
            if(bodyReceipt.PhysicsBuffering!=0)captureProblems.Add("physics-buffering");
            if(requirePublicStateExact&&!SameBits(bodyReceipt.ActorPose,publicPose))
                captureProblems.Add("public-pose");
            if(!SameBits(bodyReceipt.BufferedBody2World,bodyReceipt.CoreBody2World))
                captureProblems.Add("body2world-core");
            if(!Finite(bodyReceipt.ActorPose))captureProblems.Add("actor-pose-nonfinite");
            if(!Finite(bodyReceipt.Body2Actor))captureProblems.Add("body2actor-nonfinite");
            if(!Finite(bodyReceipt.BufferedBody2World))captureProblems.Add("body2world-nonfinite");
            if(bodyReceipt.BodySim.Equals(UIntPtr.Zero))captureProblems.Add("body-sim-null");
            if(bodyReceipt.WakeCounterBufferedBits!=bodyReceipt.WakeCounterCoreBits)
                captureProblems.Add("wake-counter-core");
            if(bodyReceipt.BufferedIsSleeping>1)captureProblems.Add("buffered-sleep-range");
            if(bodyReceipt.BodySimActive>1)captureProblems.Add("body-sim-active-range");
            if(bodyReceipt.BufferedIsSleeping==bodyReceipt.BodySimActive)
                captureProblems.Add("sleep-active-complement");
            if(bodyReceipt.LifecycleStable!=1)captureProblems.Add("lifecycle-unstable");
            if(bodyReceipt.BodyCore.Equals(UIntPtr.Zero))captureProblems.Add("body-core-null");
            if(!bodyReceipt.BodyCoreBodySim.Equals(bodyReceipt.BodySim))
                captureProblems.Add("body-core-body-sim");
            if(bodyReceipt.BodySimActive!=0&&!bodyReceipt.ActiveBodyAtSceneIndex.Equals(bodyReceipt.BodySim))
                captureProblems.Add("active-body-scene-slot");
            bool publicSleeping=body.IsSleeping();
            if(requirePublicStateExact&&(bodyReceipt.BufferedIsSleeping!=0)!=publicSleeping)
                captureProblems.Add("public-sleep="+publicSleeping+" native-sleep="+bodyReceipt.BufferedIsSleeping);
            if(captureProblems.Count!=0)
                throw new InvalidOperationException("Native body2World capture failed: result="+
                    bodyReceipt.Result+" error="+bodyReceipt.LastError+" problems="+
                    string.Join(",",captureProblems.ToArray()));
            NativeKinematicTargetReceipt kinematicTarget=default(NativeKinematicTargetReceipt);
            if(body.isKinematic)
            {
                int targetOk=nativeGetKinematicTarget(new UIntPtr(unityPlayerBase),nativePointer,out kinematicTarget);
                bodyRecord["kinematicTarget"]=NativeKinematicTargetDiagnostic(kinematicTarget);
                if(targetOk!=1||kinematicTarget.ApiVersion!=NativeHelperApiVersion||
                    kinematicTarget.StructSize!=(uint)Marshal.SizeOf(typeof(NativeKinematicTargetReceipt))||
                    kinematicTarget.Result!=1||!kinematicTarget.Rigidbody.Equals(nativePointer)||
                    !kinematicTarget.Actor.Equals(bodyReceipt.Actor)||kinematicTarget.UnityIsKinematic!=1||
                    kinematicTarget.PublicTargetValid>1||kinematicTarget.BufferedTargetValid>1||
                    kinematicTarget.SimStateData.Equals(UIntPtr.Zero)||kinematicTarget.SimStateIsKinematic!=1||
                    kinematicTarget.CoreTargetValid>1||
                    kinematicTarget.PublicTargetValid!=kinematicTarget.CoreTargetValid||
                    (kinematicTarget.PublicTargetValid!=0&&!Finite(kinematicTarget.Target)))
                    throw new InvalidOperationException("Native kinematic-target capture failed: result="+
                        kinematicTarget.Result+" error="+kinematicTarget.LastError);
            }
            bodyRecord["exact"]=true;
            int poseSize=Marshal.SizeOf(typeof(NativeRigidPose));
            int geometrySize=Marshal.SizeOf(typeof(NativeShapeGeometry));
            IntPtr shapes=Marshal.AllocHGlobal(MaximumNativeShapePoses*IntPtr.Size);
            IntPtr poses=Marshal.AllocHGlobal(MaximumNativeShapePoses*poseSize);
            IntPtr geometries=Marshal.AllocHGlobal(MaximumNativeShapePoses*geometrySize);
            try {
                uint count,error;
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
                    Poses=poseCopy,Geometries=geometryCopy,ActorPose=bodyReceipt.ActorPose,
                    Body2Actor=bodyReceipt.Body2Actor,Body2World=bodyReceipt.BufferedBody2World,
                    WakeCounterBits=bodyReceipt.WakeCounterBufferedBits,
                    BufferedIsSleeping=bodyReceipt.BufferedIsSleeping,BodySimActive=bodyReceipt.BodySimActive,
                    KinematicTarget=kinematicTarget,Lifecycle=bodyReceipt};
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
                if(row.Body.isKinematic&&(target.WakeCounterBits!=current.WakeCounterBits||
                    target.BufferedIsSleeping!=current.BufferedIsSleeping||target.BodySimActive!=current.BodySimActive))
                {
                    RecordNativeKinematicWakeMismatch(row,target,current);
                    bool deferredSleep=DeferredSleepingKinematicCandidate(row,target,current);
                    bool deferredTarget=DeferredPendingKinematicTargetCandidate(row,target,current);
                    if(deferredSleep) {
                        deferredKinematicSleepEntities.Add(row.EntityId);
                        deferredKinematicSleepPreimages[row.EntityId]=current;
                    }
                    if(deferredTarget)deferredKinematicTargetEntities.Add(row.EntityId);
                    if(!deferredSleep&&!deferredTarget)
                        throw new InvalidOperationException("Kinematic native wake state changed for entity "+row.EntityId+
                        ": target=(wake "+target.WakeCounterBits+", sleeping "+target.BufferedIsSleeping+
                        ", active "+target.BodySimActive+", publicTarget "+target.KinematicTarget.PublicTargetValid+
                        ", coreTarget "+target.KinematicTarget.CoreTargetValid+") current=(wake "+current.WakeCounterBits+
                        ", sleeping "+current.BufferedIsSleeping+", active "+current.BodySimActive+
                        ", publicTarget "+current.KinematicTarget.PublicTargetValid+", coreTarget "+
                        current.KinematicTarget.CoreTargetValid+").");
                }
                result.Add(row,target);
            }
            return result;
        }

        private bool DeferredSleepingKinematicCandidate(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint current)
        {
            return row!=null&&row.RawIsKinematic&&row.Body!=null&&row.Body.isKinematic&&
                row.Body.useGravity==row.RawUseGravity&&SleepingTargetlessKinematic(target)&&
                SettlingTargetlessKinematic(current,row.RawUseGravity)&&
                SameNativeShapeState(target,current)&&
                SameNativeKinematicTarget(target.KinematicTarget,current.KinematicTarget)&&
                target.RigidbodyPointer.Equals(current.RigidbodyPointer)&&
                target.Lifecycle.Actor.Equals(current.Lifecycle.Actor)&&
                target.Lifecycle.BodySim.Equals(current.Lifecycle.BodySim)&&
                target.Lifecycle.BodyCore.Equals(current.Lifecycle.BodyCore)&&
                target.Lifecycle.SimStateData.Equals(current.Lifecycle.SimStateData);
        }

        private bool DeferredPendingKinematicTargetCandidate(Snapshot row,
            NativeShapeCheckpoint target,NativeShapeCheckpoint current)
        {
            // The local dynamic scheduler can replace one colliderless proxy
            // with the same logical entity.  A freshly inserted replacement is
            // a stable sleeping, targetless kinematic, while the historical
            // checkpoint can be the exact pre-maintenance MOVED|SETTLING
            // phase produced by a redundant setKinematicTarget at its current
            // pose.  Admit only that source-derived phase pair; the official
            // PhysX setter below reconstructs the target and the pause gate's
            // one maintenance step must then converge to the proven settling
            // targetless state before gameplay resumes.
            return row!=null&&row.RawIsKinematic&&row.Body!=null&&row.Body.isKinematic&&
                row.Colliders!=null&&row.Colliders.Length==0&&
                row.Body.useGravity==row.RawUseGravity&&
                PendingAtCurrentPoseKinematic(target,row.RawUseGravity)&&
                FreshSleepingTargetlessKinematic(current,row.RawUseGravity)&&
                SameNativeShapeState(target,current)&&
                target.Shapes.Length==0&&
                target.RigidbodyPointer.Equals(current.RigidbodyPointer)&&
                target.Lifecycle.Scene.Equals(current.Lifecycle.Scene)&&
                target.Lifecycle.InteractionScene.Equals(current.Lifecycle.InteractionScene)&&
                target.Lifecycle.ScScene.Equals(current.Lifecycle.ScScene)&&
                target.Lifecycle.ControlState==current.Lifecycle.ControlState&&
                target.Lifecycle.BodyBufferFlags==current.Lifecycle.BodyBufferFlags&&
                target.Lifecycle.SimulationRunning==0&&current.Lifecycle.SimulationRunning==0&&
                target.Lifecycle.PhysicsBuffering==0&&current.Lifecycle.PhysicsBuffering==0;
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

        private void RecordNativeKinematicWakeMismatch(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint current)
        {
            nativeKinematicWakeMismatches.Add(new Dictionary<string,object>{{"entityId",row.EntityId},
                {"rigidbody",NativeHex(target.RigidbodyPointer)},
                {"targetWakeCounterBits",target.WakeCounterBits},
                {"targetBufferedIsSleeping",target.BufferedIsSleeping},{"targetBodySimActive",target.BodySimActive},
                {"currentWakeCounterBits",current.WakeCounterBits},
                {"currentBufferedIsSleeping",current.BufferedIsSleeping},{"currentBodySimActive",current.BodySimActive},
                {"targetActorPose",NativePoseDiagnostic(target.ActorPose)},
                {"currentActorPose",NativePoseDiagnostic(current.ActorPose)},
                {"targetKinematicTarget",NativeKinematicTargetDiagnostic(target.KinematicTarget)},
                {"currentKinematicTarget",NativeKinematicTargetDiagnostic(current.KinematicTarget)},
                {"targetLifecycle",NativeLifecycleDiagnostic(target.Lifecycle)},
                {"currentLifecycle",NativeLifecycleDiagnostic(current.Lifecycle)}});
            if(nativeKinematicWakeMismatches.Count>32)nativeKinematicWakeMismatches.RemoveAt(0);
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

        private static object NativeKinematicTargetDiagnostic(NativeKinematicTargetReceipt value)
        {
            return new Dictionary<string,object>{{"result",value.Result},{"lastError",value.LastError},
                {"actor",NativeHex(value.Actor)},{"unityIsKinematic",value.UnityIsKinematic},
                {"publicTargetValid",value.PublicTargetValid},{"scbBodyBufferFlags",value.ScbBodyBufferFlags},
                {"bufferedTargetValid",value.BufferedTargetValid},{"simStateData",NativeHex(value.SimStateData)},
                {"simStateIsKinematic",value.SimStateIsKinematic},{"coreTargetValid",value.CoreTargetValid},
                {"target",NativePoseDiagnostic(value.Target)}};
        }

        private static object NativeLifecycleDiagnostic(NativeBody2WorldCaptureReceipt value)
        {
            return new Dictionary<string,object>{
                {"bodyCore",NativeHex(value.BodyCore)},{"bodyCoreBodySim",NativeHex(value.BodyCoreBodySim)},
                {"bodyCoreFlags",value.BodyCoreFlags},{"simStateData",NativeHex(value.SimStateData)},
                {"simStateTargetValid",value.SimStateTargetValid},
                {"interactionScene",NativeHex(value.InteractionScene)},{"scScene",NativeHex(value.ScScene)},
                {"sceneArrayIndex",value.SceneArrayIndex},{"bodySimInternalFlags",value.BodySimInternalFlags},
                {"kinematicMoved",(value.BodySimInternalFlags&0x4)!=0},
                {"kinematicSettling",(value.BodySimInternalFlags&0x200)!=0},
                {"inSleepList",(value.BodySimInternalFlags&0x10)!=0},
                {"inWakeList",(value.BodySimInternalFlags&0x20)!=0},
                {"sleepNotify",(value.BodySimInternalFlags&0x40)!=0},
                {"wakeNotify",(value.BodySimInternalFlags&0x80)!=0},
                {"velocityModState",value.VelocityModState},{"islandHook",value.IslandHook},
                {"activeBodiesData",NativeHex(value.ActiveBodiesData)},
                {"activeBodiesCount",value.ActiveBodiesCount},{"activeBodiesCapacity",value.ActiveBodiesCapacity},
                {"activeTwoWayStart",value.ActiveTwoWayStart},
                {"activeBodyAtSceneIndex",NativeHex(value.ActiveBodyAtSceneIndex)},
                {"activeBodiesHash",value.ActiveBodiesHash},
                {"islandManager",NativeHex(value.IslandManager)},{"islandNodeData",NativeHex(value.IslandNodeData)},
                {"islandNodeOwner",NativeHex(value.IslandNodeOwner)},
                {"islandNodeIslandId",value.IslandNodeIslandId},{"islandNodeFlags",value.IslandNodeFlags},
                {"islandNodeKinematic",(value.IslandNodeFlags&0x1)!=0},
                {"islandNodeNotReadyForSleeping",(value.IslandNodeFlags&0x8)!=0},
                {"islandNodeInSleepingIsland",(value.IslandNodeFlags&0x10)!=0},
                {"kinematicBitmap",NativeHex(value.KinematicBitmap)},
                {"kinematicChangeBitmap",NativeHex(value.KinematicChangeBitmap)},
                {"notReadyBitmap",NativeHex(value.NotReadyBitmap)},
                {"notReadyChangeBitmap",NativeHex(value.NotReadyChangeBitmap)},
                {"kinematicBitmapMap",NativeHex(value.KinematicBitmapMap)},
                {"kinematicChangeBitmapMap",NativeHex(value.KinematicChangeBitmapMap)},
                {"notReadyBitmapMap",NativeHex(value.NotReadyBitmapMap)},
                {"notReadyChangeBitmapMap",NativeHex(value.NotReadyChangeBitmapMap)},
                {"kinematicBitmapWordCount",value.KinematicBitmapWordCount},
                {"kinematicChangeBitmapWordCount",value.KinematicChangeBitmapWordCount},
                {"notReadyBitmapWordCount",value.NotReadyBitmapWordCount},
                {"notReadyChangeBitmapWordCount",value.NotReadyChangeBitmapWordCount},
                {"kinematicBitmapWord",value.KinematicBitmapWord},
                {"kinematicChangeBitmapWord",value.KinematicChangeBitmapWord},
                {"notReadyBitmapWord",value.NotReadyBitmapWord},
                {"notReadyChangeBitmapWord",value.NotReadyChangeBitmapWord},
                {"kinematicBitmapBit",value.KinematicBitmapBit},
                {"kinematicChangeBitmapBit",value.KinematicChangeBitmapBit},
                {"notReadyBitmapBit",value.NotReadyBitmapBit},
                {"notReadyChangeBitmapBit",value.NotReadyChangeBitmapBit},
                {"islandManagerFlags",value.IslandManagerFlags},
                {"islandEverythingAsleep",(value.IslandManagerFlags&0xFF)!=0},
                {"islandHasAnythingChanged",((value.IslandManagerFlags>>8)&0xFF)!=0},
                {"islandPerformUpdate",((value.IslandManagerFlags>>16)&0xFF)!=0},
                {"sleepBodiesData",NativeHex(value.SleepBodiesData)},
                {"sleepBodiesCount",value.SleepBodiesCount},{"sleepBodiesCapacity",value.SleepBodiesCapacity},
                {"sleepBodiesHash",value.SleepBodiesHash},{"sleepBodiesIndex",value.SleepBodiesIndex},
                {"sleepBodiesContainsBody",value.SleepBodyListValid!=0&&value.SleepBodiesIndex!=uint.MaxValue},
                {"wokeBodiesData",NativeHex(value.WokeBodiesData)},
                {"wokeBodiesCount",value.WokeBodiesCount},{"wokeBodiesCapacity",value.WokeBodiesCapacity},
                {"wokeBodiesHash",value.WokeBodiesHash},{"wokeBodiesIndex",value.WokeBodiesIndex},
                {"wokeBodiesContainsBody",value.WokeBodyListValid!=0&&value.WokeBodiesIndex!=uint.MaxValue},
                {"wokeBodyListValid",value.WokeBodyListValid},{"sleepBodyListValid",value.SleepBodyListValid},
                {"stable",value.LifecycleStable}
            };
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
                if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeShapePoseRestoreReceipt))
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
