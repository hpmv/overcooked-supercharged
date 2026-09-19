using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEngine;
using SuperchargedPatch;
using SuperchargedPatch.Authoring;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;
using BodyInvariants=SuperchargedPatch.NativeBodyPoseCheckpoint.BodyInvariants;

namespace SuperchargedPatch.Authoring.Modules
{
    // This file is compiled into a separate, uniquely named DLL. The permanent
    // plugin never includes it. Native reset/pose algorithm revisions belong here.
    public sealed partial class BodyRestoreModule:IAuthoringModule,IBodyRestoreStrategy
    {
        private const uint NativeHelperApiVersion=11;
        private const string ExpectedUnityPlayerSha256="90E2FB176B133E3C1401FA21FD0C365DBED82AFD409912B824D222E66E868560";
        private const int MaximumRotationAssignments=4;
        private const int MaximumPositionAssignments=4;
        private const int MaximumJointPoseAssignments=5;
        private const float MaximumRotationResidual=0.000001f;
        private const float MaximumMassFrameResidual=0.00001f;
        private const float MaximumPoseMotionSideEffectResidual=0.000001f;
        private readonly List<object> rotationRestores=new List<object>();
        private readonly List<object> massRestores=new List<object>();
        private readonly List<object> nativePoseRestores=new List<object>();
        private readonly List<object> nativeMassFrameRestores=new List<object>();
        private readonly List<object> motionSideEffectRestores=new List<object>();
        private readonly List<object> nativeBody2WorldRestores=new List<object>();
        private readonly List<object> nativeWakeStateRestores=new List<object>();
        private readonly List<object> nativeKinematicTargetRestores=new List<object>();
        private readonly List<object> nativeKinematicTargetInvalidations=new List<object>();
        private long restoreCall,discardedRotationRecords;
        private IDisposable registration;
        private IntPtr nativeLibrary;
        private uint unityPlayerBase;
        private string nativePath,nativeSha256,unityPlayerSha256;
        private NativeSetGlobalPose nativeSetGlobalPose;
        private NativeSetMassFrame nativeSetMassFrame;
        private NativeCaptureBody2World nativeCaptureBody2World;
        private NativeRestoreBody2World nativeRestoreBody2World;
        private NativeRestoreWakeState nativeRestoreWakeState;
        private NativeGetKinematicTarget nativeGetKinematicTarget;
        private NativeSetKinematicTarget nativeSetKinematicTarget;
        private NativeInvalidateKinematicTarget nativeInvalidateKinematicTarget;
        private readonly FieldInfo cachedPtr=typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic);
        private bool disposed;
        public string Name {get{return "body-native-auto-reset-v48-deferred-settling-mass";}}
        public int ApiVersion {get{return 1;}}

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeRigidPose
        {
            internal float Px,Py,Pz,Qx,Qy,Qz,Qw;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeSetGlobalPoseReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor;
            internal NativeRigidPose Pose;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeBody2WorldCaptureReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor,Scene;
            internal uint ControlState,BodyBufferFlags,SimulationRunning,PhysicsBuffering;
            internal NativeRigidPose ActorPose,Body2Actor,BufferedBody2World,CoreBody2World;
            internal UIntPtr BodySim;
            internal uint WakeCounterBufferedBits,WakeCounterCoreBits,BufferedIsSleeping,BodySimActive;
            internal UIntPtr BodyCore,BodyCoreBodySim;
            internal uint BodyCoreFlags;
            internal UIntPtr SimStateData;
            internal uint SimStateTargetValid;
            internal UIntPtr InteractionScene,ScScene;
            internal uint SceneArrayIndex,BodySimInternalFlags,VelocityModState,IslandHook;
            internal UIntPtr ActiveBodiesData;
            internal uint ActiveBodiesCount,ActiveBodiesCapacity,ActiveTwoWayStart;
            internal UIntPtr ActiveBodyAtSceneIndex;
            internal uint ActiveBodiesHash;
            internal UIntPtr IslandManager,IslandNodeData,IslandNodeOwner;
            internal uint IslandNodeIslandId,IslandNodeFlags;
            internal UIntPtr KinematicBitmap,KinematicChangeBitmap,NotReadyBitmap,NotReadyChangeBitmap;
            internal UIntPtr KinematicBitmapMap,KinematicChangeBitmapMap,NotReadyBitmapMap,NotReadyChangeBitmapMap;
            internal uint KinematicBitmapWordCount,KinematicChangeBitmapWordCount,NotReadyBitmapWordCount,NotReadyChangeBitmapWordCount;
            internal uint KinematicBitmapWord,KinematicChangeBitmapWord,NotReadyBitmapWord,NotReadyChangeBitmapWord;
            internal uint KinematicBitmapBit,KinematicChangeBitmapBit,NotReadyBitmapBit,NotReadyChangeBitmapBit;
            internal uint IslandManagerFlags;
            internal UIntPtr SleepBodiesData;
            internal uint SleepBodiesCount,SleepBodiesCapacity,SleepBodiesHash,SleepBodiesIndex;
            internal UIntPtr WokeBodiesData;
            internal uint WokeBodiesCount,WokeBodiesCapacity,WokeBodiesHash,WokeBodiesIndex;
            internal uint WokeBodyListValid,SleepBodyListValid,LifecycleStable;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeBody2WorldRestoreReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor,SceneBefore,SceneAfter,ApiScene;
            internal uint DynamicTimestampBefore,DynamicTimestampAfter;
            internal uint ControlStateBefore,ControlStateAfter,BodyBufferFlagsBefore,BodyBufferFlagsAfter;
            internal uint SimulationRunningBefore,SimulationRunningAfter;
            internal uint PhysicsBufferingBefore,PhysicsBufferingAfter,Changed;
            internal NativeRigidPose ActorPoseBefore,ActorPoseTarget,ActorPoseAfter;
            internal NativeRigidPose Body2ActorBefore,Body2ActorAfter;
            internal NativeRigidPose BufferedBody2WorldBefore,CoreBody2WorldBefore,Body2WorldTarget;
            internal NativeRigidPose BufferedBody2WorldAfter,CoreBody2WorldAfter;
            internal UIntPtr BodySimBefore,BodySimAfter;
            internal uint WakeCounterBufferedBitsBefore,WakeCounterBufferedBitsAfter;
            internal uint WakeCounterCoreBitsBefore,WakeCounterCoreBitsAfter;
            internal uint BufferedIsSleepingBefore,BufferedIsSleepingAfter;
            internal uint BodySimActiveBefore,BodySimActiveAfter;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeWakeStateRestoreReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor,BodySim;
            internal uint TargetWakeCounterBits,TargetSleeping;
            internal uint WakeCounterBufferedBitsBefore,WakeCounterCoreBitsBefore;
            internal uint BufferedIsSleepingBefore,BodySimActiveBefore;
            internal uint WakeCounterBufferedBitsAfter,WakeCounterCoreBitsAfter;
            internal uint BufferedIsSleepingAfter,BodySimActiveAfter,CallMask;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeKinematicTargetReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor;
            internal uint UnityIsKinematic,PublicTargetValid,ScbBodyBufferFlags,BufferedTargetValid;
            internal UIntPtr SimStateData;
            internal uint SimStateIsKinematic,CoreTargetValid;
            internal NativeRigidPose Target;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeInvalidateKinematicTargetReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor,BodyCore,SimStateData;
            internal uint UnityIsKinematic,SimStateIsKinematic,TargetValidBefore,TargetValidAfter;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeRigidMassFrame
        {
            internal float Cx,Cy,Cz,Qx,Qy,Qz,Qw,Ix,Iy,Iz;
        }
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeSetMassFrameReceipt
        {
            internal uint ApiVersion,StructSize,Result,LastError;
            internal UIntPtr UnityBase,Rigidbody,Actor;
            internal uint AutomaticInertiaBefore,AutomaticCenterBefore,AutomaticInertiaAfter,AutomaticCenterAfter;
            internal NativeRigidMassFrame Frame;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NativeApiVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSetGlobalPose(
            UIntPtr unityBase,UIntPtr rigidbody,ref NativeRigidPose pose,out NativeSetGlobalPoseReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSetMassFrame(
            UIntPtr unityBase,UIntPtr rigidbody,ref NativeRigidMassFrame frame,out NativeSetMassFrameReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeCaptureBody2World(
            UIntPtr unityBase,UIntPtr rigidbody,out NativeBody2WorldCaptureReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeRestoreBody2World(
            UIntPtr unityBase,UIntPtr rigidbody,ref NativeRigidPose actorPose,ref NativeRigidPose body2Actor,
            ref NativeRigidPose body2World,out NativeBody2WorldRestoreReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeRestoreWakeState(
            UIntPtr unityBase,UIntPtr rigidbody,uint targetWakeCounterBits,uint targetSleeping,
            out NativeWakeStateRestoreReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeGetKinematicTarget(
            UIntPtr unityBase,UIntPtr rigidbody,out NativeKinematicTargetReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSetKinematicTarget(
            UIntPtr unityBase,UIntPtr rigidbody,ref NativeRigidPose pose,
            out NativeKinematicTargetReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeInvalidateKinematicTarget(
            UIntPtr unityBase,UIntPtr rigidbody,out NativeInvalidateKinematicTargetReceipt receipt);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("BodyRestoreModule");
            if(operation=="probe-empty-reset")return ProbeEmptyReset(args);
            if(operation=="activate") {
                ConfigureNativePose(args??new Dictionary<string,object>());
                ActivateNativeShapeCapture();
                try {
                    ActivateColliderAncestorPoseRestore();
                    var next=NativeBodyPoseCheckpoint.InstallRestoreStrategy(this);
                    var old=registration;registration=next;if(old!=null)old.Dispose();
                }
                catch {DeactivateColliderAncestorPoseRestore();DeactivateNativeShapeCapture();ReleaseNativePose();throw;}
            }
            else {
                if(args!=null&&args.Count!=0)throw new ArgumentException("Only activation accepts nativePath and sha256.");
                if(operation=="deactivate") {if(registration!=null)registration.Dispose();registration=null;DeactivateColliderAncestorPoseRestore();DeactivateNativeShapeCapture();ReleaseNativePose();}
                else if(operation!="status")throw new ArgumentException("Unknown body module operation: "+operation);
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"hasRegistrationLease",registration!=null},
                {"restoreCalls",restoreCall},{"massRestores",massRestores.ToArray()},{"rotationRestores",rotationRestores.ToArray()},
                {"discardedRotationRecords",discardedRotationRecords},{"lastEmptyResetProbe",lastEmptyResetProbe},
                {"nativePath",nativePath},{"nativeSha256",nativeSha256},{"nativeGlobalPoseActive",nativeSetGlobalPose!=null},
                {"unityPlayerSha256",unityPlayerSha256},
                {"nativeMassFrameActive",nativeSetMassFrame!=null},{"nativePoseRestores",nativePoseRestores.ToArray()},
                {"nativeMassFrameRestores",nativeMassFrameRestores.ToArray()},
                {"motionSideEffectRestores",motionSideEffectRestores.ToArray()},
                {"nativeBody2WorldRestores",nativeBody2WorldRestores.ToArray()},
                {"nativeSleepingKinematicPoseRestores",nativeSleepingKinematicPoseRestores.ToArray()},
                {"nativeKinematicTargetRestores",nativeKinematicTargetRestores.ToArray()},
                {"nativeKinematicTargetInvalidations",nativeKinematicTargetInvalidations.ToArray()},
                {"nativeWakeStateRestores",nativeWakeStateRestores.ToArray()},
                {"nativeBody2WorldCaptures",nativeBody2WorldCaptures.ToArray()},
                {"nativeKinematicWakeMismatches",nativeKinematicWakeMismatches.ToArray()},
                {"nativeKinematicStageDiagnostics",NativeKinematicStageDiagnostics},
                {"nativePostMaintenanceDiagnostics",NativePostMaintenanceDiagnostics},
                {"nativeKinematicFreezeDiagnostics",NativeKinematicFreezeDiagnostics},
                {"nativeShapePoseCaptureActive",NativeShapeCaptureActive},
                {"nativeShapePoseCaptures",nativeShapePoseCaptures},{"nativeShapePoseCaptureFailure",nativeShapePoseCaptureFailure},
                {"nativeShapePoseRestores",nativeShapePoseRestores.ToArray()},
                {"nativeShapeTopologyMismatches",nativeShapeTopologyMismatches.ToArray()},
                {"nativeShapeGeometryRebinds",nativeShapeGeometryRebinds.ToArray()},
                {"nativeDestroyedBodyRebinds",nativeDestroyedBodyRebinds.ToArray()},
                {"nativeDestroyedBodyModeRestores",nativeDestroyedBodyModeRestores.ToArray()},
                {"nativeShapeGeometryRebindPending",pendingRecreatedShapes.Count},
                {"nativeShapeGeometryRebindPoisoned",nativeShapeGeometryRebindPoisoned},
                {"nativeShapeGeometryRebindFailure",nativeShapeGeometryRebindFailure},
                {"colliderAncestorPoseRestoreActive",ColliderAncestorPoseRestoreActive},
                {"colliderAncestorPoseRestores",colliderAncestorPoseRestores.ToArray()},
                {"coreDispatch",NativeBodyPoseCheckpoint.RestoreStrategyDiagnostics}};
        }
        public void Dispose(){if(disposed)return;if(registration!=null)registration.Dispose();registration=null;DeactivateColliderAncestorPoseRestore();DeactivateNativeShapeCapture();ReleaseNativePose();disposed=true;}

        private void ConfigureNativePose(Dictionary<string,object> args)
        {
            if(args.Count==0)return;
            if(args.Count!=2||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256"))
                throw new ArgumentException("Body activation accepts only nativePath and sha256.");
            if(nativeLibrary!=IntPtr.Zero)throw new InvalidOperationException("Native body pose helper is already loaded.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expected=Convert.ToString(args["sha256"]).ToUpperInvariant();
            if(!File.Exists(path))throw new FileNotFoundException("Native body pose helper is missing.",path);
            string actual=Hash(path);
            if(actual!=expected)throw new InvalidOperationException("Native body pose helper hash mismatch: "+actual);
            if(IntPtr.Size!=4||cachedPtr==null)throw new InvalidOperationException("Native body pose restore requires the x86 Unity object ABI.");
            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(string.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            string unityHash=Hash(unity.FileName);
            if(unityHash!=ExpectedUnityPlayerSha256)
                throw new InvalidOperationException("UnityPlayer.dll hash mismatch: "+unityHash);
            unityPlayerBase=unchecked((uint)unity.BaseAddress.ToInt32());
            nativeLibrary=LoadLibrary(path);
            if(nativeLibrary==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try {
                var version=Export<NativeApiVersion>("oc2_rigidbody_rebuild_api_version");
                nativeSetGlobalPose=Export<NativeSetGlobalPose>("oc2_rigidbody_set_global_pose");
                nativeSetMassFrame=Export<NativeSetMassFrame>("oc2_rigidbody_set_mass_frame");
                nativeCaptureBody2World=Export<NativeCaptureBody2World>("oc2_rigidbody_capture_body2world");
                nativeRestoreBody2World=Export<NativeRestoreBody2World>("oc2_rigidbody_restore_body2world");
                nativeRestoreWakeState=Export<NativeRestoreWakeState>("oc2_rigidbody_restore_wake_state");
                nativeGetKinematicTarget=Export<NativeGetKinematicTarget>("oc2_rigidbody_get_kinematic_target");
                nativeSetKinematicTarget=Export<NativeSetKinematicTarget>(
                    "oc2_rigidbody_set_kinematic_target");
                nativeInvalidateKinematicTarget=Export<NativeInvalidateKinematicTarget>(
                    "oc2_rigidbody_invalidate_kinematic_target");
                nativeCaptureShapePoses=Export<NativeCaptureShapePoses>("oc2_rigidbody_capture_shape_poses");
                nativeRestoreShapePoses=Export<NativeRestoreShapePoses>("oc2_rigidbody_restore_shape_poses");
                if(version()!=NativeHelperApiVersion)throw new InvalidOperationException("Native body pose/mass/shape-state helper API version mismatch.");
                nativePath=path;nativeSha256=actual;unityPlayerSha256=unityHash;
            }
            catch {ReleaseNativePose();throw;}
        }

        private T Export<T>(string name) where T:class
        {
            IntPtr pointer=GetProcAddress(nativeLibrary,name);
            if(pointer==IntPtr.Zero)throw new MissingMethodException("Native export missing: "+name);
            return Marshal.GetDelegateForFunctionPointer(pointer,typeof(T)) as T;
        }

        private void ReleaseNativePose()
        {
            nativeSetGlobalPose=null;
            nativeSetMassFrame=null;
            nativeCaptureBody2World=null;
            nativeRestoreBody2World=null;
            nativeRestoreWakeState=null;
            nativeGetKinematicTarget=null;
            nativeSetKinematicTarget=null;
            nativeInvalidateKinematicTarget=null;
            nativeCaptureShapePoses=null;
            nativeRestoreShapePoses=null;
            if(nativeLibrary!=IntPtr.Zero){FreeLibrary(nativeLibrary);nativeLibrary=IntPtr.Zero;}
            unityPlayerBase=0;
            unityPlayerSha256=null;
        }

        private static string Hash(string path)
        {
            using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","");
        }
        private BodyInvariants CaptureInvariants(Rigidbody body){return NativeBodyPoseCheckpoint.ObserveBodyProperties(body);}
        private bool SameMassFrame(BodyInvariants a,BodyInvariants b)
        {
            return a.Mass==b.Mass&&Same(a.CenterOfMass,b.CenterOfMass)&&Same(a.InertiaTensor,b.InertiaTensor)
                &&Same(a.InertiaTensorRotation,b.InertiaTensorRotation);
        }
        private static bool PendingSleepNotificationLifecycle(
            NativeBody2WorldCaptureReceipt target,NativeBody2WorldCaptureReceipt current,
            bool rawUseGravity)
        {
            // PhysX Sc::BodySim::deactivateKinematic consumes
            // BF_KINEMATIC_SETTLING by calling notifyPutToSleep. Before the
            // scene dispatches/clears that notification, the actor is already
            // inactive and in its sleeping island but is present once in
            // mSleepBodies with BF_SLEEP_NOTIFY|BF_IS_IN_SLEEP_LIST. This is a
            // one-maintenance intermediate, not a replacement checkpoint.
            uint gravityFlag=rawUseGravity?0u:1u;
            return target.Actor.Equals(current.Actor)&&target.Scene.Equals(current.Scene)&&
                target.ControlState==current.ControlState&&
                target.BodyBufferFlags==current.BodyBufferFlags&&
                target.SimulationRunning==0&&current.SimulationRunning==0&&
                target.PhysicsBuffering==0&&current.PhysicsBuffering==0&&
                target.BodySim.Equals(current.BodySim)&&target.BodyCore.Equals(current.BodyCore)&&
                target.BodyCoreBodySim.Equals(current.BodyCoreBodySim)&&
                target.BodyCoreFlags==current.BodyCoreFlags&&
                target.SimStateData.Equals(current.SimStateData)&&
                target.SimStateTargetValid==0&&current.SimStateTargetValid==0&&
                target.InteractionScene.Equals(current.InteractionScene)&&
                target.ScScene.Equals(current.ScScene)&&
                target.SceneArrayIndex==uint.MaxValue-1&&
                current.SceneArrayIndex==uint.MaxValue-1&&
                target.BodySimInternalFlags==gravityFlag&&
                current.BodySimInternalFlags==(0x50u|gravityFlag)&&
                target.VelocityModState==current.VelocityModState&&
                target.IslandHook==current.IslandHook&&
                target.ActiveBodiesData.Equals(current.ActiveBodiesData)&&
                target.ActiveBodiesCapacity==current.ActiveBodiesCapacity&&
                current.ActiveBodyAtSceneIndex.Equals(UIntPtr.Zero)&&
                target.IslandManager.Equals(current.IslandManager)&&
                target.IslandNodeData.Equals(current.IslandNodeData)&&
                target.IslandNodeOwner.Equals(current.IslandNodeOwner)&&
                target.IslandNodeIslandId==current.IslandNodeIslandId&&
                target.IslandNodeFlags==0x11u&&current.IslandNodeFlags==0x11u&&
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
                target.KinematicBitmapBit==1&&current.KinematicBitmapBit==1&&
                target.KinematicChangeBitmapBit==0&&current.KinematicChangeBitmapBit==0&&
                target.NotReadyBitmapBit==0&&current.NotReadyBitmapBit==0&&
                target.NotReadyChangeBitmapBit==0&&current.NotReadyChangeBitmapBit==1&&
                current.SleepBodiesIndex!=uint.MaxValue&&
                current.SleepBodiesIndex<current.SleepBodiesCount&&
                current.WokeBodiesIndex==uint.MaxValue&&current.LifecycleStable==1;
        }
        private void ValidateInvariants(Snapshot row,bool requireMassFrame=false)
        {
            var a=row.Invariants;var b=CaptureInvariants(row.Body);
            if(a==null || (requireMassFrame&&!SameMassFrame(a,b)) || a.Drag!=b.Drag || a.AngularDrag!=b.AngularDrag
                || a.Constraints!=b.Constraints || a.Interpolation!=b.Interpolation || a.CollisionDetection!=b.CollisionDetection
                || a.DetectCollisions!=b.DetectCollisions || a.SleepThreshold!=b.SleepThreshold || a.MaxAngularVelocity!=b.MaxAngularVelocity)
                throw new InvalidOperationException("Initial native body "+row.EntityId+" "+(requireMassFrame?"mass/inertia/settings":"immutable settings")+" differ.");
        }

        public void Restore(Snapshot[] saved)
        {
            if(disposed)throw new ObjectDisposedException("BodyRestoreModule");
            deferredKinematicSleepEntities.Clear();
            deferredKinematicTargetEntities.Clear();
            deferredKinematicSleepPreimages.Clear();
            nativePostMaintenanceFailure=null;
            NativeBodyPoseCheckpoint.Validate(saved);
            var nativeShapeTargets=RequireNativeShapeTargets(saved);
            // Require target compound geometry before any mass recomputation.
            // Unchanged-mass early pose restoration precedes the cannon sidecar;
            // final verification checks its restored child hierarchy as well.
            foreach(var row in saved)
                if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                    NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,false);
            long call=++restoreCall;
            foreach (var row in saved)
            {
                var nativeTarget=nativeShapeTargets[row];
                var velocity=row.Body.velocity;var angular=row.Body.angularVelocity;
                bool kinematic=row.Body.isKinematic,gravity=row.Body.useGravity;
                bool admittedDeferredKinematicSleep=
                    deferredKinematicSleepEntities.Contains(row.EntityId);
                if(!Finite(velocity)||!Finite(angular))throw new InvalidOperationException("Nonfinite current native body velocity: "+row.EntityId);
                bool deferKinematicSleep;
                var sleepingKinematicPreimage=PrepareSleepingKinematicNativePoseRestore(
                    row,nativeTarget,out deferKinematicSleep);
                bool restoredSleepingKinematicPose=TryRestoreSleepingKinematicPoseNatively(
                    row,nativeTarget,sleepingKinematicPreimage,deferKinematicSleep,
                    velocity,angular,kinematic,gravity,call);
                if (!restoredSleepingKinematicPose) {
                    // Unity Rigidbody and Transform expose distinct stored poses.
                    // Restore the observed local transform before assigning the body;
                    // do not infer either pose from the other or synchronize PhysX.
                    if (!Same(row.Transform.localPosition, row.LocalPosition)) row.Transform.localPosition = row.LocalPosition;
                    if (!Same(row.Transform.localRotation, row.LocalRotation)) row.Transform.localRotation = row.LocalRotation;
                    if (!Same(row.Body.position, row.BodyPosition)) {
                        row.Body.position = row.BodyPosition;
                        if(!Same(row.Body.position,row.BodyPosition)&&nativeSetGlobalPose!=null)
                            RestoreExistingActorPose(row,null,velocity,angular,kinematic,gravity,"initial-position-fallback");
                    }
                }
                RestoreNativeShapePoses(row,nativeTarget,call,velocity,angular,kinematic,gravity);
                int provisionalAssignments=0;
                bool massFrameDiffers=!SameMassFrame(row.Invariants,CaptureInvariants(row.Body));
                // A restored compound hierarchy can leave PhysX with a pending
                // automatic mass-property refresh even when the public values
                // already equal the checkpoint. Consume that refresh now, then
                // restore the exact saved frame while preserving automatic mode.
                if(massFrameDiffers||(row.Colliders.Length>0&&row.HasAnalyticColliders)) {
                    provisionalAssignments=RecomputeMassFrame(row,nativeTarget,call,velocity,angular,kinematic,gravity);
                }
                if(nativeRestoreBody2World!=null) {
                    // Public Rigidbody pose can be an unreachable output of
                    // setGlobalPose after float32 COM composition. Restore the
                    // checkpoint's original Scb body2World preimage once the
                    // exact body2Actor/mass frame is back in place.
                    RestoreCheckpointBody2World(row,nativeTarget,velocity,angular,
                        kinematic,gravity,"final-after-mass");
                } else {
                    RestoreRotation(row,call,velocity,angular,kinematic,gravity,provisionalAssignments);
                    if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Body.rotation,row.BodyRotation)) {
                        if(nativeSetGlobalPose==null)
                            throw new InvalidOperationException("Exact final joint body-pose restore is unavailable: "+row.EntityId);
                        RestoreExistingActorPose(row,null,velocity,angular,kinematic,gravity,"final-after-mass",true);
                    }
                }
                if(nativeRestoreWakeState!=null) {
                    if(kinematic) {
                        if(deferredKinematicTargetEntities.Contains(row.EntityId))
                            RestoreCheckpointKinematicTarget(row,nativeTarget,call);
                        if(admittedDeferredKinematicSleep&&!deferKinematicSleep) {
                            RequireDeferredKinematicMaintenanceAfterMass(row,nativeTarget,call);
                            deferKinematicSleep=true;
                        }
                        bool targetSleeping=nativeTarget.BufferedIsSleeping!=0;
                        if(targetSleeping!=row.Body.IsSleeping()&&!deferKinematicSleep)
                            throw new InvalidOperationException("Kinematic native sleep state changed for "+row.EntityId+".");
                        if(deferKinematicSleep)RequireDeferredKinematicMaintenance(
                            row,nativeTarget,sleepingKinematicPreimage,call);
                    } else {
                        RestoreCheckpointWakeState(row,nativeTarget,velocity,angular,kinematic,gravity);
                    }
                } else if(nativeRestoreBody2World!=null) {
                    throw new InvalidOperationException("Native body2World restore requires exact wake-state support.");
                }
                ValidateUnchanged(row,velocity,angular,kinematic,gravity);
            }
        }

        private void ValidateUnchanged(Snapshot row,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            // Native pause temporarily changes these fields. Pin their values at
            // this restore call, rather than replacing them with advancing values.
            NativeBodyPoseCheckpoint.Validate(new[] {row});
            ValidateInvariants(row,true);
            if(!Same(row.Body.velocity,velocity)||!Same(row.Body.angularVelocity,angular)
                ||row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity)
                throw new InvalidOperationException("Native rotation restore changed non-pose physics state: "+row.EntityId);
        }

        private int RecomputeMassFrame(Snapshot row,NativeShapeCheckpoint nativeTarget,long call,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            var log=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},{"before",MassFrame(row.Body)},
                {"savedColliderCount",row.Colliders.Length},{"currentColliderCount",NativeBodyColliderCheckpoint.Capture(row.Body).Length},{"isKinematicBeforeReset",row.Body.isKinematic},
                {"savedRawIsKinematic",row.RawIsKinematic},{"currentVelocity",Point(row.Body.velocity)},
                {"currentAngularVelocity",Point(row.Body.angularVelocity)},{"savedRawVelocity",Point(row.RawVelocity)},
                {"savedRawAngularVelocity",Point(row.RawAngularVelocity)},
                {"hasObjectContainer",row.Object.GetComponents<Component>().Any(c=>c!=null&&c.GetType().Name=="ObjectContainer")},
                {"target",MassFrame(row.Invariants)},{"exact",false},{"resetCenterOfMass",false},{"resetInertiaTensor",false}};
            massRestores.Add(log);if(massRestores.Count>128)massRestores.RemoveAt(0);
            int assignments=0;
            try {
                if(!row.HasAnalyticColliders)
                    throw new InvalidOperationException("Automatic mass reset requires captured analytic collider geometry; mutable mesh contents are not checkpointed.");
                log["targetPose"]=TargetPose(row);log["beforeProvisionalRotationPose"]=CurrentPose(row);
                // The exact target transform is already installed. Put the
                // captured body orientation in place before native recompute;
                // a small setter readback residual is provisional, never success.
                if(!Same(row.Body.rotation,row.BodyRotation)) {
                    assignments=1;var rotationLog=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                        {"attempt",1},{"phase","before-native-mass-reset"},{"target",Rotation(row.BodyRotation)},
                        {"input",Rotation(row.BodyRotation)},{"exact",false}};
                    rotationRestores.Add(rotationLog);if(rotationRestores.Count>256){rotationRestores.RemoveAt(0);discardedRotationRecords++;}
                    row.Body.rotation=row.BodyRotation;var observed=row.Body.rotation;var residual=Subtract(row.BodyRotation,observed);
                    rotationLog["readback"]=DiagnosticRotation(observed);rotationLog["residual"]=DiagnosticRotation(residual);
                    rotationLog["exact"]=Same(observed,row.BodyRotation);
                    if(!Finite(observed)||!Finite(residual)||MaxAbs(residual)>MaximumRotationResidual)
                        throw new InvalidOperationException("Native provisional body pose is outside its exact-restoration envelope.");
                }
                log["afterProvisionalRotationPose"]=CurrentPose(row);
                NativeBodyPoseCheckpoint.Validate(new[]{row});RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                // X spawn/delete witness: assigning the captured rotation under
                // the future mass frame perturbed only Rigidbody.position.y.
                // R4 proves the position setter is also non-inverse under that
                // future mass frame. Bound preimage attempts, retain exact target
                // and never reset with a wrong pose or accept a small residual.
                if(assignments==1&&!Same(row.Body.position,row.BodyPosition)) {
                    var p=row.Body.position;var t=row.BodyPosition;
                    if(!Finite(p)||Math.Abs(p.x-t.x)>0.00001f||Math.Abs(p.y-t.y)>0.00001f||Math.Abs(p.z-t.z)>0.00001f
                        ||!Same(row.Transform.localPosition,row.LocalPosition)||!Same(row.Transform.localRotation,row.LocalRotation)
                        ||!Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                        throw new InvalidOperationException("Native body "+row.EntityId+" position reapplication lacks its bounded rotation-only pose prerequisite.");
                    log["positionReappliedAfterProvisionalRotation"]=true;
                    RestorePositionBeforeMassReset(row,log,velocity,angular,kinematic,gravity);
                    log["afterPositionReapplicationPose"]=CurrentPose(row);
                    NativeBodyPoseCheckpoint.Validate(new[]{row});RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                    NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                }
                var preMassPositionResidual=Subtract(row.Body.position,row.BodyPosition);
                var preMassRotationResidual=Subtract(row.Body.rotation,row.BodyRotation);
                if(!Finite(preMassPositionResidual)||!Finite(preMassRotationResidual)
                    ||MaxAbs(preMassPositionResidual)>MaximumMassFrameResidual
                    ||MaxAbs(preMassRotationResidual)>MaximumRotationResidual
                    ||!Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                    throw new InvalidOperationException("Native body "+row.EntityId+" body/transform pose is outside the bounded pre-mass-reset envelope.");
                log["boundedPreMassPose"] = new Dictionary<string,object> {
                    {"positionResidual",DiagnosticPoint(preMassPositionResidual)},
                    {"rotationResidual",DiagnosticRotation(preMassRotationResidual)},
                    {"positionExact",Same(row.Body.position,row.BodyPosition)},
                    {"rotationExact",Same(row.Body.rotation,row.BodyRotation)} };
                if(row.Colliders.Length==0)RestoreEmptyProxyMass(row,log,velocity,angular,kinematic,gravity);
                else {
                    if(row.Body.mass!=row.Invariants.Mass)row.Body.mass=row.Invariants.Mass;
                    row.Body.ResetCenterOfMass();log["resetCenterOfMass"]=true;log["afterResetCenterOfMass"]=MassFrame(row.Body);
                    row.Body.ResetInertiaTensor();log["resetInertiaTensor"]=true;log["afterResetInertiaTensor"]=MassFrame(row.Body);
                    if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body))) {
                        RestoreExactMassFrame(row,log,velocity,angular,kinematic,gravity);
                        if(!Same(row.Body.position,row.BodyPosition)
                            ||MaxAbs(Subtract(row.Body.rotation,row.BodyRotation))>0.0f) {
                            log["poseRepairAfterNativeMassFrame"]=true;
                            if(nativeRestoreBody2World!=null)
                                RestoreCheckpointBody2World(row,nativeTarget,velocity,angular,
                                    kinematic,gravity,"after-native-mass-frame");
                            else
                                RestoreExistingActorPose(row,log,velocity,angular,kinematic,gravity,"after-native-mass-frame");
                        }
                    }
                }
                RestoreBoundedMotionSideEffect(row,velocity,angular,kinematic,gravity,log,"automatic-mass-reset");
                log["after"]=MassFrame(row.Body);
                ValidateUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                log["exact"]=true;return assignments;
            }
            catch(Exception error){try{log["after"]=MassFrame(row.Body);}catch(Exception readError){log["afterError"]=readError.Message;}log["error"]=error.Message;throw;}
        }

        private void RestoreExactMassFrame(Snapshot row,Dictionary<string,object> log,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            if(nativeSetMassFrame==null)
                throw new InvalidOperationException("Exact native mass-frame correction is unavailable: "+row.EntityId);
            var before=CaptureInvariants(row.Body);
            // A checkpoint may intentionally lie between compound-shape removal
            // and Unity's later automatic mass refresh.  In that state the saved
            // mass frame is exact historical state, but ResetCenterOfMass and
            // ResetInertiaTensor immediately collapse it to the current topology.
            // Do not impose a small-residual prerequisite on the native restore:
            // target shape membership, geometry and local poses were preflighted
            // and restored exactly, and the native helper preserves and verifies
            // Unity's automatic-mode flags around the official PhysX setters.
            // Mass itself is assigned through Unity above and must already match.
            if(before.Mass!=row.Invariants.Mass)
                throw new InvalidOperationException("Automatic mass reset left a different mass before native exact restore: "+row.EntityId);
            log["nativeExactMassFrameResetResidual"]=new Dictionary<string,object> {
                {"centerOfMass",Point(Subtract(before.CenterOfMass,row.Invariants.CenterOfMass))},
                {"inertiaTensor",Point(Subtract(before.InertiaTensor,row.Invariants.InertiaTensor))},
                {"inertiaTensorRotation",Rotation(Subtract(before.InertiaTensorRotation,row.Invariants.InertiaTensorRotation))}};
            IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(row.Body);
            if(bodyPointer==IntPtr.Zero)throw new InvalidOperationException("Native Rigidbody pointer is null: "+row.EntityId);
            var target=row.Invariants;
            var nativeFrame=new NativeRigidMassFrame {Cx=target.CenterOfMass.x,Cy=target.CenterOfMass.y,Cz=target.CenterOfMass.z,
                Qx=target.InertiaTensorRotation.x,Qy=target.InertiaTensorRotation.y,Qz=target.InertiaTensorRotation.z,Qw=target.InertiaTensorRotation.w,
                Ix=target.InertiaTensor.x,Iy=target.InertiaTensor.y,Iz=target.InertiaTensor.z};
            NativeSetMassFrameReceipt receipt;
            int ok=nativeSetMassFrame(new UIntPtr(unityPlayerBase),
                new UIntPtr(unchecked((uint)bodyPointer.ToInt32())),ref nativeFrame,out receipt);
            var record=new Dictionary<string,object>{{"restoreCall",restoreCall},{"entityId",row.EntityId},
                {"before",MassFrame(before)},{"target",MassFrame(target)},
                {"unityBase","0x"+receipt.UnityBase.ToUInt32().ToString("X8")},
                {"rigidbody","0x"+receipt.Rigidbody.ToUInt32().ToString("X8")},
                {"actor","0x"+receipt.Actor.ToUInt32().ToString("X8")},
                {"result",receipt.Result},{"lastError",receipt.LastError},
                {"automaticInertiaBefore",receipt.AutomaticInertiaBefore},{"automaticCenterBefore",receipt.AutomaticCenterBefore},
                {"automaticInertiaAfter",receipt.AutomaticInertiaAfter},{"automaticCenterAfter",receipt.AutomaticCenterAfter},
                {"exact",false}};
            nativeMassFrameRestores.Add(record);if(nativeMassFrameRestores.Count>128)nativeMassFrameRestores.RemoveAt(0);
            log["nativeExactMassFrame"]=record;
            if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeSetMassFrameReceipt))
                ||receipt.Result!=1||receipt.AutomaticInertiaBefore!=1||receipt.AutomaticCenterBefore!=1
                ||receipt.AutomaticInertiaAfter!=1||receipt.AutomaticCenterAfter!=1)
                throw new InvalidOperationException("Native exact mass-frame restore failed for "+row.EntityId+
                    ": result="+receipt.Result+" error="+receipt.LastError);
            var after=CaptureInvariants(row.Body);record["after"]=MassFrame(after);
            RestoreBoundedMotionSideEffect(row,velocity,angular,kinematic,gravity,record,"native-exact-mass-frame");
            RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
            NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
            if(!SameMassFrame(target,after))
                throw new InvalidOperationException("Native exact mass-frame readback differs for "+row.EntityId);
            if(!Same(row.Transform.localPosition,row.LocalPosition)||!Same(row.Transform.localRotation,row.LocalRotation)
                ||!Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation)
                ||!Finite(row.Body.position)||!Finite(row.Body.rotation)
                ||MaxAbs(Subtract(row.Body.position,row.BodyPosition))>MaximumMassFrameResidual
                ||MaxAbs(Subtract(row.Body.rotation,row.BodyRotation))>MaximumRotationResidual)
                throw new InvalidOperationException("Native exact mass-frame restore changed pose outside its bounded envelope: "+row.EntityId);
            record["poseAfter"]=CurrentPose(row);record["exact"]=true;
        }

