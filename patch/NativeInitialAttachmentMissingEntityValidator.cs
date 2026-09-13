using System;
using Hpmv;
using UnityEngine;

namespace SuperchargedPatch
{
    // The ordinary fixed-entity WarpSpec row for a PhysicalAttachment's
    // separately registered Rigidbody survives even when that container has
    // been destroyed.  Admission is limited to the already-qualified pair and
    // to the exact pose-only row emitted for the historical container.
    internal static class NativeInitialAttachmentMissingEntityValidator
    {
        internal static bool Validate(EntityWarpSpec spec,int ownerId,int containerId,
            Vector3 position,UnityEngine.Quaternion rotation,Vector3 velocity,Vector3 angularVelocity)
        {
            if(spec==null||!spec.__isset.entityId)return false;
            if(spec.EntityId!=ownerId&&spec.EntityId!=containerId)return false;
            if(spec.EntityId==ownerId)
                throw new InvalidOperationException("Recreated initial attachment owner must use its qualified spawn path, not a missing fixed-entity row.");
            ValidatePoseOnly(spec,containerId);
            if(!Same(spec.Position,position)||!Same(spec.Rotation,rotation)
                ||!Same(spec.Velocity,velocity)||!Same(spec.AngularVelocity,angularVelocity))
                throw new InvalidOperationException("Recreated initial attachment container pose row differs from its checkpoint.");
            return true;
        }
        internal static bool ValidatePoseOnly(EntityWarpSpec spec,int containerId)
        {
            if(spec==null||!spec.__isset.entityId||spec.EntityId!=containerId)return false;
            var expected=new EntityWarpSpec.Isset {entityId=true,position=true,rotation=true,
                velocity=true,angularVelocity=true};
            if(!spec.__isset.Equals(expected)||spec.Position==null||spec.Rotation==null
                ||spec.Velocity==null||spec.AngularVelocity==null)
                throw new InvalidOperationException("Initial attachment container residue requires one exact pose-only fixed-entity row.");
            return true;
        }
        private static bool Same(Point value,Vector3 target)
        {return value.X==target.x&&value.Y==target.y&&value.Z==target.z;}
        private static bool Same(Hpmv.Quaternion value,UnityEngine.Quaternion target)
        {return value.X==target.x&&value.Y==target.y&&value.Z==target.z&&value.W==target.w;}
    }
}
