using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Native pause freezes bodies and keeps their resumable state in TimeManager.
    // Reading that state must not confuse the temporary zero velocity with the
    // velocity of the paused simulation. No native fields are changed here.
    public static class NativePhysicsObservation
    {
        public sealed class Frozen
        {
            public Vector3 Velocity,AngularVelocity;
            public bool IsKinematic,UseGravity;
        }
        private static readonly FieldInfo frozenList=AccessTools.Field(typeof(TimeManager),"m_frozenPhysics");
        private static readonly Type frozenType=typeof(TimeManager).GetNestedType("FrozenPhysicsData",BindingFlags.NonPublic);
        private static readonly FieldInfo body=AccessTools.Field(frozenType,"m_frozenBody");
        private static readonly FieldInfo velocity=AccessTools.Field(frozenType,"m_linearVelocity");
        private static readonly FieldInfo angular=AccessTools.Field(frozenType,"m_angularVelocity");
        private static readonly FieldInfo kinematic=AccessTools.Field(frozenType,"m_isKinematic");
        private static readonly FieldInfo gravity=AccessTools.Field(frozenType,"m_useGravity");

        public static Dictionary<Rigidbody,Frozen> CaptureFrozen()
        {
            var result=new Dictionary<Rigidbody,Frozen>();
            var manager=Helpers.CurrentTimeManager;
            if(manager==null)return result;
            foreach(object entry in (IEnumerable)frozenList.GetValue(manager)) {
                var rb=(Rigidbody)body.GetValue(entry);
                if(rb==null)continue;
                if(result.ContainsKey(rb))throw new InvalidOperationException("Duplicate native frozen physics body.");
                result.Add(rb,new Frozen {Velocity=(Vector3)velocity.GetValue(entry),AngularVelocity=(Vector3)angular.GetValue(entry),
                    IsKinematic=(bool)kinematic.GetValue(entry),UseGravity=(bool)gravity.GetValue(entry)});
            }
            return result;
        }
        public static void ReadVelocity(Rigidbody rb,Dictionary<Rigidbody,Frozen> frozen,out Vector3 linear,out Vector3 spin)
        {
            Frozen saved;
            if(frozen.TryGetValue(rb,out saved)){linear=saved.Velocity;spin=saved.AngularVelocity;}
            else {linear=rb.velocity;spin=rb.angularVelocity;}
        }
        private static object Point(Vector3 value) {return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z}};}
        private static object Rotation(Quaternion value) {return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},{"w",value.w}};}
        public static object CaptureRegistry()
        {
            var frozen=CaptureFrozen();var rows=new List<object>();
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++) {
                var entry=entries._items[i];var obj=entry.m_GameObject;
                var rb=obj.GetPhysicsContainerIfExists() ?? obj.GetComponent<Rigidbody>();
                if(rb==null)continue;
                Frozen saved;bool paused=frozen.TryGetValue(rb,out saved);
                rows.Add(new Dictionary<string,object> {
                    {"entityId",(int)entry.m_Header.m_uEntityID},{"bodyInstanceId",rb.GetInstanceID()},
                    {"position",Point(rb.position)},{"rotation",Rotation(rb.rotation)},
                    {"transformPosition",Point(rb.transform.position)},{"transformRotation",Rotation(rb.transform.rotation)},
                    {"transformLocalPosition",Point(rb.transform.localPosition)},{"transformLocalRotation",Rotation(rb.transform.localRotation)},
                    {"centerOfMass",Point(rb.centerOfMass)},{"worldCenterOfMass",Point(rb.worldCenterOfMass)},
                    {"inertiaTensor",Point(rb.inertiaTensor)},{"inertiaTensorRotation",Rotation(rb.inertiaTensorRotation)},
                    {"mass",rb.mass},{"drag",rb.drag},{"angularDrag",rb.angularDrag},{"constraints",(int)rb.constraints},
                    {"interpolation",(int)rb.interpolation},{"collisionDetectionMode",(int)rb.collisionDetectionMode},
                    {"detectCollisions",rb.detectCollisions},
                    {"rawVelocity",Point(rb.velocity)},{"rawAngularVelocity",Point(rb.angularVelocity)},
                    {"rawIsKinematic",rb.isKinematic},{"rawUseGravity",rb.useGravity},{"sleeping",rb.IsSleeping()},
                    {"frozenByNativeTimeManager",paused},{"resumeVelocity",Point(paused?saved.Velocity:rb.velocity)},
                    {"resumeAngularVelocity",Point(paused?saved.AngularVelocity:rb.angularVelocity)},
                    {"resumeIsKinematic",paused?saved.IsKinematic:rb.isKinematic},{"resumeUseGravity",paused?saved.UseGravity:rb.useGravity}
                });
            }
            return new Dictionary<string,object>{{"source","native-rigidbody-and-TimeManager.FrozenPhysicsData"},{"bodies",rows.ToArray()}};
        }
    }
}