        private void RestoreCheckpointBody2World(Snapshot row,NativeShapeCheckpoint target,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity,string phase,
            bool requireTransformExact=true)
        {
            if(nativeRestoreBody2World==null||target==null)
                throw new InvalidOperationException("Exact native body2World restore is unavailable: "+row.EntityId);
            if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                throw new InvalidOperationException("Exact native body2World restore requires the checkpoint mass frame: "+row.EntityId);
            if(!Finite(target.ActorPose)||!Finite(target.Body2Actor)||!Finite(target.Body2World))
                throw new InvalidOperationException("Native body2World sidecar is nonfinite: "+row.EntityId);
            var expectedActorPose=new NativeRigidPose {Px=row.BodyPosition.x,Py=row.BodyPosition.y,Pz=row.BodyPosition.z,
                Qx=row.BodyRotation.x,Qy=row.BodyRotation.y,Qz=row.BodyRotation.z,Qw=row.BodyRotation.w};
            if(!SameBits(target.ActorPose,expectedActorPose)) {
                nativeBody2WorldRestores.Add(new Dictionary<string,object> {
                    {"restoreCall",restoreCall},{"entityId",row.EntityId},
                    {"phase",phase+"-actor-pose-preflight"},{"preflightMismatch",true},
                    {"targetActorPose",NativePoseDiagnostic(target.ActorPose)},
                    {"expectedManagedActorPose",NativePoseDiagnostic(expectedActorPose)},
                    {"targetBody2Actor",NativePoseDiagnostic(target.Body2Actor)},
                    {"targetBody2World",NativePoseDiagnostic(target.Body2World)},
                    {"currentPose",CurrentPose(row)},
                    {"savedRawIsKinematic",row.RawIsKinematic},
                    {"currentIsKinematic",row.Body.isKinematic}
                });
                if(nativeBody2WorldRestores.Count>128)nativeBody2WorldRestores.RemoveAt(0);
                throw new InvalidOperationException("Native actor-pose sidecar differs from its managed checkpoint: "+row.EntityId);
            }
            bool sleeping=row.Body.IsSleeping();
            var actorPose=target.ActorPose;var body2Actor=target.Body2Actor;var body2World=target.Body2World;
            NativeBody2WorldRestoreReceipt receipt;
            int ok=nativeRestoreBody2World(new UIntPtr(unityPlayerBase),target.RigidbodyPointer,
                ref actorPose,ref body2Actor,ref body2World,out receipt);
            var record=new Dictionary<string,object>{{"restoreCall",restoreCall},{"entityId",row.EntityId},
                {"phase",phase},{"rigidbody",NativeHex(receipt.Rigidbody)},{"actor",NativeHex(receipt.Actor)},
                {"requireTransformExact",requireTransformExact},
                {"sceneBefore",NativeHex(receipt.SceneBefore)},{"sceneAfter",NativeHex(receipt.SceneAfter)},
                {"apiScene",NativeHex(receipt.ApiScene)},{"controlStateBefore",receipt.ControlStateBefore},
                {"controlStateAfter",receipt.ControlStateAfter},{"bodyBufferFlagsBefore",receipt.BodyBufferFlagsBefore},
                {"bodyBufferFlagsAfter",receipt.BodyBufferFlagsAfter},{"simulationRunningBefore",receipt.SimulationRunningBefore},
                {"simulationRunningAfter",receipt.SimulationRunningAfter},{"physicsBufferingBefore",receipt.PhysicsBufferingBefore},
                {"physicsBufferingAfter",receipt.PhysicsBufferingAfter},{"dynamicTimestampBefore",receipt.DynamicTimestampBefore},
                {"dynamicTimestampAfter",receipt.DynamicTimestampAfter},{"mutationMask",receipt.Changed},
                {"changed",receipt.Changed!=0},
                {"actorPoseBefore",NativePoseDiagnostic(receipt.ActorPoseBefore)},
                {"actorPoseTarget",NativePoseDiagnostic(receipt.ActorPoseTarget)},
                {"actorPoseAfter",NativePoseDiagnostic(receipt.ActorPoseAfter)},
                {"body2ActorBefore",NativePoseDiagnostic(receipt.Body2ActorBefore)},
                {"body2ActorAfter",NativePoseDiagnostic(receipt.Body2ActorAfter)},
                {"body2WorldBefore",NativePoseDiagnostic(receipt.BufferedBody2WorldBefore)},
                {"body2WorldTarget",NativePoseDiagnostic(receipt.Body2WorldTarget)},
                {"body2WorldAfter",NativePoseDiagnostic(receipt.BufferedBody2WorldAfter)},
                {"bodySimBefore",NativeHex(receipt.BodySimBefore)},{"bodySimAfter",NativeHex(receipt.BodySimAfter)},
                {"wakeCounterBufferedBitsBefore",receipt.WakeCounterBufferedBitsBefore},
                {"wakeCounterBufferedBitsAfter",receipt.WakeCounterBufferedBitsAfter},
                {"wakeCounterCoreBitsBefore",receipt.WakeCounterCoreBitsBefore},
                {"wakeCounterCoreBitsAfter",receipt.WakeCounterCoreBitsAfter},
                {"bufferedIsSleepingBefore",receipt.BufferedIsSleepingBefore},
                {"bufferedIsSleepingAfter",receipt.BufferedIsSleepingAfter},
                {"bodySimActiveBefore",receipt.BodySimActiveBefore},
                {"bodySimActiveAfter",receipt.BodySimActiveAfter},
                {"result",receipt.Result},{"lastError",receipt.LastError},{"exact",false}};
            nativeBody2WorldRestores.Add(record);
            if(nativeBody2WorldRestores.Count>128)nativeBody2WorldRestores.RemoveAt(0);
            if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||
                receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeBody2WorldRestoreReceipt))||
                receipt.Result!=1||!receipt.Rigidbody.Equals(target.RigidbodyPointer)||
                receipt.SimulationRunningBefore!=0||receipt.SimulationRunningAfter!=0||
                receipt.PhysicsBufferingBefore!=0||receipt.PhysicsBufferingAfter!=0||
                !receipt.SceneBefore.Equals(receipt.SceneAfter)||
                receipt.ControlStateBefore!=2||receipt.ControlStateAfter!=2||
                receipt.BodyBufferFlagsBefore!=0||receipt.BodyBufferFlagsAfter!=0||
                !receipt.BodySimBefore.Equals(receipt.BodySimAfter)||
                receipt.WakeCounterBufferedBitsBefore!=receipt.WakeCounterBufferedBitsAfter||
                receipt.WakeCounterCoreBitsBefore!=receipt.WakeCounterCoreBitsAfter||
                receipt.BufferedIsSleepingBefore!=receipt.BufferedIsSleepingAfter||
                receipt.BodySimActiveBefore!=receipt.BodySimActiveAfter||
                !SameBits(receipt.ActorPoseTarget,target.ActorPose)||
                !SameBits(receipt.ActorPoseAfter,target.ActorPose)||
                !SameBits(receipt.Body2ActorBefore,target.Body2Actor)||
                !SameBits(receipt.Body2ActorAfter,target.Body2Actor)||
                !SameBits(receipt.BufferedBody2WorldAfter,target.Body2World)||
                !SameBits(receipt.CoreBody2WorldAfter,target.Body2World)||
                (receipt.Changed!=0&&(receipt.ApiScene.Equals(UIntPtr.Zero)||
                    receipt.DynamicTimestampAfter!=receipt.DynamicTimestampBefore+1u)))
                throw new InvalidOperationException("Native exact body2World restore failed for "+row.EntityId+
                    ": result="+receipt.Result+" error="+receipt.LastError);
            if(row.Body.IsSleeping()!=sleeping)
                throw new InvalidOperationException("Native exact body2World restore changed sleep state: "+row.EntityId);
            ValidateUnchanged(row,velocity,angular,kinematic,gravity);
            NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
            if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Body.rotation,row.BodyRotation)||
                (requireTransformExact&&(!Same(row.Transform.localPosition,row.LocalPosition)||
                !Same(row.Transform.localRotation,row.LocalRotation)||!Same(row.Transform.position,row.WorldPosition)||
                !Same(row.Transform.rotation,row.WorldRotation))))
                throw new InvalidOperationException("Native exact body2World restore did not reconstruct every public pose: "+row.EntityId);
            record["sleepingBeforeAfter"]=sleeping;record["exact"]=true;
        }

        private void RestoreCheckpointWakeState(Snapshot row,NativeShapeCheckpoint target,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            if(nativeRestoreWakeState==null||target==null)
                throw new InvalidOperationException("Exact native wake-state restore is unavailable: "+row.EntityId);
            if(kinematic)
                throw new InvalidOperationException("Native wake-state mutation is forbidden for a kinematic body: "+row.EntityId);
            if(target.BufferedIsSleeping>1||target.BodySimActive>1||
                target.BufferedIsSleeping==target.BodySimActive)
                throw new InvalidOperationException("Native wake-state sidecar is invalid: "+row.EntityId);
            NativeWakeStateRestoreReceipt receipt;
            int ok=nativeRestoreWakeState(new UIntPtr(unityPlayerBase),target.RigidbodyPointer,
                target.WakeCounterBits,target.BufferedIsSleeping,out receipt);
            var record=new Dictionary<string,object>{{"restoreCall",restoreCall},{"entityId",row.EntityId},
                {"rigidbody",NativeHex(receipt.Rigidbody)},{"actor",NativeHex(receipt.Actor)},
                {"bodySim",NativeHex(receipt.BodySim)},{"targetWakeCounterBits",receipt.TargetWakeCounterBits},
                {"targetSleeping",receipt.TargetSleeping},{"wakeCounterBufferedBitsBefore",receipt.WakeCounterBufferedBitsBefore},
                {"wakeCounterCoreBitsBefore",receipt.WakeCounterCoreBitsBefore},
                {"bufferedIsSleepingBefore",receipt.BufferedIsSleepingBefore},{"bodySimActiveBefore",receipt.BodySimActiveBefore},
                {"wakeCounterBufferedBitsAfter",receipt.WakeCounterBufferedBitsAfter},
                {"wakeCounterCoreBitsAfter",receipt.WakeCounterCoreBitsAfter},
                {"bufferedIsSleepingAfter",receipt.BufferedIsSleepingAfter},{"bodySimActiveAfter",receipt.BodySimActiveAfter},
                {"callMask",receipt.CallMask},{"result",receipt.Result},{"lastError",receipt.LastError},{"exact",false}};
            nativeWakeStateRestores.Add(record);
            if(nativeWakeStateRestores.Count>128)nativeWakeStateRestores.RemoveAt(0);
            uint expectedMask=0;
            bool beforeExact=receipt.WakeCounterBufferedBitsBefore==target.WakeCounterBits&&
                receipt.WakeCounterCoreBitsBefore==target.WakeCounterBits&&
                receipt.BufferedIsSleepingBefore==target.BufferedIsSleeping&&
                receipt.BodySimActiveBefore==target.BodySimActive;
            if(!beforeExact)expectedMask=receipt.BufferedIsSleepingBefore!=0&&
                (target.WakeCounterBits&0x7FFFFFFFu)==0?3u:1u;
            if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||
                receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeWakeStateRestoreReceipt))||receipt.Result!=1||
                !receipt.Rigidbody.Equals(target.RigidbodyPointer)||receipt.BodySim.Equals(UIntPtr.Zero)||
                receipt.TargetWakeCounterBits!=target.WakeCounterBits||receipt.TargetSleeping!=target.BufferedIsSleeping||
                receipt.WakeCounterBufferedBitsAfter!=target.WakeCounterBits||
                receipt.WakeCounterCoreBitsAfter!=target.WakeCounterBits||
                receipt.BufferedIsSleepingAfter!=target.BufferedIsSleeping||
                receipt.BodySimActiveAfter!=target.BodySimActive||receipt.CallMask!=expectedMask)
                throw new InvalidOperationException("Native exact wake-state restore failed for "+row.EntityId+
                    ": result="+receipt.Result+" error="+receipt.LastError);
            ValidateUnchanged(row,velocity,angular,kinematic,gravity);
            NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
            if((target.BufferedIsSleeping!=0)!=row.Body.IsSleeping()||
                !Same(row.Body.position,row.BodyPosition)||!Same(row.Body.rotation,row.BodyRotation)||
                !Same(row.Transform.localPosition,row.LocalPosition)||!Same(row.Transform.localRotation,row.LocalRotation)||
                !Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                throw new InvalidOperationException("Native exact wake-state restore changed public body state: "+row.EntityId);
            record["exact"]=true;
        }
        private void RequireMotionUnchanged(Snapshot row,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            if(!Same(row.Body.velocity,velocity)||!Same(row.Body.angularVelocity,angular)||row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity)
                throw new InvalidOperationException("Native mass reset changed non-mass physics state: "+row.EntityId);
        }
        private void RestorePositionBeforeMassReset(Snapshot row,Dictionary<string,object> log,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            if(nativeSetGlobalPose!=null)
            {
                RestoreExistingActorPose(row,log,velocity,angular,kinematic,gravity,"before-native-mass-reset");
                return;
            }
            var records=new List<object>();log["positionPreimageAttempts"]=records;
            var target=row.BodyPosition;var candidate=target;float previous=float.PositiveInfinity;
            var mass=CaptureInvariants(row.Body);
            for(int attempt=1;attempt<=MaximumPositionAssignments;attempt++) {
                var delta=new Vector3(candidate.x-target.x,candidate.y-target.y,candidate.z-target.z);
                if(!Finite(candidate)||MaxAbs(delta)>0.00001f)
                    throw new InvalidOperationException("Native position preimage escaped its bounded candidate envelope: "+row.EntityId);
                var record=new Dictionary<string,object>{{"attempt",attempt},{"target",Point(target)},{"input",Point(candidate)},{"exact",false}};
                records.Add(record);row.Body.position=candidate;
                var observed=row.Body.position;var residual=new Vector3(target.x-observed.x,target.y-observed.y,target.z-observed.z);
                record["pose"]=CurrentPose(row);record["readback"]=DiagnosticPoint(observed);record["residual"]=DiagnosticPoint(residual);
                NativeBodyPoseCheckpoint.Validate(new[]{row});ValidateInvariants(row);
                RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                if(!SameMassFrame(mass,CaptureInvariants(row.Body))||!Same(row.Transform.localPosition,row.LocalPosition)
                    ||!Same(row.Transform.localRotation,row.LocalRotation)||!Same(row.Transform.position,row.WorldPosition)
                    ||!Same(row.Transform.rotation,row.WorldRotation)||!Finite(row.Body.rotation)
                    ||MaxAbs(Subtract(row.BodyRotation,row.Body.rotation))>MaximumRotationResidual)
                    throw new InvalidOperationException("Native position preimage changed another required state: "+row.EntityId);
                if(Same(observed,target)){record["exact"]=true;return;}
                if(!Finite(observed)||!Finite(residual)||MaxAbs(residual)>0.00001f)
                    throw new InvalidOperationException("Native position readback residual is nonfinite or outside its bounded envelope: "+row.EntityId);
                float size=MaxAbs(residual);
                if(size>=previous)throw new InvalidOperationException("Native position preimage stagnated or worsened: "+row.EntityId);
                if(attempt==MaximumPositionAssignments)throw new InvalidOperationException("Native position preimage has no exact readback within four assignments: "+row.EntityId);
                var next=new Vector3(candidate.x+residual.x,candidate.y+residual.y,candidate.z+residual.z);
                if(Same(next,candidate))throw new InvalidOperationException("Native position preimage cannot progress at float precision: "+row.EntityId);
                candidate=next;previous=size;
            }
        }

        private void RestoreExistingActorPose(Snapshot row,Dictionary<string,object> log,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity,string phase,bool requireExactRotation=false)
        {
            IntPtr bodyPointer=(IntPtr)cachedPtr.GetValue(row.Body);
            if(bodyPointer==IntPtr.Zero)throw new InvalidOperationException("Native Rigidbody pointer is null: "+row.EntityId);
            var record=new Dictionary<string,object>{{"restoreCall",restoreCall},{"entityId",row.EntityId},
                {"phase",phase},{"requireExactRotation",requireExactRotation},
                {"target",TargetPose(row)},{"attempts",new List<object>()},{"readback",CurrentPose(row)},{"exact",false}};
            nativePoseRestores.Add(record);if(nativePoseRestores.Count>128)nativePoseRestores.RemoveAt(0);
            if(log!=null)log["nativeExistingActorPose-"+phase]=record;
            var attempts=(List<object>)record["attempts"];
            Vector3 candidatePosition=row.BodyPosition;
            Quaternion candidateRotation=row.BodyRotation;
            float previous=float.PositiveInfinity;
            for(int attempt=1;attempt<=MaximumJointPoseAssignments;attempt++) {
                var candidatePositionDelta=new Vector3(candidatePosition.x-row.BodyPosition.x,
                    candidatePosition.y-row.BodyPosition.y,candidatePosition.z-row.BodyPosition.z);
                var candidateRotationDelta=Subtract(candidateRotation,row.BodyRotation);
                if(!Finite(candidatePosition)||!Finite(candidateRotation)||MaxAbs(candidatePositionDelta)>0.00001f
                    ||MaxAbs(candidateRotationDelta)>MaximumRotationResidual)
                    throw new InvalidOperationException("Native existing-actor pose preimage escaped its bounded candidate envelope: "+row.EntityId);
                var candidate=new NativeRigidPose {Px=candidatePosition.x,Py=candidatePosition.y,Pz=candidatePosition.z,
                    Qx=candidateRotation.x,Qy=candidateRotation.y,Qz=candidateRotation.z,Qw=candidateRotation.w};
                NativeSetGlobalPoseReceipt receipt;
                int ok=nativeSetGlobalPose(new UIntPtr(unityPlayerBase),
                    new UIntPtr(unchecked((uint)bodyPointer.ToInt32())),ref candidate,out receipt);
                var attemptRecord=new Dictionary<string,object>{{"attempt",attempt},
                    {"inputPosition",Point(candidatePosition)},{"inputRotation",Rotation(candidateRotation)},
                    {"unityBase","0x"+receipt.UnityBase.ToUInt32().ToString("X8")},
                    {"rigidbody","0x"+receipt.Rigidbody.ToUInt32().ToString("X8")},
                    {"actor","0x"+receipt.Actor.ToUInt32().ToString("X8")},
                    {"result",receipt.Result},{"lastError",receipt.LastError},{"exact",false}};
                attempts.Add(attemptRecord);
                if(ok!=1||receipt.ApiVersion!=NativeHelperApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeSetGlobalPoseReceipt))||receipt.Result!=1)
                    throw new InvalidOperationException("Native existing-actor pose restore failed for "+row.EntityId+
                        ": result="+receipt.Result+" error="+receipt.LastError);
                NativeBodyPoseCheckpoint.Validate(new[]{row});
                ValidateInvariants(row);
                RestoreBoundedMotionSideEffect(row,velocity,angular,kinematic,gravity,attemptRecord,"native-existing-actor-pose");
                RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                Vector3 observedPosition=row.Body.position;
                Quaternion observedRotation=row.Body.rotation;
                Vector3 positionResidual=new Vector3(row.BodyPosition.x-observedPosition.x,
                    row.BodyPosition.y-observedPosition.y,row.BodyPosition.z-observedPosition.z);
                Quaternion rotationResidual=Subtract(row.BodyRotation,observedRotation);
                attemptRecord["readback"]=CurrentPose(row);
                attemptRecord["positionResidual"]=DiagnosticPoint(positionResidual);
                attemptRecord["rotationResidual"]=DiagnosticRotation(rotationResidual);
                record["readback"]=attemptRecord["readback"];
                if(!Same(row.Transform.localPosition,row.LocalPosition)||!Same(row.Transform.localRotation,row.LocalRotation)
                    ||!Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                    throw new InvalidOperationException("Native existing-actor pose changed the restored transform: "+row.EntityId);
                if(!Finite(observedPosition)||!Finite(observedRotation)||!Finite(positionResidual)||!Finite(rotationResidual)
                    ||MaxAbs(positionResidual)>0.00001f||MaxAbs(rotationResidual)>MaximumRotationResidual)
                    throw new InvalidOperationException("Native existing-actor pose residual is nonfinite or outside its bounded envelope: "+row.EntityId);
                bool positionExact=Same(observedPosition,row.BodyPosition);
                bool rotationExact=Same(observedRotation,row.BodyRotation);
                attemptRecord["positionExact"]=positionExact;attemptRecord["rotationExact"]=rotationExact;
                if(positionExact&&(!requireExactRotation||rotationExact)) {
                    attemptRecord["exact"]=true;
                    record["positionExact"]=true;
                    record["rotationDeferredToPostReset"]=!rotationExact;
                    record["exact"]=true;
                    return;
                }
                float size=Math.Max(MaxAbs(positionResidual),requireExactRotation?MaxAbs(rotationResidual):0.0f);
                attemptRecord["maximumAbsoluteResidual"]=size;
                // PhysX normalizes the actor quaternion and composes it through
                // the center-of-mass frame.  Float-lattice corrections can
                // cross the target, retain the same maximum residual, and even
                // take one larger step before the next bounded input becomes
                // exact.  Monotonic residual size is therefore not a valid
                // inverse-setter prerequisite.  Candidate/residual envelopes,
                // finite checks, actual candidate progress, the five-assignment
                // cap, and exact final readback still reject drift and cycles.
                if(!requireExactRotation&&size>=previous&&size<=MaximumMassFrameResidual) {
                    record["boundedTransientAccepted"]=true;
                    record["maximumAbsoluteResidual"]=size;
                    return;
                }
                if(attempt==MaximumJointPoseAssignments)
                    throw new InvalidOperationException("Native existing-actor pose has no exact readback within five assignments: "+row.EntityId);
                Vector3 nextPosition=new Vector3(candidatePosition.x+positionResidual.x,
                    candidatePosition.y+positionResidual.y,candidatePosition.z+positionResidual.z);
                Quaternion nextRotation=requireExactRotation
                    ?new Quaternion(candidateRotation.x+rotationResidual.x,candidateRotation.y+rotationResidual.y,
                        candidateRotation.z+rotationResidual.z,candidateRotation.w+rotationResidual.w)
                    :candidateRotation;
                if(Same(nextPosition,candidatePosition)&&Same(nextRotation,candidateRotation))
                    throw new InvalidOperationException("Native existing-actor pose preimage cannot progress at float precision: "+row.EntityId);
                candidatePosition=nextPosition;candidateRotation=nextRotation;previous=size;
            }
        }

        private void RestoreBoundedMotionSideEffect(Snapshot row,Vector3 velocity,Vector3 angular,
            bool kinematic,bool gravity,Dictionary<string,object> ownerRecord,string phase)
        {
            Vector3 observedVelocity=row.Body.velocity;
            Vector3 observedAngular=row.Body.angularVelocity;
            if(Same(observedVelocity,velocity)&&Same(observedAngular,angular)
                &&row.Body.isKinematic==kinematic&&row.Body.useGravity==gravity)return;
            Vector3 velocityResidual=Subtract(velocity,observedVelocity);
            Vector3 angularResidual=Subtract(angular,observedAngular);
            var record=new Dictionary<string,object> {
                {"restoreCall",restoreCall},{"entityId",row.EntityId},
                {"phase",phase},
                {"expectedVelocity",Point(velocity)},{"observedVelocity",DiagnosticPoint(observedVelocity)},
                {"velocityResidual",DiagnosticPoint(velocityResidual)},
                {"expectedAngularVelocity",Point(angular)},{"observedAngularVelocity",DiagnosticPoint(observedAngular)},
                {"angularVelocityResidual",DiagnosticPoint(angularResidual)},
                {"expectedKinematic",kinematic},{"observedKinematic",row.Body.isKinematic},
                {"expectedGravity",gravity},{"observedGravity",row.Body.useGravity},{"exact",false}};
            motionSideEffectRestores.Add(record);
            if(motionSideEffectRestores.Count>128)motionSideEffectRestores.RemoveAt(0);
            ownerRecord["boundedMotionSideEffectRestore"]=record;
            if(row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity
                ||!Finite(observedVelocity)||!Finite(observedAngular)
                ||!Finite(velocityResidual)||!Finite(angularResidual)
                ||MaxAbs(velocityResidual)>MaximumPoseMotionSideEffectResidual
                ||MaxAbs(angularResidual)>MaximumPoseMotionSideEffectResidual)
                throw new InvalidOperationException("Native restoration changed motion outside its bounded exact-repair envelope during "+phase+": "+row.EntityId);
            if(!Same(observedVelocity,velocity))row.Body.velocity=velocity;
            if(!Same(observedAngular,angular))row.Body.angularVelocity=angular;
            Vector3 afterVelocity=row.Body.velocity;
            Vector3 afterAngular=row.Body.angularVelocity;
            record["afterVelocity"]=DiagnosticPoint(afterVelocity);
            record["afterAngularVelocity"]=DiagnosticPoint(afterAngular);
            record["afterKinematic"]=row.Body.isKinematic;
            record["afterGravity"]=row.Body.useGravity;
            if(!Same(afterVelocity,velocity)||!Same(afterAngular,angular)
                ||row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity)
                throw new InvalidOperationException("Native restoration motion side effect has no exact setter readback during "+phase+": "+row.EntityId);
            record["exact"]=true;
        }
        private object MassFrame(Rigidbody body){return MassFrame(CaptureInvariants(body));}
        private object CurrentPose(Snapshot r){return new Dictionary<string,object>{{"bodyPosition",DiagnosticPoint(r.Body.position)},
            {"bodyRotation",DiagnosticRotation(r.Body.rotation)},{"transformPosition",DiagnosticPoint(r.Transform.position)},
            {"transformRotation",DiagnosticRotation(r.Transform.rotation)},{"localPosition",DiagnosticPoint(r.Transform.localPosition)},
            {"localRotation",DiagnosticRotation(r.Transform.localRotation)}};}
        private object TargetPose(Snapshot r){return new Dictionary<string,object>{{"bodyPosition",Point(r.BodyPosition)},
            {"bodyRotation",Rotation(r.BodyRotation)},{"transformPosition",Point(r.WorldPosition)},
            {"transformRotation",Rotation(r.WorldRotation)},{"localPosition",Point(r.LocalPosition)},{"localRotation",Rotation(r.LocalRotation)}};}
        private object MassFrame(BodyInvariants a){return new Dictionary<string,object>{{"mass",a.Mass},{"centerOfMass",Point(a.CenterOfMass)},
            {"inertiaTensor",Point(a.InertiaTensor)},{"inertiaTensorRotation",Rotation(a.InertiaTensorRotation)}};}
        private void RestoreRotation(Snapshot row,long call,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity,int priorAssignments=0)
        {
            if(Same(row.Body.rotation,row.BodyRotation))return;
            Quaternion candidate=row.BodyRotation;
            float previous=float.PositiveInfinity;
            for(int attempt=priorAssignments+1;attempt<=MaximumRotationAssignments;attempt++) {
                if(!Finite(candidate)||MaxAbs(Subtract(candidate,row.BodyRotation))>MaximumRotationResidual)
                    throw new InvalidOperationException("Native rotation setter preimage escaped its small candidate envelope: "+row.EntityId);
                var log=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},{"attempt",attempt},
                    {"target",Rotation(row.BodyRotation)},{"input",Rotation(candidate)},{"readback",null},{"exact",false}};
                rotationRestores.Add(log);
                if(rotationRestores.Count>256){rotationRestores.RemoveAt(0);discardedRotationRecords++;}
                try {
                    row.Body.rotation=candidate;
                    Quaternion observed=row.Body.rotation,residual=Subtract(row.BodyRotation,observed);
                    log["readback"]=DiagnosticRotation(observed);log["residual"]=DiagnosticRotation(residual);
                    bool finite=Finite(observed)&&Finite(residual);float size=finite?MaxAbs(residual):float.PositiveInfinity;
                    log["finite"]=finite;log["maximumAbsoluteResidual"]=finite?(object)size:null;
                    ValidateUnchanged(row,velocity,angular,kinematic,gravity);
                    if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Transform.localPosition,row.LocalPosition)
                        ||!Same(row.Transform.localRotation,row.LocalRotation)||!Same(row.Transform.position,row.WorldPosition)
                        ||!Same(row.Transform.rotation,row.WorldRotation)) {
                        log["jointPoseRepairBefore"]=CurrentPose(row);
                        if(nativeSetGlobalPose==null)
                            throw new InvalidOperationException("Native rotation setter changed another restored pose: "+row.EntityId);
                        RestoreExistingActorPose(row,log,velocity,angular,kinematic,gravity,"after-final-rotation",true);
                        log["jointPoseRepairAfter"]=CurrentPose(row);
                        observed=row.Body.rotation;residual=Subtract(row.BodyRotation,observed);
                        log["readback"]=DiagnosticRotation(observed);log["residual"]=DiagnosticRotation(residual);
                        ValidateUnchanged(row,velocity,angular,kinematic,gravity);
                        if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Transform.localPosition,row.LocalPosition)
                            ||!Same(row.Transform.localRotation,row.LocalRotation)||!Same(row.Transform.position,row.WorldPosition)
                            ||!Same(row.Transform.rotation,row.WorldRotation)||!Same(observed,row.BodyRotation))
                            throw new InvalidOperationException("Native joint pose repair did not restore every exact pose: "+row.EntityId);
                        log["exact"]=true;return;
                    }
                    if(Same(observed,row.BodyRotation)){log["exact"]=true;return;}
                    if(!finite||size>MaximumRotationResidual)
                        throw new InvalidOperationException("Native rotation readback residual is nonfinite or outside small envelope: "+row.EntityId);
                    if(size>=previous)
                        throw new InvalidOperationException("Native rotation setter preimage stagnated or worsened: "+row.EntityId);
                    if(attempt==MaximumRotationAssignments)
                        throw new InvalidOperationException("Native rotation setter has no exact readback within four assignments: "+row.EntityId);
                    Quaternion next=new Quaternion(candidate.x+residual.x,candidate.y+residual.y,candidate.z+residual.z,candidate.w+residual.w);
                    if(Same(next,candidate))throw new InvalidOperationException("Native rotation setter preimage cannot progress at float precision: "+row.EntityId);
                    candidate=next;previous=size;
                }
                catch(Exception error){log["error"]=error.Message;throw;}
            }
        }
        private Quaternion Subtract(Quaternion a,Quaternion b){return new Quaternion(a.x-b.x,a.y-b.y,a.z-b.z,a.w-b.w);}
        private Vector3 Subtract(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        private float MaxAbs(Quaternion q){return Math.Max(Math.Max(Math.Abs(q.x),Math.Abs(q.y)),Math.Max(Math.Abs(q.z),Math.Abs(q.w)));}
        private float MaxAbs(Vector3 v){return Math.Max(Math.Abs(v.x),Math.Max(Math.Abs(v.y),Math.Abs(v.z)));}

        private object Point(Vector3 v) { return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } }; }
        private object DiagnosticPoint(Vector3 v) {return Finite(v)?Point(v):(object)string.Join(",",new[]{v.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),v.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),v.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)});}
        private object Rotation(Quaternion q) { return new Dictionary<string, object> { { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w } }; }
        private object DiagnosticRotation(Quaternion q) {return Finite(q)?Rotation(q):(object)Exact(q);}
        private bool Same(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        private string Exact(Quaternion q) { return string.Join(",",new[] {q.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.w.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}); }
        private bool Same(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        private bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private bool Finite(Quaternion q) { return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w); }
    }
}
