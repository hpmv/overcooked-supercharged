using System;
using System.Collections.Generic;
using UnityEngine;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;

namespace SuperchargedPatch.Authoring.Modules
{
    // This synthetic pose test exercises BodyRestoreModule without loading the
    // native shape/ancestor helpers. Keep the partial surface explicit so the
    // production module is still compiled in full by Build-FrameworkModule.
    public sealed partial class BodyRestoreModule
    {
        private sealed class NativeShapeCheckpoint
        {
            internal Rigidbody Body;
            internal UIntPtr RigidbodyPointer;
            internal NativeRigidPose ActorPose,Body2Actor,Body2World;
            internal uint WakeCounterBits,BufferedIsSleeping,BodySimActive;
            internal NativeKinematicTargetReceipt KinematicTarget;
            internal NativeBody2WorldCaptureReceipt Lifecycle;
        }
        private delegate int NativeCaptureShapePoses();
        private delegate int NativeRestoreShapePoses();

        private NativeCaptureShapePoses nativeCaptureShapePoses;
        private NativeRestoreShapePoses nativeRestoreShapePoses;
        private readonly List<object> nativeShapePoseRestores=new List<object>();
        private readonly List<object> nativeSleepingKinematicPoseRestores=new List<object>();
        private readonly List<object> nativeBody2WorldCaptures=new List<object>();
        private readonly List<object> nativeShapeTopologyMismatches=new List<object>();
        private readonly List<object> nativeKinematicWakeMismatches=new List<object>();
        private object NativeKinematicStageDiagnostics {get{return new Dictionary<string,object>();}}
        private object NativePostMaintenanceDiagnostics {get{return new Dictionary<string,object>();}}
        private object NativeKinematicFreezeDiagnostics {get{return new Dictionary<string,object>();}}
        private readonly List<object> nativeShapeGeometryRebinds=new List<object>();
        private readonly List<object> nativeDestroyedBodyRebinds=new List<object>();
        private readonly List<object> nativeDestroyedBodyModeRestores=new List<object>();
        private readonly List<object> pendingRecreatedShapes=new List<object>();
        private readonly HashSet<int> deferredKinematicSleepEntities=new HashSet<int>();
        private readonly HashSet<int> deferredKinematicTargetEntities=new HashSet<int>();
        private readonly Dictionary<int,NativeShapeCheckpoint> deferredKinematicSleepPreimages=
            new Dictionary<int,NativeShapeCheckpoint>();
        private int nativeShapePoseCaptures;
        private string nativeShapePoseCaptureFailure;
        private string nativePostMaintenanceFailure;
        private bool nativeShapeGeometryRebindPoisoned;
        private string nativeShapeGeometryRebindFailure;
        private readonly List<object> colliderAncestorPoseRestores=new List<object>();
        private int nativePoseFixtureAttempts;
        private int nativeBody2WorldFixtureCalls;
        private int nativeWakeStateFixtureCalls;

