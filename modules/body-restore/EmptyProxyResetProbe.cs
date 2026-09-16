using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class BodyRestoreModule
    {
        private readonly int moduleThread=Thread.CurrentThread.ManagedThreadId;
        private object lastEmptyResetProbe;
        private object ProbeEmptyReset(Dictionary<string,object> args)
        {
            if(Thread.CurrentThread.ManagedThreadId!=moduleThread||!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Empty proxy probe requires the original paused Unity thread.");
            if(args==null||args.Count!=3||!args.ContainsKey("entityId")||!args.ContainsKey("expectedObjectId")||!args.ContainsKey("expectedBodyId"))
                throw new ArgumentException("Provide only entityId, expectedObjectId and expectedBodyId.");
            int entityId=ExactInt(args["entityId"]),objectId=ExactInt(args["expectedObjectId"]),bodyId=ExactInt(args["expectedBodyId"]);
            if(entityId!=121)throw new ArgumentException("This witnessed diagnostic is bounded to native entity121.");
            var matches=new List<EntitySerialisationEntry>();var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)if((int)entries._items[i].m_Header.m_uEntityID==entityId&&entries._items[i].m_GameObject!=null)matches.Add(entries._items[i]);
            if(matches.Count!=1)throw new InvalidOperationException("Probe requires exactly one live registered native proxy.");
            var entry=matches[0];var obj=entry.m_GameObject;var body=obj.GetComponent<Rigidbody>();
            if(obj.GetInstanceID()!=objectId||body==null||body.GetInstanceID()!=bodyId||!obj.GetComponents<Component>().Any(c=>c!=null&&c.GetType().Name=="ObjectContainer"))
                throw new InvalidOperationException("Proxy GameObject/body/native ObjectContainer identity differs.");
            RequireEmptyProxy(entry,obj,body);
            bool originalKinematic=body.isKinematic,originalGravity=body.useGravity;
            var velocity=body.velocity;var angular=body.angularVelocity;
            if(!originalKinematic||!Same(velocity,new Vector3())||!Same(angular,new Vector3()))
                throw new InvalidOperationException("Probe requires an originally kinematic, motionless empty proxy.");
            var before=CaptureInvariants(body);var position=body.position;var rotation=body.rotation;
            var localPosition=obj.transform.localPosition;var localRotation=obj.transform.localRotation;
            var stages=new List<object>();
            var receipt=new Dictionary<string,object>{{"scope","native-empty-proxy-kinematic-reset-experiment"},{"entityId",entityId},
                {"objectInstanceId",objectId},{"bodyInstanceId",bodyId},{"ok",false},{"mutationAttempted",false},
                {"originalKinematicRestored",false},{"resetCenterOfMass",false},{"resetInertiaTensor",false},
                {"simulationInvoked",false},{"numericMassAssignments",false},{"stages",stages},
                {"qualification","Diagnostic primitive only; changed mass values do not establish target equality or replay parity."}};
            lastEmptyResetProbe=receipt;
            Action<string> observe=phase=>stages.Add(new Dictionary<string,object>{{"phase",phase},{"state",ProbeState(body)}});
            observe("before");bool completed=false;
            try {
                receipt["mutationAttempted"]=true;body.isKinematic=false;observe("after-clear-kinematic");
                RequireEmptyProxy(entry,obj,body);
                body.ResetCenterOfMass();receipt["resetCenterOfMass"]=true;observe("after-reset-center");
                RequireEmptyProxy(entry,obj,body);
                body.ResetInertiaTensor();receipt["resetInertiaTensor"]=true;observe("after-reset-inertia");
                RequireEmptyProxy(entry,obj,body);completed=true;
            }
            catch(Exception error){receipt["error"]=error.ToString();}
            finally {
                try {
                    if(body==null)throw new InvalidOperationException("Original proxy disappeared before kinematic cleanup.");
                    if(body.isKinematic!=originalKinematic)body.isKinematic=originalKinematic;
                    receipt["originalKinematicRestored"]=body.isKinematic==originalKinematic;observe("after-restore-kinematic");
                    RequireEmptyProxy(entry,obj,body);
                    bool motionSame=Same(velocity,body.velocity)&&Same(angular,body.angularVelocity)&&body.useGravity==originalGravity;
                    bool poseSame=Same(position,body.position)&&Same(rotation,body.rotation)&&Same(localPosition,obj.transform.localPosition)&&Same(localRotation,obj.transform.localRotation);
                    receipt["originalMotionAndGravityPreserved"]=motionSame;receipt["originalPosesPreserved"]=poseSame;
                    receipt["massFieldsChanged"]=!SameMassFrame(before,CaptureInvariants(body));
                    receipt["ok"]=completed&&motionSame&&poseSame&&(bool)receipt["originalKinematicRestored"];
                }
                catch(Exception error){receipt["cleanupError"]=error.ToString();receipt["ok"]=false;}
            }
            return receipt;
        }
        private static int ExactInt(object value)
        {
            if(!(value is int)&&!(value is long)&&!(value is double))throw new ArgumentException("Integer identity required.");
            double number=Convert.ToDouble(value);if(double.IsNaN(number)||double.IsInfinity(number)||number<int.MinValue||number>int.MaxValue||number!=Math.Truncate(number))throw new ArgumentException("Exact Int32 identity required.");
            return (int)number;
        }
        private static void RequireEmptyProxy(EntitySerialisationEntry entry,GameObject obj,Rigidbody body)
        {
            if(obj==null||body==null||EntitySerialisationRegistry.GetEntry(obj)!=entry||entry.m_GameObject!=obj||obj.GetComponent<Rigidbody>()!=body)throw new InvalidOperationException("Registered proxy incarnation changed during probe.");
            if(obj.GetComponentsInChildren<Collider>(true).Any(c=>c.attachedRigidbody==body))throw new InvalidOperationException("Probe proxy has an attached collider.");
            NativeBodyMassMode.RequireAutomatic(body);
        }
        private object ProbeState(Rigidbody body)
        {
            try{return new Dictionary<string,object>{{"mass",MassFrame(body)},{"isKinematic",body.isKinematic},{"useGravity",body.useGravity},
                {"velocity",Point(body.velocity)},{"angularVelocity",Point(body.angularVelocity)},{"position",Point(body.position)},
                {"rotation",Rotation(body.rotation)},{"colliderCount",NativeBodyColliderCheckpoint.Capture(body).Length}};}
            catch(Exception error){return new Dictionary<string,object>{{"observationError",error.ToString()}};}
        }
    }
}
