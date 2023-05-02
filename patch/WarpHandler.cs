using Hpmv;
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

            Debug.Log("Begin warping!");
            var warp = input.Warp;
            foreach (var entityId in warp.EntitiesToDelete)
            {
                var entity = EntitySerialisationRegistry.GetEntry((uint)entityId);
                if (entity == null)
                {
                    Debug.Log($"Error destroying entity ID {entityId}, entity does not exist!");
                    continue;
                }
                NetworkUtils.DestroyObject(entity.m_GameObject);
                Debug.Log($"Destroyed entity {entityId}");            }

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
                        Debug.Log($"Error warping existing entity {entityThrift.EntityId}: no such entity with this ID!");
                        continue;
                    }
                } else if (entityThrift.SpawningPath.Count > 0)
                {
                    var spawningPathStr = string.Join(", ", entityThrift.SpawningPath.Select(p => "" + p).ToArray());
                    if (entityThrift.SpawningPath.Count == 1)
                    {
                        Debug.Log($"Error spawning new entity for warping: Spawning path {spawningPathStr} is empty!");
                        continue;
                    }
                    var spawnerEntity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.SpawningPath[0]);
                    if (spawnerEntity == null)
                    {
                        Debug.Log($"Error spawning new entity {spawningPathStr} for warping: Spawner entity ID does not exist!");
                        continue;
                    }
                    var success = true;
                    for (var j = 1; j < entityThrift.SpawningPath.Count; j++)
                    {
                        var spawnableIndex = entityThrift.SpawningPath[j];
                        var spawnableEntities = spawnerEntity.m_GameObject.GetComponent<SpawnableEntityCollection>();
                        if (spawnableEntities == null)
                        {
                            Debug.Log($"Error spawning new entity {spawningPathStr} for warping: The {j}th spawner does not have a SpawnableEntityCollection!");
                            success = false;
                            break;
                        }
                        var prefab = spawnableEntities.GetSpawnableEntityByIndex(j);
                        if (prefab == null)
                        {
                            Debug.Log($"Error spawning new entity {spawningPathStr} for warping: The {j}th spawn's index is out of bounds!");
                            success = false;
                            break;
                        }
                        var spawned = NetworkUtils.ServerSpawnPrefab(spawnerEntity.m_GameObject, prefab);
                        var spawnedEntry = EntitySerialisationRegistry.GetEntry(spawned);
                        if (spawnedEntry == null)
                        {
                            Debug.Log($"Error spawning new entity {spawningPathStr} for warping: Failed to get the EntitySerialisationRegistry after the {j}th spawn!");
                            success = false;
                            break;
                        }
                        NetworkUtils.DestroyObject(spawnerEntity.m_GameObject);
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
                        physicalAttachment.Detach();
                    } else
                    {
                        var parentable = parentEntity.m_GameObject.GetComponent<IParentable>();
                        if (parentable == null)
                        {
                            Debug.Log($"Error setting attachment parent: desired parent entity {parentEntity.m_Header.m_uEntityID} is not an IParentable");
                            continue;
                        }
                        physicalAttachment.Attach(parentable);
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

                // TODO: more properties to warp
            }

        }
    }

}
