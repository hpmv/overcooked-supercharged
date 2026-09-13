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
        private const int MaximumRotationAssignments=4;
        private const int MaximumPositionAssignments=4;
        private const float MaximumRotationResidual=0.000001f;
        private const float MaximumMassFrameResidual=0.00001f;
        private const float MaximumPoseMotionSideEffectResidual=0.000001f;
        private readonly List<object> rotationRestores=new List<object>();
        private readonly List<object> massRestores=new List<object>();
        private readonly List<object> nativePoseRestores=new List<object>();
        private readonly List<object> nativeMassFrameRestores=new List<object>();
        private readonly List<object> motionSideEffectRestores=new List<object>();
        private long restoreCall,discardedRotationRecords;
        private IDisposable registration;
        private IntPtr nativeLibrary;
        private uint unityPlayerBase;
        private string nativePath,nativeSha256;
        private NativeSetGlobalPose nativeSetGlobalPose;
        private NativeSetMassFrame nativeSetMassFrame;
        private readonly FieldInfo cachedPtr=typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic);
        private bool disposed;
        public string Name {get{return "body-native-auto-reset-v32-recreated-native-shape-state-rebind";}}
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
                {"nativeMassFrameActive",nativeSetMassFrame!=null},{"nativePoseRestores",nativePoseRestores.ToArray()},
                {"nativeMassFrameRestores",nativeMassFrameRestores.ToArray()},
                {"motionSideEffectRestores",motionSideEffectRestores.ToArray()},
                {"nativeShapePoseCaptureActive",NativeShapeCaptureActive},
                {"nativeShapePoseCaptures",nativeShapePoseCaptures},{"nativeShapePoseCaptureFailure",nativeShapePoseCaptureFailure},
                {"nativeShapePoseRestores",nativeShapePoseRestores.ToArray()},
                {"nativeShapeTopologyMismatches",nativeShapeTopologyMismatches.ToArray()},
                {"nativeShapeGeometryRebinds",nativeShapeGeometryRebinds.ToArray()},
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
            unityPlayerBase=unchecked((uint)unity.BaseAddress.ToInt32());
            nativeLibrary=LoadLibrary(path);
            if(nativeLibrary==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try {
                var version=Export<NativeApiVersion>("oc2_rigidbody_rebuild_api_version");
                nativeSetGlobalPose=Export<NativeSetGlobalPose>("oc2_rigidbody_set_global_pose");
                nativeSetMassFrame=Export<NativeSetMassFrame>("oc2_rigidbody_set_mass_frame");
                nativeCaptureShapePoses=Export<NativeCaptureShapePoses>("oc2_rigidbody_capture_shape_poses");
                nativeRestoreShapePoses=Export<NativeRestoreShapePoses>("oc2_rigidbody_restore_shape_poses");
                if(version()!=6)throw new InvalidOperationException("Native body pose/mass/shape-state helper API version mismatch.");
                nativePath=path;nativeSha256=actual;
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
            nativeCaptureShapePoses=null;
            nativeRestoreShapePoses=null;
            if(nativeLibrary!=IntPtr.Zero){FreeLibrary(nativeLibrary);nativeLibrary=IntPtr.Zero;}
            unityPlayerBase=0;
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
                var velocity=row.Body.velocity;var angular=row.Body.angularVelocity;
                bool kinematic=row.Body.isKinematic,gravity=row.Body.useGravity;
                if(!Finite(velocity)||!Finite(angular))throw new InvalidOperationException("Nonfinite current native body velocity: "+row.EntityId);
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
                RestoreNativeShapePoses(row,nativeShapeTargets[row],call,velocity,angular,kinematic,gravity);
                int provisionalAssignments=0;
                bool massFrameDiffers=!SameMassFrame(row.Invariants,CaptureInvariants(row.Body));
                // A restored compound hierarchy can leave PhysX with a pending
                // automatic mass-property refresh even when the public values
                // already equal the checkpoint. Consume that refresh now, then
                // restore the exact saved frame while preserving automatic mode.
                if(massFrameDiffers||(row.Colliders.Length>0&&row.HasAnalyticColliders)) {
                    provisionalAssignments=RecomputeMassFrame(row,call,velocity,angular,kinematic,gravity);
                }
                RestoreRotation(row,call,velocity,angular,kinematic,gravity,provisionalAssignments);
                // Resetting the center-of-mass frame can move the public
                // Rigidbody.position even when rotation already reads exactly,
                // so RestoreRotation may correctly have had nothing to do.
                // Close both coupled pose fields together after all mass work.
                if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Body.rotation,row.BodyRotation)) {
                    if(nativeSetGlobalPose==null)
                        throw new InvalidOperationException("Exact final joint body-pose restore is unavailable: "+row.EntityId);
                    RestoreExistingActorPose(row,null,velocity,angular,kinematic,gravity,"final-after-mass",true);
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
        private int RecomputeMassFrame(Snapshot row,long call,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            var log=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},{"before",MassFrame(row.Body)},
                {"savedColliderCount",row.Colliders.Length},{"currentColliderCount",NativeBodyColliderCheckpoint.Capture(row.Body).Length},{"isKinematicBeforeReset",row.Body.isKinematic},
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
            if(ok!=1||receipt.ApiVersion!=6||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeSetMassFrameReceipt))
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
            for(int attempt=1;attempt<=MaximumPositionAssignments;attempt++) {
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
                if(ok!=1||receipt.ApiVersion!=6||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeSetGlobalPoseReceipt))||receipt.Result!=1)
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
                if(size>=previous) {
                    if(!requireExactRotation&&size<=MaximumMassFrameResidual) {
                        record["boundedTransientAccepted"]=true;
                        record["maximumAbsoluteResidual"]=size;
                        return;
                    }
                    throw new InvalidOperationException("Native existing-actor pose preimage stagnated or worsened: "+row.EntityId);
                }
                if(attempt==MaximumPositionAssignments)
                    throw new InvalidOperationException("Native existing-actor pose has no exact readback within four assignments: "+row.EntityId);
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
