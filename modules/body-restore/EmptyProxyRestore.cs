using System;
using System.Collections.Generic;
using System.Linq;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using Snapshot=SuperchargedPatch.NativeBodyPoseCheckpoint.Snapshot;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class BodyRestoreModule
    {
        private void RestoreEmptyProxyMass(Snapshot row,Dictionary<string,object> log,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            // Native X r2 observed the empty proxy's default mass frame return
            // synchronously at isKinematic=false, before either Reset method.
            // Keep this exception limited to the witnessed native container state.
            if(!kinematic||!row.RawIsKinematic||!Same(velocity,new Vector3())||!Same(angular,new Vector3())
                ||!Same(row.RawVelocity,new Vector3())||!Same(row.RawAngularVelocity,new Vector3())
                ||row.Body.mass!=row.Invariants.Mass||row.Colliders.Length!=0
                ||!row.Object.GetComponents<Component>().Any(c=>c!=null&&c.GetType().Name=="ObjectContainer"))
                throw new InvalidOperationException("Empty body mass rebuild requires a saved/current kinematic, motionless native ObjectContainer with unchanged mass.");
            NativeBodyPoseCheckpoint.Validate(new[]{row});
            var entry=EntitySerialisationRegistry.GetEntry(row.Object);
            RequireEmptyProxy(entry,row.Object,row.Body);
            log["emptyProxyRebuild"]=true;log["beforeClearKinematic"]=ProbeState(row.Body);
            try {
                row.Body.isKinematic=false;log["afterClearKinematic"]=ProbeState(row.Body);
                RequireEmptyProxy(entry,row.Object,row.Body);RequireMotionUnchanged(row,velocity,angular,false,gravity);
                row.Body.ResetCenterOfMass();log["resetCenterOfMass"]=true;log["afterResetCenterOfMass"]=MassFrame(row.Body);
                RequireEmptyProxy(entry,row.Object,row.Body);RequireMotionUnchanged(row,velocity,angular,false,gravity);
                row.Body.ResetInertiaTensor();log["resetInertiaTensor"]=true;log["afterResetInertiaTensor"]=MassFrame(row.Body);
                RequireEmptyProxy(entry,row.Object,row.Body);RequireMotionUnchanged(row,velocity,angular,false,gravity);
            }
            finally {
                if(row.Body!=null&&row.Body.isKinematic!=kinematic)row.Body.isKinematic=kinematic;
                log["afterRestoreKinematic"]=ProbeState(row.Body);
                log["originalKinematicRestored"]=row.Body!=null&&row.Body.isKinematic==kinematic;
            }
            RequireEmptyProxy(entry,row.Object,row.Body);
            RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
            // A dynamic PhysicalAttachment container can be captured after its
            // last collider was removed but before Unity's later automatic mass
            // refresh.  The ordinary empty reset correctly produces the default
            // empty frame, which is not that historical checkpoint.  Reuse the
            // same API-6 exact setter as the proved nonempty transient path only
            // after restoring the original kinematic mode; it verifies the
            // automatic center/inertia flags before and after the PhysX write.
            if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                RestoreExactMassFrame(row,log,velocity,angular,kinematic,gravity);
            // Numeric mass properties, original motion and every permanent core
            // postcondition still have to match; there is no managed numeric
            // fallback and no acceptance tolerance.
            ValidateUnchanged(row,velocity,angular,kinematic,gravity);
            NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
        }
    }
}
