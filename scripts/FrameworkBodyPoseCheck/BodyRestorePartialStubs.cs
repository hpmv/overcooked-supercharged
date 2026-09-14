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
        private sealed class NativeShapeCheckpoint {}
        private delegate int NativeCaptureShapePoses();
        private delegate int NativeRestoreShapePoses();

        private NativeCaptureShapePoses nativeCaptureShapePoses;
        private NativeRestoreShapePoses nativeRestoreShapePoses;
        private readonly List<object> nativeShapePoseRestores=new List<object>();
        private readonly List<object> nativeShapeTopologyMismatches=new List<object>();
        private readonly List<object> nativeShapeGeometryRebinds=new List<object>();
        private readonly List<object> pendingRecreatedShapes=new List<object>();
        private int nativeShapePoseCaptures;
        private string nativeShapePoseCaptureFailure;
        private bool nativeShapeGeometryRebindPoisoned;
        private string nativeShapeGeometryRebindFailure;
        private readonly List<object> colliderAncestorPoseRestores=new List<object>();
        private int nativePoseFixtureAttempts;

        public int NativePoseFixtureAttempts { get { return nativePoseFixtureAttempts; } }

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
                    ApiVersion = 6,
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

        private bool NativeShapeCaptureActive {get{return false;}}
        private bool ColliderAncestorPoseRestoreActive {get{return false;}}
        private void ActivateNativeShapeCapture() {}
        private void DeactivateNativeShapeCapture() {}
        private void ActivateColliderAncestorPoseRestore() {}
        private void DeactivateColliderAncestorPoseRestore() {}
        private Dictionary<Snapshot,NativeShapeCheckpoint> RequireNativeShapeTargets(Snapshot[] rows)
        {
            var result=new Dictionary<Snapshot,NativeShapeCheckpoint>();
            foreach(var row in rows)result[row]=new NativeShapeCheckpoint();
            return result;
        }
        private void RestoreNativeShapePoses(Snapshot row,NativeShapeCheckpoint target,long call,
            Vector3 velocity,Vector3 angular,bool kinematic,bool gravity) {}
    }
}
