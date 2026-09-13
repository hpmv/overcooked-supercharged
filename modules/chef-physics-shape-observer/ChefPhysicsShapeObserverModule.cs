using System;
using System.Collections.Generic;
using System.Linq;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only comparison of Unity's exposed Rigidbody/Transform pose and the
    // physics-backed world bounds returned for each local-chef capsule.
    public sealed class ChefPhysicsShapeObserverModule : IAuthoringModule
    {
        private bool disposed;
        public string Name { get { return "local-chef-physics-shape-observer-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ChefPhysicsShapeObserverModule");
            if(args!=null&&args.Count!=0)throw new ArgumentException("Shape observations take no arguments.");
            if(operation!="sample"&&operation!="status")throw new ArgumentException("Use sample or status.");
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"mutationAttempted",false},
                {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                {"samples",operation=="sample"?Sample():new object[0]},
                {"scope","Read-only local-chef Rigidbody, Transform, center-of-mass and CapsuleCollider world-bounds observation; no hooks or Unity setters."}};
        }

        private static object[] Sample()
        {
            var rows=new List<object>();
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];var obj=entry==null?null:entry.m_GameObject;
                var body=obj==null?null:obj.GetComponent<Rigidbody>();
                if(body==null||body.GetComponent<ServerChefSynchroniser>()==null
                    ||body.GetComponent<ClientOnTheServerChefSynchroniser>()==null)continue;
                var capsules=body.GetComponents<CapsuleCollider>();
                if(capsules.Length!=1)throw new InvalidOperationException("Expected one local-chef capsule.");
                var capsule=capsules[0];var bounds=capsule.bounds;
                rows.Add(new Dictionary<string,object>{{"entityId",(int)entry.m_Header.m_uEntityID},
                    {"bodyInstanceId",body.GetInstanceID()},{"capsuleInstanceId",capsule.GetInstanceID()},
                    {"bodyPosition",Point(body.position)},{"transformPosition",Point(body.transform.position)},
                    {"worldCenterOfMass",Point(body.worldCenterOfMass)},
                    {"boundsCenter",Point(bounds.center)},{"boundsExtents",Point(bounds.extents)},
                    {"boundsMin",Point(bounds.min)},{"boundsMax",Point(bounds.max)},
                    {"isKinematic",body.isKinematic},{"sleeping",body.IsSleeping()}});
            }
            return rows.OrderBy(x=>(int)((Dictionary<string,object>)x)["entityId"]).ToArray();
        }

        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}
        public void Dispose(){disposed=true;}
    }
}