        public int NativePoseFixtureAttempts { get { return nativePoseFixtureAttempts; } }
        public int NativeBody2WorldFixtureCalls { get { return nativeBody2WorldFixtureCalls; } }
        public int NativeBody2WorldCaptureReceiptSize { get { return System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeBody2WorldCaptureReceipt)); } }
        public int NativeBody2WorldRestoreReceiptSize { get { return System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeBody2WorldRestoreReceipt)); } }
        public int NativeWakeStateFixtureCalls { get { return nativeWakeStateFixtureCalls; } }
        public int NativeWakeStateRestoreReceiptSize { get { return System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeWakeStateRestoreReceipt)); } }
        public int NativeKinematicTargetReceiptSize { get { return System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeKinematicTargetReceipt)); } }

        public void ConfigureNativePoseFixture(Rigidbody body,
            Func<int, Vector3, Quaternion, KeyValuePair<Vector3, Quaternion>> roundtrip)
        {
            unityPlayerBase = 1;
            nativeSetGlobalPose = delegate(UIntPtr unity, UIntPtr rigidbody,
                ref NativeRigidPose pose, out NativeSetGlobalPoseReceipt receipt)
            {
                var inputPosition = new Vector3(pose.Px, pose.Py, pose.Pz);
                var inputRotation = new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw);
                var output = roundtrip(++nativePoseFixtureAttempts, inputPosition, inputRotation);
                body.position = output.Key;
                body.rotation = output.Value;
                receipt = new NativeSetGlobalPoseReceipt {
                    ApiVersion = NativeHelperApiVersion,
                    StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeSetGlobalPoseReceipt)),
                    Result = 1,
                    LastError = 0,
                    UnityBase = unity,
                    Rigidbody = rigidbody,
                    Actor = new UIntPtr(1),
                    Pose = new NativeRigidPose {
                        Px = output.Key.x, Py = output.Key.y, Pz = output.Key.z,
                        Qx = output.Value.x, Qy = output.Value.y,
                        Qz = output.Value.z, Qw = output.Value.w
                    }
                };
                return 1;
            };
        }

        public void RestoreExistingActorPoseFixture(Snapshot row)
        {
            RestoreExistingActorPose(row, null, row.Body.velocity, row.Body.angularVelocity,
                row.Body.isKinematic, row.Body.useGravity, "fixture", true);
        }

        public void ConfigureNativeBody2WorldFixture(Rigidbody body, string mode)
        {
            unityPlayerBase=1;
            nativeRestoreBody2World=delegate(UIntPtr unity,UIntPtr rigidbody,
                ref NativeRigidPose actorPose,ref NativeRigidPose body2Actor,
                ref NativeRigidPose body2World,out NativeBody2WorldRestoreReceipt receipt)
            {
                nativeBody2WorldFixtureCalls++;
                var before=Pose(body.position,body.rotation);
                body.position=new Vector3(actorPose.Px,actorPose.Py,actorPose.Pz);
                body.rotation=new Quaternion(actorPose.Qx,actorPose.Qy,actorPose.Qz,actorPose.Qw);
                receipt=new NativeBody2WorldRestoreReceipt {
                    ApiVersion=NativeHelperApiVersion,
                    StructSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeBody2WorldRestoreReceipt)),
                    Result=1,UnityBase=unity,Rigidbody=rigidbody,Actor=new UIntPtr(2),
                    SceneBefore=new UIntPtr(3),SceneAfter=new UIntPtr(3),ApiScene=new UIntPtr(4),
                    DynamicTimestampBefore=100,DynamicTimestampAfter=101,
                    ControlStateBefore=2,ControlStateAfter=2,
                    SimulationRunningBefore=0,SimulationRunningAfter=0,
                    PhysicsBufferingBefore=0,PhysicsBufferingAfter=0,Changed=1,
                    ActorPoseBefore=before,ActorPoseTarget=actorPose,ActorPoseAfter=actorPose,
                    Body2ActorBefore=body2Actor,Body2ActorAfter=body2Actor,
                    BufferedBody2WorldBefore=before,CoreBody2WorldBefore=before,
                    Body2WorldTarget=body2World,BufferedBody2WorldAfter=body2World,
                    CoreBody2WorldAfter=body2World,BodySimBefore=new UIntPtr(5),BodySimAfter=new UIntPtr(5),
                    WakeCounterBufferedBitsBefore=0x3F000000,WakeCounterBufferedBitsAfter=0x3F000000,
                    WakeCounterCoreBitsBefore=0x3F000000,WakeCounterCoreBitsAfter=0x3F000000,
                    BufferedIsSleepingBefore=0,BufferedIsSleepingAfter=0,
                    BodySimActiveBefore=1,BodySimActiveAfter=1
                };
                if(mode=="control")receipt.ControlStateAfter=1;
                if(mode=="mass")receipt.Body2ActorBefore.Px+=1;
                if(mode=="simulation")receipt.SimulationRunningBefore=1;
                if(mode=="readback")receipt.CoreBody2WorldAfter.Px+=1;
                if(mode=="sleep")body.sleeping=!body.sleeping;
                if(mode=="motion")body.velocity=new Vector3(9,0,0);
                return 1;
            };
        }

        public void RestoreCheckpointBody2WorldFixture(Snapshot row)
        {
            var actorPose=Pose(row.BodyPosition,row.BodyRotation);
            var target=new NativeShapeCheckpoint {Body=row.Body,RigidbodyPointer=new UIntPtr(1),
                ActorPose=actorPose,
                Body2Actor=Pose(new Vector3(0,0,0),new Quaternion(0,0,0,1)),
                Body2World=actorPose};
            RestoreCheckpointBody2World(row,target,row.Body.velocity,row.Body.angularVelocity,
                row.Body.isKinematic,row.Body.useGravity,"fixture");
        }

        public void ConfigureNativeWakeStateFixture(Rigidbody body,string mode,uint targetWakeCounterBits)
        {
            unityPlayerBase=1;
            nativeRestoreWakeState=delegate(UIntPtr unity,UIntPtr rigidbody,uint targetBits,
                uint targetSleeping,out NativeWakeStateRestoreReceipt receipt)
            {
                nativeWakeStateFixtureCalls++;
                uint beforeSleeping=body.IsSleeping()?1u:0u;
                uint beforeWake=beforeSleeping!=0?0u:targetBits;
                uint callMask=beforeSleeping!=0&&((targetBits&0x7FFFFFFFu)==0)?3u:
                    (beforeSleeping!=targetSleeping||beforeWake!=targetBits?1u:0u);
                body.sleeping=targetSleeping!=0;
                receipt=new NativeWakeStateRestoreReceipt {
                    ApiVersion=NativeHelperApiVersion,
                    StructSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeWakeStateRestoreReceipt)),
                    Result=1,UnityBase=unity,Rigidbody=rigidbody,Actor=new UIntPtr(2),BodySim=new UIntPtr(3),
                    TargetWakeCounterBits=targetBits,TargetSleeping=targetSleeping,
                    WakeCounterBufferedBitsBefore=beforeWake,WakeCounterCoreBitsBefore=beforeWake,
                    BufferedIsSleepingBefore=beforeSleeping,BodySimActiveBefore=beforeSleeping==0?1u:0u,
                    WakeCounterBufferedBitsAfter=targetBits,WakeCounterCoreBitsAfter=targetBits,
                    BufferedIsSleepingAfter=targetSleeping,BodySimActiveAfter=targetSleeping==0?1u:0u,
                    CallMask=callMask
                };
                if(mode=="counter")receipt.WakeCounterCoreBitsAfter++;
                if(mode=="sleep")receipt.BufferedIsSleepingAfter=1;
                if(mode=="body-sim")receipt.BodySim=UIntPtr.Zero;
                if(mode=="mask")receipt.CallMask=7;
                if(mode=="motion")body.velocity=new Vector3(9,0,0);
                return 1;
            };
        }

        public void RestoreCheckpointWakeStateFixture(Snapshot row,uint targetWakeCounterBits)
        {
            var target=new NativeShapeCheckpoint {Body=row.Body,RigidbodyPointer=new UIntPtr(1),
                WakeCounterBits=targetWakeCounterBits,BufferedIsSleeping=0,BodySimActive=1};
            RestoreCheckpointWakeState(row,target,row.Body.velocity,row.Body.angularVelocity,
                row.Body.isKinematic,row.Body.useGravity);
        }

        private static NativeRigidPose Pose(Vector3 position,Quaternion rotation)
        {
            return new NativeRigidPose {Px=position.x,Py=position.y,Pz=position.z,
                Qx=rotation.x,Qy=rotation.y,Qz=rotation.z,Qw=rotation.w};
        }

        private bool NativeShapeCaptureActive {get{return false;}}
        private bool ColliderAncestorPoseRestoreActive {get{return false;}}
        private void ActivateNativeShapeCapture() {}
        private void DeactivateNativeShapeCapture() {}
        private void ActivateColliderAncestorPoseRestore() {}
        private void DeactivateColliderAncestorPoseRestore() {}
        private Dictionary<Snapshot,NativeShapeCheckpoint> RequireNativeShapeTargets(Snapshot[] rows)
        {
            var result=new Dictionary<Snapshot,NativeShapeCheckpoint>();
            foreach(var row in rows)result[row]=new NativeShapeCheckpoint {Body=row.Body};
            return result;
        }
        private void RestoreNativeShapePoses(Snapshot row,NativeShapeCheckpoint target,long call,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity) {}
        private NativeShapeCheckpoint PrepareSleepingKinematicNativePoseRestore(
            Snapshot row,NativeShapeCheckpoint target,out bool deferSleep)
        {deferSleep=false;return null;}
        private bool TryRestoreSleepingKinematicPoseNatively(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint preTransform,bool deferSleep,
            Vector3 velocity,Vector3 angular,bool kinematic,
            bool gravity,long call) {return false;}
        private void RequireDeferredKinematicMaintenance(Snapshot row,NativeShapeCheckpoint target,
            NativeShapeCheckpoint preTransform,long call) {}
        private void RestoreCheckpointKinematicTarget(Snapshot row,NativeShapeCheckpoint target,
            long call) {}
        private void RequireDeferredKinematicMaintenanceAfterMass(
            Snapshot row,NativeShapeCheckpoint target,long call) {}

        private bool Finite(NativeRigidPose pose)
        {
            return Finite(pose.Px)&&Finite(pose.Py)&&Finite(pose.Pz)&&Finite(pose.Qx)&&
                Finite(pose.Qy)&&Finite(pose.Qz)&&Finite(pose.Qw);
        }

        private static bool SameBits(float a,float b)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(a),0)==
                BitConverter.ToInt32(BitConverter.GetBytes(b),0);
        }

        private static bool SameBits(NativeRigidPose a,NativeRigidPose b)
        {
            return SameBits(a.Px,b.Px)&&SameBits(a.Py,b.Py)&&SameBits(a.Pz,b.Pz)&&
                SameBits(a.Qx,b.Qx)&&SameBits(a.Qy,b.Qy)&&SameBits(a.Qz,b.Qz)&&SameBits(a.Qw,b.Qw);
        }

        private static string NativeHex(UIntPtr value)
        {
            return "0x"+value.ToUInt64().ToString("X8");
        }

        private static object NativePoseDiagnostic(NativeRigidPose value)
        {
            return new Dictionary<string,object>{{"position",new[]{value.Px,value.Py,value.Pz}},
                {"rotation",new[]{value.Qx,value.Qy,value.Qz,value.Qw}}};
        }
    }
}
