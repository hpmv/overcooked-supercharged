using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    internal static class Helpers
    {
        public static bool IsPaused()
        {
            return TimeManager.IsPaused(TimeManager.PauseLayer.Main);
        }


        // Just an arbitrary object to tell TimeManager whose pause we're resuming.
        // We don't initialize a new object though. This is because when we hot-reload the plugin,
        // we would get a different object and then it's impossible to resume the game. So, we use
        // a stable reference. The exact reference doesn't matter, it just needs to not be part of
        // the plugin.
        private static object timeManagerPauseArbitration = typeof(TimeManager);

        public static TimeManager CurrentTimeManager { get; set; }

        public static void Pause()
        {
            CurrentTimeManager.SetPaused(TimeManager.PauseLayer.Main, true, timeManagerPauseArbitration);
        }

        public static void Resume()
        {
            CurrentTimeManager.SetPaused(TimeManager.PauseLayer.Main, false, timeManagerPauseArbitration);
        }

        public static GameObject GetSpawnableEntityByIndex(this SpawnableEntityCollection collection, int index)
        {
            FieldInfo m_spawnables = typeof(SpawnableEntityCollection).GetField("m_spawnables", BindingFlags.Instance | BindingFlags.NonPublic);
            var spawnables = (List<GameObject>)m_spawnables.GetValue(collection);
            if (index < 0 || index >= spawnables.Count)
            {
                return null;
            }
            return spawnables[index];
        }

        /// <summary>
        /// If the object is a PhysicalAttachment, and the container object is currently the parent, return that object.
        /// Otherwise return null. The container may not be the parent if the object is currently attached to a parent
        /// object (chef, AttachStation, etc.).
        /// </summary>
        public static Rigidbody GetPhysicsContainerIfExists(this GameObject gameObject)
        {
            if (gameObject.GetComponent<PhysicalAttachment>() is PhysicalAttachment attachment)
            {
                if (gameObject.transform.parent == attachment.m_container.transform)
                {
                    return attachment.m_container;
                }
            }
            return null;
        }

        public static Vector3 FromThrift(this Hpmv.Point point)
        {
            return new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        }

        public static Hpmv.Point ToThrift(this Vector3 vector)
        {
            return new Hpmv.Point { X = vector.x, Y = vector.y, Z = vector.z };
        }

        public static Quaternion FromThrift(this Hpmv.Quaternion quaternion)
        {
            return new Quaternion((float)quaternion.X, (float)quaternion.Y, (float)quaternion.Z, (float)quaternion.W);
        }

        public static Hpmv.Quaternion ToThrift(this Quaternion quaternion)
        {
            return new Hpmv.Quaternion { X = quaternion.x, Y = quaternion.y, Z = quaternion.z, W = quaternion.w };
        }
    }
}
