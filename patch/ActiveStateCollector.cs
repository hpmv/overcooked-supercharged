using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    public class Cached<T>
    {
        private T item = default;

        public bool Replace(T newValue)
        {
            if (!EqualityComparer<T>.Default.Equals(newValue, item))
            {
                item = newValue;
                return true;
            }
            return false;
        }
    }

    public class CachedEntityState
    {
        public Cached<Vector3?> Location { get; } = new Cached<Vector3?>();
        public Cached<UnityEngine.Quaternion?> Rotation { get; } = new Cached<UnityEngine.Quaternion?>();
        public Cached<Vector3?> Velocity { get; } = new Cached<Vector3?>();
        public Cached<Vector3?> AngularVelocity { get; } = new Cached<Vector3?>();
        public Cached<EntityPathReference> EntityPathReference { get; } = new Cached<EntityPathReference>();
    }

    public static class ActiveStateCollector
    {
        private static int FrameNumber { get; set; }
        private static readonly Dictionary<int, CachedEntityState> cache = new Dictionary<int, CachedEntityState>();

        private static ItemData EnsureItemData(this OutputData data, int entityID)
        {
            if (!data.Items.ContainsKey(entityID)) {
                data.Items[entityID] = new ItemData();
            }
            return data.Items[entityID];
        }

        public static void ClearCacheAfterWarp(int frame)
        {
            FrameNumber = frame;
            cache.Clear();
        }

        public static void NotifyFrame(int frame)
        {
            if (frame <= FrameNumber)
            {
                // Console.WriteLine($"Restarting at frame {frame}; was at frame {FrameNumber}");
                ClearCacheAfterWarp(frame);
            }
            else
            {
                FrameNumber = frame;
            }
        }

        public static void CollectDataForFrame(OutputData currentFrameData)
        {
            if (currentFrameData.Items == null)
            {
                currentFrameData.Items = new Dictionary<int, ItemData>();
            }
            currentFrameData.Chefs = new Dictionary<int, ChefSpecificData>();
            FastList<EntitySerialisationEntry> mEntitiesList = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < mEntitiesList.Count; i++)
            {
                var t = mEntitiesList._items[i];

                Vector3 position = t.m_GameObject.transform.position;
                UnityEngine.Quaternion rotation = t.m_GameObject.transform.rotation;
                Vector3 velocity = Vector3.zero;
                Vector3 angularVelocity = Vector3.zero;
                if (t.m_GameObject.GetPhysicsContainerIfExists() is Rigidbody container)
                {
                    position = container.transform.position;
                    rotation = container.transform.rotation;
                    velocity = container.velocity;
                    angularVelocity = container.angularVelocity;
                }
                EntityPathReference entityPathReference = null;
                if (t.m_GameObject.GetComponent<EntityPathReferenceMarker>() is EntityPathReferenceMarker marker)
                {
                    entityPathReference = marker.EntityPath;
                }
                int mUEntityID = (int)t.m_Header.m_uEntityID;
                if (!cache.ContainsKey(mUEntityID))
                {
                    cache[mUEntityID] = new CachedEntityState();
                }
                var cacheEntry = cache[mUEntityID];
                if (cacheEntry.Location.Replace(position))
                {
                    currentFrameData.EnsureItemData(mUEntityID).Pos = position.ToThrift();
                }
                if (cacheEntry.Rotation.Replace(rotation))
                {
                    currentFrameData.EnsureItemData(mUEntityID).Rotation = rotation.ToThrift();
                }
                if (cacheEntry.Velocity.Replace(velocity))
                {
                    currentFrameData.EnsureItemData(mUEntityID).Velocity = velocity.ToThrift();
                }
                if (cacheEntry.AngularVelocity.Replace(angularVelocity))
                {
                    currentFrameData.EnsureItemData(mUEntityID).AngularVelocity = angularVelocity.ToThrift();
                }
                if (cacheEntry.EntityPathReference.Replace(entityPathReference))
                {
                    currentFrameData.EnsureItemData(mUEntityID).EntityPathReference = entityPathReference.ToThrift();
                }

                if (t.m_GameObject.GetComponent<ClientPlayerControlsImpl_Default>() is ClientPlayerControlsImpl_Default cpci)
                {
                    currentFrameData.Chefs[mUEntityID] = CollectDataForChef(cpci);
                }
            }
            currentFrameData.FrameNumber = FrameNumber;
            currentFrameData.InvalidStateReason = StateInvalidityManager.InvalidReason;
        }

        private static ChefSpecificData CollectDataForChef(ClientPlayerControlsImpl_Default cpci)
        {
            var data = new ChefSpecificData();
            var m_controls = cpci.m_controls();
            // Recalculate the nearby objects. This is because the current calculation was for the previous frame, after which the chefs already moved.
            // (The order goes - calculate nearby objects, update carry/interact, and *then* update movement). So, whatever we calculate here should
            // be whatever is used for next frame, thereby giving us the correct data to make decisions for the next frame's input.
            var nearbyObjects = m_controls?.FindNearbyObjects();
            data.HighlightedForPickup = nearbyObjects?.m_TheOriginalHandlePickup != null ? (int)EntitySerialisationRegistry.GetId(nearbyObjects.m_TheOriginalHandlePickup) : -1;
            data.HighlightedForUse = nearbyObjects?.m_interactable != null ? (int)EntitySerialisationRegistry.GetId(nearbyObjects.m_interactable.gameObject) : -1;
            data.HighlightedForPlacement = nearbyObjects?.m_iHandlePlacement is Component ? (int)EntitySerialisationRegistry.GetId(((Component)nearbyObjects.m_iHandlePlacement).gameObject) : -1;

            data.DashTimer = cpci.m_dashTimer();
            var interactingEntity = cpci.m_predictedInteracted();
            data.InteractingEntity = interactingEntity != null ? (int)EntitySerialisationRegistry.GetId(interactingEntity.gameObject) : -1;
            data.LastVelocity = cpci.m_lastVelocity().ToThrift();
            data.AimingThrow = cpci.m_aimingThrow();
            data.MovementInputSuppressed = cpci.m_movementInputSuppressed();
            data.LastMoveInputDirection = cpci.m_lastMoveInputDirection().ToThrift();
            data.ImpactStartTime = cpci.m_impactStartTime();
            data.ImpactTimer = cpci.m_impactTimer();
            data.ImpactVelocity = cpci.m_impactVelocity().ToThrift();
            data.LeftOverTime = cpci.m_LeftOverTime();
            return data;
        }
    }
}
