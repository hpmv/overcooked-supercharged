﻿using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    public class WarpHandler
    {
        public static void HandleWarpRequestIfAny()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Warp == null)
            {
                return;
            }
            if (!Helpers.IsPaused())
            {
                Debug.Log("[WARP] Warning: Not warping because the game is not paused!");
                return;
            }

            Debug.Log("[WARP] Begin warping!");

            // Resume the time manager before warping, because at the end we need to pause again to save the velocities.
            Debug.Log("[WARP] Before warping, resuming the TimeManager");
            Helpers.Resume();

            var warp = input.Warp;
            foreach (var entityId in warp.EntitiesToDelete)
            {
                var entity = EntitySerialisationRegistry.GetEntry((uint)entityId);
                if (entity == null)
                {
                    Debug.Log($"[WARP] Error destroying entity ID {entityId}, entity does not exist!");
                    continue;
                }
                NetworkUtils.DestroyObject(entity.m_GameObject);
                Debug.Log($"[WARP] Destroyed entity {entityId}");
            }

            Dictionary<EntityPathReference, EntitySerialisationEntry> entityPathReferenceToEntry = new Dictionary<EntityPathReference, EntitySerialisationEntry>();

            for (int i = 0; i < warp.Entities.Count; i++)
            {
                var entityThrift = warp.Entities[i];
                EntitySerialisationEntry entity;
                if (entityThrift.__isset.entityId)
                {
                    entity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.EntityId);
                    if (entity == null)
                    {
                        Debug.Log($"[WARP] Error warping existing entity {entityThrift.EntityId}: no such entity with this ID!");
                        continue;
                    }
                } else if (entityThrift.SpawningPath.Count > 0)
                {
                    var spawningPathStr = string.Join(", ", entityThrift.SpawningPath.Select(p => "" + p).ToArray());
                    if (entityThrift.SpawningPath.Count == 1)
                    {
                        Debug.Log($"[WARP] Error spawning new entity for warping: Spawning path {spawningPathStr} is empty!");
                        continue;
                    }
                    var spawnerEntity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.SpawningPath[0]);
                    if (spawnerEntity == null)
                    {
                        Debug.Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: Spawner entity ID does not exist!");
                        continue;
                    }
                    var success = true;
                    for (var j = 1; j < entityThrift.SpawningPath.Count; j++)
                    {
                        var spawnableIndex = entityThrift.SpawningPath[j];
                        var spawnableEntities = spawnerEntity.m_GameObject.GetComponent<SpawnableEntityCollection>();
                        if (spawnableEntities == null)
                        {
                            Debug.Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: The {j}th spawner does not have a SpawnableEntityCollection!");
                            success = false;
                            break;
                        }
                        var prefab = spawnableEntities.GetSpawnableEntityByIndex(spawnableIndex);
                        if (prefab == null)
                        {
                            Debug.Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: The {j}th spawn's index is out of bounds!");
                            success = false;
                            break;
                        }
                        var spawned = NetworkUtils.ServerSpawnPrefab(spawnerEntity.m_GameObject, prefab);
                        var spawnedEntry = EntitySerialisationRegistry.GetEntry(spawned);
                        Debug.Log($"[WARP] Spawned entity {spawnedEntry.m_Header.m_uEntityID} along path {spawningPathStr}");
                        if (spawnedEntry == null)
                        {
                            Debug.Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: Failed to get the EntitySerialisationRegistry after the {j}th spawn!");
                            success = false;
                            break;
                        }
                        if (j > 1)
                        {
                            Debug.Log($"[WARP] Destroying intermediate entity {spawnerEntity.m_Header.m_uEntityID}");
                            NetworkUtils.DestroyObject(spawnerEntity.m_GameObject);
                        }
                        spawnerEntity = spawnedEntry;
                    }
                    if (!success)
                    {
                        continue;
                    }
                    entity = spawnerEntity;
                } else 
                {
                    Debug.Log($"Error warping entity (warp spec index {i}): neither entityId nor spawningPath was specified!");
                    continue;
                }

                if (entityThrift.__isset.entityPathReference)
                {
                    var entityPathReference = entityThrift.EntityPathReference.FromThrift();
                    entityPathReferenceToEntry[entityPathReference] = entity;
                    var marker = entity.m_GameObject.GetComponent<EntityPathReferenceMarker>() ??
                        entity.m_GameObject.AddComponent<EntityPathReferenceMarker>();
                    marker.EntityPath = entityPathReference;
                    Debug.Log($"[WARP] Set entity path reference of {entity.m_Header.m_uEntityID} to {entityPathReference}");
                }

                if (entityThrift.__isset.attachmentParent)
                {
                    var attachmentParent = entityThrift.AttachmentParent;
                    EntitySerialisationEntry parentEntity;
                    if (attachmentParent.__isset.parentEntityPathReference)
                    {
                        parentEntity = entityPathReferenceToEntry[attachmentParent.ParentEntityPathReference.FromThrift()];
                        if (parentEntity == null)
                        {
                            Debug.Log($"Error setting attachment parent: parent entity path reference {attachmentParent.ParentEntityPathReference} does not exist");
                            continue;
                        }
                    } else if (attachmentParent.__isset.parentEntityId)
                    {
                        parentEntity = EntitySerialisationRegistry.GetEntry((uint)attachmentParent.ParentEntityId);
                        if (parentEntity == null)
                        {
                            Debug.Log($"Error setting attachment parent: parent entity ID {attachmentParent.ParentEntityId} does not exist");
                            continue;
                        }
                    } else
                    {
                        parentEntity = null;
                    }
                    var physicalAttachment = entity.m_GameObject.GetComponent<ServerPhysicalAttachment>();
                    if (physicalAttachment == null)
                    {
                        Debug.Log($"Error setting attachment parent: entity {entity.m_Header.m_uEntityID} does not have a ServerPhysicalAttachment");
                        continue;
                    }
                    if (parentEntity == null)
                    {
                        var existingParent = physicalAttachment.transform.parent;
                        if (existingParent == null)
                        {
                            continue;
                        }
                        if (existingParent.GetComponent<ServerAttachStation>() is ServerAttachStation station)
                        {
                            Debug.Log($"[WARP] Detaching attachment {entity.m_Header.m_uEntityID} from parent ServerAttachStation");
                            station.TakeItem();
                        } else if (existingParent.GetComponent<ServerPlayerAttachmentCarrier>() is ServerPlayerAttachmentCarrier carrier)
                        {
                            Debug.Log($"[WARP] Detaching attachment {entity.m_Header.m_uEntityID} from parent ServerPlayerAttachmentCarrier");
                            carrier.TakeItem();
                        } else
                        {
                            Debug.Log($"Warning when detaching attachment for entity {entity.m_Header.m_uEntityID}: parent entity is of unhandled kind");
                            physicalAttachment.Detach();
                        }
                    } else
                    {
                        if (physicalAttachment.transform.parent == parentEntity.m_GameObject.transform)
                        {
                            continue;
                        }
                        if (parentEntity.m_GameObject.GetComponent<ServerAttachStation>() is ServerAttachStation station) {
                            if (station.HasItem())
                            {
                                Debug.Log($"[WARP] Detaching previous attachment of parent ServerAttachStation {parentEntity.m_Header.m_uEntityID}");
                                station.TakeItem();
                            }
                            Debug.Log($"[WARP] Attaching {entity.m_Header.m_uEntityID} to parent ServerAttachStation {parentEntity.m_Header.m_uEntityID}");
                            station.AddItem(entity.m_GameObject, default);
                            
                        } else if (parentEntity.m_GameObject.GetComponent<ServerPlayerAttachmentCarrier>() is ServerPlayerAttachmentCarrier carrier)
                        {
                            if (carrier.HasAttachment(PlayerAttachTarget.Default))
                            {
                                Debug.Log($"[WARP] Detaching previous attachment of parent ServerPlayerAttachmentCarrier {parentEntity.m_Header.m_uEntityID}");
                                carrier.TakeItem(PlayerAttachTarget.Default);
                            }
                            Debug.Log($"[WARP] Attaching {entity.m_Header.m_uEntityID} to parent ServerPlayerAttachmentCarrier {parentEntity.m_Header.m_uEntityID}");
                            carrier.CarryItem(entity.m_GameObject);
                        } else if (parentEntity.m_GameObject.GetComponent<IParentable>() is IParentable parentable)
                        {
                            Debug.Log($"Warning when attaching entity entity {entity.m_Header.m_uEntityID} to new parent {parentEntity.m_Header.m_uEntityID}: parent is of unhandled kind");
                            physicalAttachment.Attach(parentable);
                        } else { 
                            Debug.Log($"Error setting attachment parent: desired parent entity {parentEntity.m_Header.m_uEntityID} is not an IParentable");
                            continue;
                        }
                    }
                }

                if (entity.m_GameObject.GetPhysicsContainerIfExists() is Rigidbody container)
                {
                    if (entityThrift.__isset.position)
                    {
                        container.transform.position = entityThrift.Position.FromThrift();
                    }
                    if (entityThrift.__isset.rotation)
                    {
                        container.transform.rotation = entityThrift.Rotation.FromThrift();
                    }
                    if (entityThrift.__isset.velocity)
                    {
                        container.velocity = entityThrift.Velocity.FromThrift();
                    }
                    if (entityThrift.__isset.angularVelocity)
                    {
                        container.angularVelocity = entityThrift.AngularVelocity.FromThrift();
                    }
                }

                // Special case for chefs. Use PlayerRespawnBehaviour to detect if it's a chef.
                if (entity.m_GameObject.GetComponent<PlayerRespawnBehaviour>() != null)
                {
                    var rigidbody = entity.m_GameObject.GetComponent<Rigidbody>();
                    if (entityThrift.__isset.position)
                    {
                        rigidbody.transform.position = entityThrift.Position.FromThrift();
                    }
                    if (entityThrift.__isset.rotation)
                    {
                        rigidbody.transform.rotation = entityThrift.Rotation.FromThrift();
                    }
                    if (entityThrift.__isset.velocity)
                    {
                        rigidbody.velocity = entityThrift.Velocity.FromThrift();
                    }
                    if (entityThrift.__isset.angularVelocity)
                    {
                        rigidbody.angularVelocity = entityThrift.AngularVelocity.FromThrift();
                    }
                }

                // TODO: more properties to warp
            }

            // Pause the TimeManager again, to capture the velocities.
            Helpers.Pause();
            ServerInterceptionPatches.ClearCacheAfterWarp(warp.Frame);
        }
    }

}