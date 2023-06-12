using Hpmv;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    public class WarpHandler
    {
        public static StreamWriter writer = new StreamWriter("E:\\old-code-projects\\oc\\supercharged\\warp.log", false);

        private static void Log(string s)
        {
            writer.WriteLine(s);
            writer.Flush();
        }

        public static void Destroy()
        {
            writer.Close();
        }

        public static void HandleWarpRequestIfAny()
        {
            var input = Injector.Server.CurrentInput;
            if (input.Warp == null)
            {
                return;
            }
            if (!Helpers.IsPaused())
            {
                Log("[WARP] Warning: Not warping because the game is not paused!");
                return;
            }

            Log($"[WARP] Begin warping to frame {input.Warp.Frame}");

            // Resume the time manager before warping, because at the end we need to pause again to save the velocities.
            Helpers.Resume();

            var warp = input.Warp;

            Dictionary<EntityPathReference, EntitySerialisationEntry> entityPathReferenceToEntry = new Dictionary<EntityPathReference, EntitySerialisationEntry>();

            Func<EntityIdOrRef, EntitySerialisationEntry> getEntityByIdOrRef = (EntityIdOrRef idOrRef) =>
            {
                if (idOrRef.__isset.entityId)
                {
                    return EntitySerialisationRegistry.GetEntry((uint)idOrRef.EntityId);
                }
                var reference = idOrRef.EntityPathReference.FromThrift();
                return entityPathReferenceToEntry.ContainsKey(reference) ? entityPathReferenceToEntry[reference] : null;
            };

            for (int i = 0; i < warp.Entities.Count; i++)
            {
                var entityThrift = warp.Entities[i];
                EntitySerialisationEntry entity;
                if (entityThrift.__isset.entityId)
                {
                    entity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.EntityId);
                    if (entity == null)
                    {
                        Log($"[WARP] Error warping existing entity {entityThrift.EntityId}: no such entity with this ID!");
                        continue;
                    }
                }
                else if (entityThrift.SpawningPath.Count > 0)
                {
                    var spawningPathStr = string.Join(", ", entityThrift.SpawningPath.Select(p => "" + p).ToArray());
                    if (entityThrift.SpawningPath.Count == 1)
                    {
                        Log($"[WARP] Error spawning new entity for warping: Spawning path {spawningPathStr} is empty!");
                        continue;
                    }
                    var spawnerEntity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.SpawningPath[0]);
                    if (spawnerEntity == null)
                    {
                        Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: Spawner entity ID does not exist!");
                        continue;
                    }
                    var success = true;
                    for (var j = 1; j < entityThrift.SpawningPath.Count; j++)
                    {
                        var spawnableIndex = entityThrift.SpawningPath[j];
                        var spawnableEntities = spawnerEntity.m_GameObject.GetComponent<SpawnableEntityCollection>();
                        if (spawnableEntities == null)
                        {
                            Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: The {j}th spawner does not have a SpawnableEntityCollection!");
                            success = false;
                            break;
                        }
                        var prefab = spawnableEntities.GetSpawnableEntityByIndex(spawnableIndex);
                        if (prefab == null)
                        {
                            Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: The {j}th spawn's index is out of bounds!");
                            success = false;
                            break;
                        }
                        var spawned = NetworkUtils.ServerSpawnPrefab(spawnerEntity.m_GameObject, prefab);
                        if (spawned.GetComponent<ServerPhysicalAttachment>() is ServerPhysicalAttachment spa)
                        {
                            // This is needed to ensure that when we first spawn it, it is properly contained in its
                            // Rigidbody container, i.e. a valid detached state. Otherwise, it is not in a valid
                            // state - it could be detached but not in a Rigidbody container.
                            spa.ManualEnable();
                        }
                        var spawnedEntry = EntitySerialisationRegistry.GetEntry(spawned);
                        Log($"[WARP] Spawned entity {spawnedEntry.m_Header.m_uEntityID} along path {spawningPathStr}");
                        if (spawnedEntry == null)
                        {
                            Log($"[WARP] Error spawning new entity {spawningPathStr} for warping: Failed to get the EntitySerialisationRegistry after the {j}th spawn!");
                            success = false;
                            break;
                        }
                        if (j > 1)
                        {
                            Log($"[WARP] Destroying intermediate entity {spawnerEntity.m_Header.m_uEntityID}");
                            NetworkUtils.DestroyObject(spawnerEntity.m_GameObject);
                        }
                        spawnerEntity = spawnedEntry;
                    }
                    if (!success)
                    {
                        continue;
                    }
                    entity = spawnerEntity;
                }
                else
                {
                    Log($"[WARP] Error warping entity (warp spec index {i}): neither entityId nor spawningPath was specified!");
                    continue;
                }

                if (entityThrift.__isset.entityPathReference)
                {
                    var entityPathReference = entityThrift.EntityPathReference.FromThrift();
                    entityPathReferenceToEntry[entityPathReference] = entity;
                    var marker = entity.m_GameObject.GetComponent<EntityPathReferenceMarker>() ??
                        entity.m_GameObject.AddComponent<EntityPathReferenceMarker>();
                    marker.EntityPath = entityPathReference;
                    Log($"[WARP] Set entity path reference of {entity.m_Header.m_uEntityID} to {entityPathReference}");
                }
            }

            // We do several iterations, because the order in which we warp specific properties is important.
            Action<Action<EntitySerialisationEntry, EntityWarpSpec>> forEachEntity = (Action<EntitySerialisationEntry, EntityWarpSpec> action) =>
            {
                for (int i = 0; i < warp.Entities.Count; i++)
                {
                    var entityThrift = warp.Entities[i];
                    EntitySerialisationEntry entity;
                    if (entityThrift.__isset.entityId)
                    {
                        entity = EntitySerialisationRegistry.GetEntry((uint)entityThrift.EntityId);
                        if (entity == null)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (!entityPathReferenceToEntry.TryGetValue(entityThrift.EntityPathReference.FromThrift(), out entity))
                        {
                            continue;
                        }
                    }
                    action(entity, entityThrift);
                }
            };

            // Remove stuff first, before adding stuff later. Attachments for example need to be free before
            // it's attached to something else.
            forEachEntity((entity, entityThrift) =>
            {
                if (entity.m_GameObject.GetComponent<ServerWorkstation>() is ServerWorkstation sw)
                {
                    foreach (var interacter in sw.Interacters())
                    {
                        sw.OnInteracterRemoved(interacter.m_object);
                    }
                }
                if (entity.m_GameObject.GetComponent<ServerAttachStation>() is ServerAttachStation sas)
                {
                    var thrift = entityThrift.AttachStation;
                    if (thrift == null)
                    {
                        Log($"[WARP] Error warping attach station {entity.m_Header.m_uEntityID}: AttachStation is null");
                        return;
                    }
                    if (sas.m_item() is IAttachment ia)
                    {
                        var existingEntity = ia.AccessGameObject();
                        if (thrift.Item == null || existingEntity != getEntityByIdOrRef(thrift.Item)?.m_GameObject)
                        {
                            sas.TakeItem();
                        }
                    }
                }
                if (entity.m_GameObject.GetComponent<ServerPlayerAttachmentCarrier>() is ServerPlayerAttachmentCarrier spac)
                {
                    var thrift = entityThrift.ChefCarry;
                    if (thrift == null)
                    {
                        Log($"[WARP] Error warping chef carry {entity.m_Header.m_uEntityID}: ChefCarry is null");
                        return;
                    }
                    // TODO: backpacks uses array index 1. We're not handling that yet.
                    if (spac.m_carriedObjects()[0] is IAttachment ia)
                    {
                        var existingEntity = ia.AccessGameObject();
                        if (thrift.CarriedItem == null || existingEntity != getEntityByIdOrRef(thrift.CarriedItem)?.m_GameObject)
                        {
                            spac.TakeItem();
                        }
                    }
                }
                if (entity.m_GameObject.GetComponent<ServerTerminal>() is ServerTerminal st)
                {
                    var session = st.GetSession();
                    if (session != null)
                    {
                        session.OnSessionEnded();
                        st.OnSessionEnded();
                    }
                }
                if (entity.m_GameObject.GetComponent<Stack>() != null)
                {
                    var serverPlateStack = entity.m_GameObject.GetComponent<ServerPlateStackBase>();
                    var clientPlateStack = entity.m_GameObject.GetComponent<ClientPlateStackBase>();
                    if (serverPlateStack == null)
                    {
                        Log($"[WARP] Error warping stack {entity.m_Header.m_uEntityID}: ServerPlateStackBase is null");
                        return;
                    }
                    if (clientPlateStack == null)
                    {
                        Log($"[WARP] Error warping stack {entity.m_Header.m_uEntityID}: ClientPlateStackBase is null");
                        return;
                    }
                    serverPlateStack.RemoveAllFromStack();
                    clientPlateStack.RemoveAllFromStack();
                }
            });

            // Handle attachment changes.
            forEachEntity((entity, entityThrift) =>
            {
                if (entity.m_GameObject.GetComponent<ServerAttachStation>() is ServerAttachStation sas)
                {
                    var thrift = entityThrift.AttachStation;
                    if (thrift == null)
                    {
                        Log($"[WARP] Error warping attach station {entity.m_Header.m_uEntityID}: AttachStation is null");
                        return;
                    }
                    if (sas.m_item() == null && thrift.Item != null)
                    {
                        var newEntity = getEntityByIdOrRef(thrift.Item);
                        if (newEntity == null)
                        {
                            Log($"[WARP] Error warping attach station {entity.m_Header.m_uEntityID}: Failed to get new item entity {thrift.Item}");
                            return;
                        }
                        sas.AddItem(newEntity.m_GameObject, default);
                    }
                }
                if (entity.m_GameObject.GetComponent<ServerPlayerAttachmentCarrier>() is ServerPlayerAttachmentCarrier spac)
                {
                    var thrift = entityThrift.ChefCarry;
                    if (thrift == null)
                    {
                        Log($"[WARP] Error warping chef carry {entity.m_Header.m_uEntityID}: ChefCarry is null");
                        return;
                    }
                    // TODO: backpacks uses array index 1. We're not handling that yet.
                    if (spac.m_carriedObjects()[0] == null && thrift.CarriedItem != null)
                    {
                        var newEntity = getEntityByIdOrRef(thrift.CarriedItem);
                        if (newEntity == null)
                        {
                            Log($"[WARP] Error warping chef carry {entity.m_Header.m_uEntityID}: Failed to get new carried item entity {thrift.CarriedItem}");
                            return;
                        }
                        spac.CarryItem(newEntity.m_GameObject);
                    }
                }
            });

            // Handle specific component warping.
            forEachEntity((entity, entityThrift) =>
            {
                if (entity.m_GameObject.GetComponent<ServerCannonMod>() is ServerCannonMod cannon)
                {
                    if (entityThrift.Cannon == null)
                    {
                        Log($"[WARP] Failed to warp cannon: no cannon specific warp data");
                        return;
                    }
                    CannonModMessage message = new CannonModMessage();
                    if (entityThrift.Cannon.ModData != null)
                    {
                        message.Deserialise(new BitStream.BitStreamReader(entityThrift.Cannon.ModData));
                    }
                    cannon.Warp(message);
                }

                if (entity.m_GameObject.GetComponent<ServerWorkstation>() is ServerWorkstation sw)
                {
                    var workstation = entityThrift.Workstation;
                    if (workstation == null)
                    {
                        Log($"[WARP] Failed to warp Workstation: no workstation specific data");
                        return;
                    }
                    if (sw.Interacters().Count != 0)
                    {
                        Log($"[WARP] Error warping Workstation: workstation should no longer have interacters after previous stage");
                        return;
                    }

                    if (workstation.Interacters != null)
                    {
                        foreach (var interacter in workstation.Interacters)
                        {
                            var chef = EntitySerialisationRegistry.GetEntry((uint)interacter.ChefEntityId)?.m_GameObject;
                            if (chef == null)
                            {
                                Log($"[WARP] Failed to warp Workstation: chef entity {interacter.ChefEntityId} does not exist");
                                return;
                            }
                            sw.OnInteracterAdded(chef, Vector2.up);  // direction doesn't matter
                        }

                        var interacters = sw.Interacters();
                        if (interacters.Count != workstation.Interacters.Count)
                        {
                            Log($"[WARP] Failed to warp Workstation: was not able to add desired interacters");
                            return;
                        }
                        for (int j = 0; j < workstation.Interacters.Count; j++)
                        {
                            var interacter = workstation.Interacters[j];
                            interacters[j].m_actionTimer = (float)interacter.ActionTimer;
                        }
                    }
                }

                if (entity.m_GameObject.GetComponent<ServerWorkableItem>() is ServerWorkableItem wi)
                {
                    var workableItem = entityThrift.WorkableItem;
                    if (workableItem == null)
                    {
                        Log($"[WARP] Failed to warp WorkableItem: no WorkableItem specific data");
                        return;
                    }

                    wi.SetOnWorkstation(workableItem.OnWorkstation);
                    wi.SetProgress(workableItem.Progress);
                    wi.SetSubProgress(workableItem.SubProgress);
                    wi.SynchroniseClientState();
                }

                if (entity.m_GameObject.GetComponent<ServerThrowableItem>() is ServerThrowableItem sti)
                {
                    var throwableItem = entityThrift.ThrowableItem;
                    var cti = sti.GetComponent<ClientThrowableItem>();
                    var wasFlying = sti.m_flying();
                    if (throwableItem == null)
                    {
                        Log($"[WARP] Failed to warp ThrowableItem: no ThrowableItem specific data");
                        return;
                    }

                    sti.SetFlying(throwableItem.IsFlying);
                    cti.SetIsFlying(throwableItem.IsFlying);
                    sti.SetFlightTimer((float)throwableItem.FlightTimer);
                    if (throwableItem.ThrowerEntityId == -1)
                    {
                        sti.SetThrower(null);
                        cti.SetThrower(null);
                    }
                    else
                    {
                        var thrower = EntitySerialisationRegistry.GetEntry((uint)throwableItem.ThrowerEntityId)?.m_GameObject;
                        if (thrower == null)
                        {
                            Log($"[WARP] Failed to warp ThrowableItem: thrower entity {throwableItem.ThrowerEntityId} does not exist");
                            return;
                        }
                        sti.SetThrower(thrower.GetComponent<IThrower>());
                        cti.SetThrower(thrower.GetComponent<IClientThrower>());
                    }

                    var newColliders = new List<Collider>();
                    foreach (var collider in throwableItem.ThrowStartColliders)
                    {
                        var colliderEntity = getEntityByIdOrRef(collider.Entity);
                        if (colliderEntity == null)
                        {
                            Log($"[WARP] Warning: failed to find collider entity {collider.Entity}");
                            continue;  // inner loop.
                        }
                        var colliders = colliderEntity.m_GameObject.GetComponents<Collider>();
                        if (collider.ColliderIndex >= colliders.Length)
                        {
                            Log($"[WARP] Warning: collider entity {collider.Entity} does not have collider index {collider.ColliderIndex}");
                            continue;  // inner loop
                        }
                        newColliders.Add(colliders[collider.ColliderIndex]);
                    }

                    var originalColliderCount = sti.m_ignoredCollidersCount();
                    for (var j = 0; j < originalColliderCount; j++)
                    {
                        Physics.IgnoreCollision(sti.m_Collider(), sti.m_ThrowStartColliders()[j], false);
                    }
                    sti.SetIgnoredCollidersCount(newColliders.Count);
                    sti.SetThrowStartColliders(newColliders.ToArray());
                    foreach (var collider in newColliders)
                    {
                        Physics.IgnoreCollision(sti.m_Collider(), collider, true);
                    }

                    if (throwableItem.IsFlying && !wasFlying)
                    {
                        sti.OnEnterFlightForWarping();
                        cti.OnEnterFlightForWarping();
                    }
                    else if (!throwableItem.IsFlying && wasFlying)
                    {
                        sti.OnExitFlightForWarping();
                        cti.OnExitFlightForWarping();
                    }
                }

                if (entity.m_GameObject.GetComponent<ServerTerminal>() is ServerTerminal st)
                {
                    var terminal = entityThrift.Terminal;
                    if (terminal == null)
                    {
                        Log($"[WARP] Failed to warp Terminal: no Terminal specific data");
                        return;
                    }
                    if (st.GetSession() != null)
                    {
                        Log($"[WARP] Warning: Terminal should no longer have a session after previous stage");
                        return;
                    }
                    if (terminal.__isset.interacterEntityId)
                    {
                        var interacter = EntitySerialisationRegistry.GetEntry((uint)terminal.InteracterEntityId)?.m_GameObject;
                        if (interacter == null)
                        {
                            Log($"[WARP] Failed to warp Terminal: interacter entity {terminal.InteracterEntityId} does not exist");
                            return;
                        }
                        st.StartSession(interacter, default);
                        if (st.GetSession() == null)
                        {
                            Log($"[WARP] Warning: Terminal failed to start session; session is still null after StartSession");
                            return;
                        }
                    }
                }

                if (entity.m_GameObject.GetComponent<ServerPilotRotation>() is ServerPilotRotation spr)
                {
                    var pilotRotation = entityThrift.PilotRotation;
                    if (pilotRotation == null)
                    {
                        Log($"[WARP] Failed to warp PilotRotation: no Rotation specific data");
                        return;
                    }
                    // Set the angle on the server side.
                    spr.SetAngle((float)pilotRotation.Angle - spr.m_startAngle());
                    spr.m_message().m_angle = (float)pilotRotation.Angle;

                    // Set the angle on the client side too. Strictly speaking this is not the exact behavior
                    // because the client side rotates the graphical angle with a very slight delay. However,
                    // this is unlikely to matter in practice so we just set it.
                    var transform = entity.m_GameObject.GetComponent<PilotRotation>().m_transformToRotate;
                    var eulerAngles = transform.rotation.eulerAngles;
                    eulerAngles.y = (float)pilotRotation.Angle;
                    transform.rotation = UnityEngine.Quaternion.Euler(eulerAngles);
                    var client = entity.m_GameObject.GetComponent<ClientPilotRotation>();
                    client.SetNextRotation(transform.rotation);
                }

                if (entity.m_GameObject.GetComponent<ServerIngredientContainer>() is ServerIngredientContainer sic)
                {
                    var container = entityThrift.IngredientContainer;
                    if (container == null)
                    {
                        Log($"[WARP] Failed to warp IngredientContainer: no IngredientContainer specific data");
                        return;
                    }
                    IngredientContainerMessage msg = new IngredientContainerMessage();
                    msg.Initialise(new AssembledDefinitionNode[] { });
                    if (container.MsgData != null) {
                        msg.Deserialise(new BitStream.BitStreamReader(container.MsgData));
                    }
                    sic.SetContentsAndForceOnContentsChanged(msg.Contents.ToList());
                }

                if (entity.m_GameObject.GetComponent<ServerCookingHandler>() is ServerCookingHandler sch)
                {
                    var handler = entityThrift.CookingHandler;
                    if (handler == null)
                    {
                        Log($"[WARP] Failed to warp CookingHandler: no CookingHandler specific data");
                        return;
                    }
                    sch.SetCookingProgressAndForceStateChangedCallback((float)handler.Progress);
                }

                if (entity.m_GameObject.GetComponent<ServerMixingHandler>() is ServerMixingHandler smh)
                {
                    var handler = entityThrift.MixingHandler;
                    if (handler == null)
                    {
                        Log($"[WARP] Failed to warp MixingHandler: no MixingHandler specific data");
                        return;
                    }
                    smh.SetMixingProgressAndForceStateChangedCallback((float)handler.Progress);
                }

                if (entity.m_GameObject.GetComponent<ServerPickupItemSwitcher>() is ServerPickupItemSwitcher spis)
                {
                    var thrift = entityThrift.PickupItemSwitcher;
                    if (thrift == null)
                    {
                        Log($"[WARP] Failed to warp PickupItemSwitcher: no PickupItemSwitcher specific data");
                        return;
                    }
                    spis.SetCurrentItemPrefabIndexAndSendServerEvent(thrift.Index);
                }

                if (entity.m_GameObject.GetComponent<ServerTriggerColourCycle>() is ServerTriggerColourCycle stcc)
                {
                    var thrift = entityThrift.TriggerColourCycle;
                    if (thrift == null)
                    {
                        Log($"[WARP] Failed to warp TriggerColourCycle: no TriggerColourCycle specific data");
                        return;
                    }
                    stcc.SetCurrentColourIndexAndSendServerEvent(thrift.Index);
                }

                if (entity.m_GameObject.GetComponent<Stack>() != null)
                {
                    var thrift = entityThrift.Stack;
                    if (thrift == null)
                    {
                        Log($"[WARP] Failed to warp Stack: no Stack specific data");
                        return;
                    }
                    var stackItems = new List<GameObject>();
                    foreach (var item in thrift.StackContents)
                    {
                        var stackItem = getEntityByIdOrRef(item);
                        if (stackItem == null)
                        {
                            Log($"[WARP] Failed to warp Stack: stack item {item} does not exist");
                            return;
                        }
                        stackItems.Add(stackItem.m_GameObject);
                    }
                    var serverPlateStack = entity.m_GameObject.GetComponent<ServerPlateStackBase>();
                    var clientPlateStack = entity.m_GameObject.GetComponent<ClientPlateStackBase>();
                    if (serverPlateStack == null)
                    {
                        Log($"[WARP] Failed to warp Stack: entity is not a ServerPlateStackBase");
                        return;
                    }
                    if (clientPlateStack == null)
                    {
                        Log($"[WARP] Failed to warp Stack: entity is not a ClientPlateStackBase");
                        return;
                    }
                    foreach (var item in stackItems)
                    {
                        serverPlateStack.AddToStackAfterTheFact(item);
                        clientPlateStack.AddToStackAfterTheFact(item);
                    }
                }

                if (entity.m_GameObject.GetComponent<ServerKitchenFlowControllerBase>() is ServerKitchenFlowControllerBase skfcb)
                {
                    {
                        var thrift = entityThrift.PlateReturnController;
                        if (thrift == null)
                        {
                            Log($"[WARP] Failed to warp KitchenFlowController: no KitchenFlowController specific data");
                            return;
                        }
                        var ptr = skfcb.GetPlateReturnController().m_platesToReturn();
                        ptr.Clear();
                        if (thrift.Plates != null) {
                            foreach (var plate in thrift.Plates)
                            {
                                var returnStation = EntitySerialisationRegistry.GetEntry((uint)plate.ReturnStationEntityId)?.m_GameObject?.GetComponent<ServerPlateReturnStation>();
                                if (returnStation == null)
                                {
                                    Log($"[WARP] Failed to warp KitchenFlowController: plate return station {plate.ReturnStationEntityId} does not exist");
                                    continue;
                                }
                                ptr.Add(PlateReturnController_PlatesPendingReturnExt.Create(
                                    returnStation, (float)plate.Timer, returnStation.GetPlatingStep()
                                    ));
                            }
                        }
                    }
                }
            });

            // Handle position setting and chef warping last.
            forEachEntity((entity, entityThrift) =>
            {
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
                if (entity.m_GameObject.GetComponent<ClientPlayerControlsImpl_Default>() is ClientPlayerControlsImpl_Default cpci)
                {
                    var chef = entityThrift.Chef;
                    if (chef == null)
                    {
                        Log($"[WARP] Failed to warp chef: no chef specific warp data");
                        return;
                    }
                    cpci.set_m_dashTimer((float)chef.DashTimer);
                    if (chef.InteractingEntity != -1)
                    {
                        var entry = EntitySerialisationRegistry.GetEntry((uint)chef.InteractingEntity);
                        if (entry == null)
                        {
                            Log($"[WARP] Failed to warp chef's interacting entity: entity ID {chef.InteractingEntity} does not exist");
                            return;
                        }
                        var clientInteractable = entry.m_GameObject.GetComponent<ClientInteractable>();
                        if (clientInteractable == null)
                        {
                            Log($"[WARP] Failed to warp chef's interacting entity: entity with ID {chef.InteractingEntity} does not have ClientInteractable component");
                            return;
                        }
                        cpci.set_m_predictedInteracted(clientInteractable);

                        // We also need to set this on the server side. This seems to be the only thing the server keeps track of.
                        // The reason we need this is because the server side performs EndInteraction triggers based on this value.
                        var serverInteractable = entry.m_GameObject.GetComponent<ServerInteractable>();
                        if (serverInteractable == null)
                        {
                            Log($"[WARP] Failed to warp chef's interacting entity: entity with ID {chef.InteractingEntity} does not have ServerInteractable component");
                            return;
                        }
                        cpci.GetComponent<ServerPlayerControlsImpl_Default>().set_m_lastInteracted(serverInteractable);
                    }
                    else
                    {
                        cpci.set_m_predictedInteracted(null);
                        cpci.GetComponent<ServerPlayerControlsImpl_Default>().set_m_lastInteracted(null);
                    }
                    cpci.set_m_lastVelocity(chef.LastVelocity.FromThrift());
                    cpci.set_m_aimingThrow(chef.AimingThrow);
                    cpci.set_m_movementInputSuppressed(chef.MovementInputSuppressed);
                    cpci.set_m_lastMoveInputDirection(chef.LastMoveInputDirection.FromThrift());
                    cpci.set_m_impactStartTime((float)chef.ImpactStartTime);
                    cpci.set_m_impactTimer((float)chef.ImpactTimer);
                    cpci.set_m_impactVelocity(chef.ImpactVelocity.FromThrift());
                    cpci.set_m_LeftOverTime((float)chef.LeftOverTime);

                    // Don't suppressUse. This flag is for players who hold the use button and then enter some
                    // session interaction (such as cannon or terminal); we don't want the use button to still
                    // be considered pressed when exiting from them. We don't take advantage of this for TAS,
                    // so just set it to false.
                    cpci.GetComponent<PlayerControls>().ControlScheme.SetUseSuppressed(false);

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

                    entity.m_GameObject.GetComponent<GroundCast>().ForceUpdateNow();
                }
            });

            // Delete things at the end. This is because we might have needed to properly cleanup things,
            // like removing something from a workstation when it's deleted.
            foreach (var entityId in warp.EntitiesToDelete)
            {
                var entity = EntitySerialisationRegistry.GetEntry((uint)entityId);
                if (entity == null)
                {
                    Log($"[WARP] Error destroying entity ID {entityId}, entity does not exist!");
                    continue;
                }
                NetworkUtils.DestroyObject(entity.m_GameObject);
                Log($"[WARP] Destroyed entity {entityId}");
            }

            // Make sure we flush any network messages after warping - otherwise they might be pushed to
            // next frame and that's not good.
            GameObject.FindObjectOfType<MultiplayerController>()?.FlushAllPendingBatchedMessages();
            // Pause the TimeManager again, to capture the velocities.
            Helpers.Pause();
            ActiveStateCollector.ClearCacheAfterWarp(warp.Frame);
            Log($"[WARP] Finished warping to frame {warp.Frame}");
        }
    }

}
