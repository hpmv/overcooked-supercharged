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
