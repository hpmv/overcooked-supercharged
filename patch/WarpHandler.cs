using Hpmv;
using OrderController;
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

        private Dictionary<EntityPathReference, EntitySerialisationEntry> entityPathReferenceToEntry = new Dictionary<EntityPathReference, EntitySerialisationEntry>();
        private MultiplayerController multiplayerController;
        private WarpSpec warp;

        private WarpHandler(WarpSpec warp)
        {
            multiplayerController = GameObject.FindObjectOfType<MultiplayerController>();
            this.warp = warp;
        }

        private EntitySerialisationEntry GetEntityByIdOrRef(EntityIdOrRef idOrRef)
        {
            if (idOrRef.__isset.entityId)
            {
                return EntitySerialisationRegistry.GetEntry((uint)idOrRef.EntityId);
            }
            var reference = idOrRef.EntityPathReference.FromThrift();
            return entityPathReferenceToEntry.ContainsKey(reference) ? entityPathReferenceToEntry[reference] : null;
        }

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

            var warpHandler = new WarpHandler(input.Warp);
            warpHandler.Warp();

            // Pause the TimeManager again, to capture the velocities.
            Helpers.Pause();
            ActiveStateCollector.ClearCacheAfterWarp(input.Warp.Frame);
            Log($"[WARP] Finished warping to frame {input.Warp.Frame}");
        }

        private void Warp()
        {
            SpawnNewEntities();

            // Remove stuff first, before adding stuff later. Attachments for example need to be free before
            // it's attached to something else.
            ForEachEntity(WarpRemovals);

            // Flush messages here, so that interaction messages are propagated.
            FlushAllPendingBatchedMessages();

            // Handle attachment changes.
            ForEachEntity(WarpAttachments);

            // Flush messages here, so that attachment messages are propagated.
            FlushAllPendingBatchedMessages();

            // Handle specific component warping.
            ForEachEntity(WarpSpecificComponents);

            // Handle position setting and chef warping.
            ForEachEntity(WarpChefAndPositions);

            // Flush messages again to propagate chef interactions.
            FlushAllPendingBatchedMessages();

            // Finally deal with anything that depends on chef interaction propagation.
            ForEachEntity(WarpAfterChefInteractionPropagation);

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

            // Flush one last time.
            FlushAllPendingBatchedMessages();

            StateInvalidityManager.InvalidReason = warp.InvalidStateReason;
        }

        private void FlushAllPendingBatchedMessages()
        {
            multiplayerController?.FlushAllPendingBatchedMessages();
        }
        private void SpawnNewEntities()
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
        }

        private void ForEachEntity(Action<EntitySerialisationEntry, EntityWarpSpec> action)
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
        }

        private void WarpRemovals(EntitySerialisationEntry entity, EntityWarpSpec entityThrift)
        {
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
                    if (thrift.Item == null || existingEntity != GetEntityByIdOrRef(thrift.Item)?.m_GameObject)
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
                    if (thrift.CarriedItem == null || existingEntity != GetEntityByIdOrRef(thrift.CarriedItem)?.m_GameObject)
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
            if (entity.m_GameObject.GetComponent<ServerPlayerControlsImpl_Default>() is ServerPlayerControlsImpl_Default spci)
            {
                if (spci.m_lastInteracted() != null)
                {
                    spci.EndInteraction();
                }
            }
        }

        private void WarpAttachments(EntitySerialisationEntry entity, EntityWarpSpec entityThrift)
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
                    var newEntity = GetEntityByIdOrRef(thrift.Item);
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
                    var newEntity = GetEntityByIdOrRef(thrift.CarriedItem);
                    if (newEntity == null)
                    {
                        Log($"[WARP] Error warping chef carry {entity.m_Header.m_uEntityID}: Failed to get new carried item entity {thrift.CarriedItem}");
                        return;
                    }
                    spac.CarryItem(newEntity.m_GameObject);
                }
            }
        }

        private void WarpSpecificComponents(EntitySerialisationEntry entity, EntityWarpSpec entityThrift)
        {
            if (entity.m_GameObject.GetComponent<ServerCannonMod>() is ServerCannonMod cannon)
            {
                if (entityThrift.Cannon == null)
                {
                    Log($"[WARP] Failed to warp cannon {entity.m_Header.m_uEntityID}: no cannon specific warp data");
                    return;
                }
                CannonModMessage message = new CannonModMessage();
                if (entityThrift.Cannon.ModData != null)
                {
                    message.Deserialise(new BitStream.BitStreamReader(entityThrift.Cannon.ModData));
                }
                cannon.Warp(message);
            }

            if (entity.m_GameObject.GetComponent<ServerWorkableItem>() is ServerWorkableItem wi)
            {
                var workableItem = entityThrift.WorkableItem;
                if (workableItem == null)
                {
                    Log($"[WARP] Failed to warp WorkableItem {entity.m_Header.m_uEntityID}: no WorkableItem specific data");
                    return;
                }

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
                    Log($"[WARP] Failed to warp ThrowableItem {entity.m_Header.m_uEntityID}: no ThrowableItem specific data");
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
                        Log($"[WARP] Failed to warp ThrowableItem {entity.m_Header.m_uEntityID}: thrower entity {throwableItem.ThrowerEntityId} does not exist");
                        return;
                    }
                    sti.SetThrower(thrower.GetComponent<IThrower>());
                    cti.SetThrower(thrower.GetComponent<IClientThrower>());
                }

                var newColliders = new List<Collider>();
                foreach (var collider in throwableItem.ThrowStartColliders)
                {
                    var colliderEntity = GetEntityByIdOrRef(collider.Entity);
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
                    Log($"[WARP] Failed to warp Terminal {entity.m_Header.m_uEntityID}: no Terminal specific data");
                    return;
                }
                if (st.GetSession() != null)
                {
                    Log($"[WARP] Warning: Terminal {entity.m_Header.m_uEntityID} should no longer have a session after previous stage");
                    return;
                }
                if (terminal.__isset.interacterEntityId)
                {
                    var interacter = EntitySerialisationRegistry.GetEntry((uint)terminal.InteracterEntityId)?.m_GameObject;
                    if (interacter == null)
                    {
                        Log($"[WARP] Failed to warp Terminal {entity.m_Header.m_uEntityID}: interacter entity {terminal.InteracterEntityId} does not exist");
                        return;
                    }
                    st.StartSession(interacter, default);
                    if (st.GetSession() == null)
                    {
                        Log($"[WARP] Warning: Terminal {entity.m_Header.m_uEntityID} failed to start session; session is still null after StartSession");
                        return;
                    }
                }
            }

            if (entity.m_GameObject.GetComponent<ServerPilotRotation>() is ServerPilotRotation spr)
            {
                var pilotRotation = entityThrift.PilotRotation;
                if (pilotRotation == null)
                {
                    Log($"[WARP] Failed to warp PilotRotation {entity.m_Header.m_uEntityID}: no Rotation specific data");
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
                    Log($"[WARP] Failed to warp IngredientContainer {entity.m_Header.m_uEntityID}: no IngredientContainer specific data");
                    return;
                }
                IngredientContainerMessage msg = new IngredientContainerMessage();
                msg.Initialise(new AssembledDefinitionNode[] { });
                if (container.MsgData != null)
                {
                    msg.Deserialise(new BitStream.BitStreamReader(container.MsgData));
                }
                sic.SetContentsAndForceOnContentsChanged(msg.Contents.ToList());
            }

            if (entity.m_GameObject.GetComponent<ServerCookingHandler>() is ServerCookingHandler sch)
            {
                var handler = entityThrift.CookingHandler;
                if (handler == null)
                {
                    Log($"[WARP] Failed to warp CookingHandler {entity.m_Header.m_uEntityID}: no CookingHandler specific data");
                    return;
                }
                sch.SetCookingProgressAndForceStateChangedCallback((float)handler.Progress);
            }

            if (entity.m_GameObject.GetComponent<ServerCookingStation>() is ServerCookingStation scs)
            {
                var thrift = entityThrift.CookingStation;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp CookingStation {entity.m_Header.m_uEntityID}: no CookingStation specific data");
                    return;
                }
                scs.SetIsTurnedOn(thrift.IsTurnedOn);
                scs.SetIsCooking(thrift.IsCooking);
                scs.SendServerEvent(new CookingStationMessage
                {
                    m_isTurnedOn = thrift.IsTurnedOn,
                    m_isCooking = thrift.IsCooking,
                });
            }

            if (entity.m_GameObject.GetComponent<ServerMixingHandler>() is ServerMixingHandler smh)
            {
                var handler = entityThrift.MixingHandler;
                if (handler == null)
                {
                    Log($"[WARP] Failed to warp MixingHandler {entity.m_Header.m_uEntityID}: no MixingHandler specific data");
                    return;
                }
                smh.SetMixingProgressAndForceStateChangedCallback((float)handler.Progress);
            }

            if (entity.m_GameObject.GetComponent<ServerPickupItemSwitcher>() is ServerPickupItemSwitcher spis)
            {
                var thrift = entityThrift.PickupItemSwitcher;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp PickupItemSwitcher {entity.m_Header.m_uEntityID}: no PickupItemSwitcher specific data");
                    return;
                }
                spis.SetCurrentItemPrefabIndexAndSendServerEvent(thrift.Index);
            }

            if (entity.m_GameObject.GetComponent<ServerPlacementItemSwitcher>() is ServerPlacementItemSwitcher spis2)
            {
                var thrift = entityThrift.PickupItemSwitcher;  // This is not a bug. The message is reused.
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp PlacementItemSwitcher {entity.m_Header.m_uEntityID}: no PlacementItemSwitcher specific data");
                    return;
                }
                spis2.SetCurrentItemPrefabIndexAndSendServerEvent(thrift.Index);
            }

            if (entity.m_GameObject.GetComponent<ServerTriggerColourCycle>() is ServerTriggerColourCycle stcc)
            {
                var thrift = entityThrift.TriggerColourCycle;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp TriggerColourCycle {entity.m_Header.m_uEntityID}: no TriggerColourCycle specific data");
                    return;
                }
                stcc.SetCurrentColourIndexAndSendServerEvent(thrift.Index);
            }

            if (entity.m_GameObject.GetComponent<Stack>() != null)
            {
                var thrift = entityThrift.Stack;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp Stack {entity.m_Header.m_uEntityID}: no Stack specific data");
                    return;
                }
                var stackItems = new List<GameObject>();
                foreach (var item in thrift.StackContents)
                {
                    var stackItem = GetEntityByIdOrRef(item);
                    if (stackItem == null)
                    {
                        Log($"[WARP] Failed to warp Stack {entity.m_Header.m_uEntityID}: stack item {item} does not exist");
                        return;
                    }
                    stackItems.Add(stackItem.m_GameObject);
                }
                var serverPlateStack = entity.m_GameObject.GetComponent<ServerPlateStackBase>();
                var clientPlateStack = entity.m_GameObject.GetComponent<ClientPlateStackBase>();
                if (serverPlateStack == null)
                {
                    Log($"[WARP] Failed to warp Stack {entity.m_Header.m_uEntityID}: entity is not a ServerPlateStackBase");
                    return;
                }
                if (clientPlateStack == null)
                {
                    Log($"[WARP] Failed to warp Stack {entity.m_Header.m_uEntityID}: entity is not a ClientPlateStackBase");
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
                    if (thrift.Plates != null)
                    {
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

                {
                    var client = skfcb.GetComponent<ClientKitchenFlowControllerBase>();
                    if (client == null)
                    {
                        Log($"[WARP] Failed to warp KitchenFlowController: no ClientKitchenFlowControllerBase");
                        return;
                    }
                    var thrift = entityThrift.KitchenController;
                    if (thrift == null)
                    {
                        Log($"[WARP] Failed to warp KitchenFlowController: no KitchenFlowController specific data");
                        return;
                    }
                    if (skfcb.RoundTimer is ServerRoundTimer srt)
                    {
                        srt.SetRoundTimer((float)thrift.RoundTime);
                    } else
                    {
                        Log($"[WARP] Failed to set game time: round timer is not a ServerRoundTimer; actual {skfcb.RoundTimer.GetType().Name}");
                    }
                    if (client.RoundTimer is ClientRoundTimer crt)
                    {
                        crt.SetRoundTimer((float)thrift.RoundTime);
                    } else
                    {
                        Log($"[WARP] Failed to set game time: round timer is not a ClientRoundTimer; actual {client.RoundTimer.GetType().Name}");
                    }

                    var score = new TeamMonitor.TeamScoreStats().FromBytes(thrift.TeamScore);
                    var serverTeamMonitor = skfcb.GetMonitorForTeam(TeamID.One);
                    serverTeamMonitor.SetScore(score);
                    var clientTeamMonitor = client.GetMonitorForTeam(TeamID.One);
                    clientTeamMonitor.SetScore(score);
                    client.GetDataStore().Write(new DataStore.Id("score.team"), new TeamScore
                    {
                        m_team = TeamID.One,
                        m_score = score,
                    });

                    var serverOrderController = serverTeamMonitor.OrdersController;
                    serverOrderController.SetNextOrderID((uint)(thrift.NextOrderId + 1));
                    serverOrderController.SetTimerUntilOrder(serverOrderController.GetNextTimeBetweenOrders() - (float)thrift.TimeSinceLastOrder);
                    serverOrderController.SetComboIndex(thrift.LastComboIndex);
                    serverOrderController.SetActiveOrders(thrift.ActiveOrders.Select(o => new OrderController.ServerOrderData().FromBytes(o)).ToList());
                    var roundData = serverOrderController.GetRoundData();
                    var roundInstanceData = serverOrderController.GetRoundInstanceData();
                    if (roundData is WarpableRoundData wrd && roundInstanceData is WarpableRoundInstanceData wrid)
                    {
                        wrd.Warp(wrid, thrift.NextOrderId);
                    } else
                    {
                        Log($"[WARP] Failed to warp KitchenFlowController: round data is not warpable; actual {roundData.GetType().Name} and {roundInstanceData.GetType().Name}");
                    }

                    var clientOrderController = clientTeamMonitor.OrdersController;
                    var gui = clientOrderController.GetGUI();
                    var activeWidgets = gui.GetActiveWidgets();
                    var dyingWidgets = gui.GetDyingWidgets();
                    var occupiedTable = gui.GetOccupiedTables();
                    foreach (var widget in activeWidgets)
                    {
                        GameObject.Destroy(widget.m_widget.gameObject);
                    }
                    foreach (var widget in dyingWidgets)
                    {
                        GameObject.Destroy(widget.m_widget.gameObject);
                    }
                    for (int i = 0; i < occupiedTable.Length; i++)
                    {
                        occupiedTable[i] = false;
                    }
                    activeWidgets.Clear();
                    dyingWidgets.Clear();

                    var clientActiveOrders = new List<ClientOrderControllerBase_ActiveOrderExt>();
                    var j = 0;
                    foreach (var order in thrift.ActiveOrders)
                    {
                        var serverOrderData = new ServerOrderData().FromBytes(order);
                        var widget = GameUtils.InstantiateUIController(gui.GetRecipeWidgetPrefab().gameObject, gui.transform as RectTransform);
                        var recipeWidgetUIController = widget.GetComponent<RecipeWidgetUIController>();
                        recipeWidgetUIController.SetRecipeTree(serverOrderData.RecipeListEntry.m_order.m_orderGuiDescription);
                        recipeWidgetUIController.SetTableNumber(j);
                        occupiedTable[j] = true;
                        recipeWidgetUIController.RefreshSubElements();
                        recipeWidgetUIController.GetTopWidgetTile().SetProgress(serverOrderData.Remaining / serverOrderData.Lifetime);
                        recipeWidgetUIController.GetAnimator().Play("Nothing", 1, 0);  // TODO: check
                        var recipeWidgetData = new RecipeFlowGUI.RecipeWidgetData(recipeWidgetUIController, 0f, gui.GetNextIndex(), serverOrderData.Lifetime, (_) => { });
                        gui.SetNextIndex(gui.GetNextIndex() + 1);
                        activeWidgets.Add(recipeWidgetData);
                        clientActiveOrders.Add(new ClientOrderControllerBase_ActiveOrderExt(serverOrderData.ID, serverOrderData.RecipeListEntry, new RecipeFlowGUI.ElementToken(recipeWidgetData)));
                        j++;
                    }
                    clientOrderController.SetActiveOrders(clientActiveOrders);
                    gui.LayoutWidgets();

                }
            }
        }

        private static void WarpChefAndPositions(EntitySerialisationEntry entity, EntityWarpSpec entityThrift)
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

                    // We also need to set this on the server side. This seems to be the only thing the server keeps track of.
                    // The reason we need this is because the server side performs EndInteraction triggers based on this value.
                    var serverInteractable = entry.m_GameObject.GetComponent<ServerInteractable>();
                    if (serverInteractable == null)
                    {
                        Log($"[WARP] Failed to warp chef's interacting entity: entity with ID {chef.InteractingEntity} does not have ServerInteractable component");
                        return;
                    }
                    cpci.GetComponent<ServerPlayerControlsImpl_Default>().BeginInteraction(serverInteractable);
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
        }

        private static void WarpAfterChefInteractionPropagation(EntitySerialisationEntry entity, EntityWarpSpec entityThrift)
        {
            if (entity.m_GameObject.GetComponent<ServerWorkstation>() is ServerWorkstation sw)
            {
                var thrift = entityThrift.Workstation;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp Workstation: no workstation specific data");
                    return;
                }
                var interacters = sw.Interacters();
                if (interacters.Count != (thrift.Interacters?.Count ?? 0))
                {
                    Log($"[WARP] Error warping Workstation: workstation interacter count {interacters.Count} does not equal expected interacter count {thrift.Interacters.Count}");
                    return;
                }

                if (thrift.Interacters != null)
                {
                    foreach (var desiredInteracter in thrift.Interacters)
                    {
                        var chef = EntitySerialisationRegistry.GetEntry((uint)desiredInteracter.ChefEntityId)?.m_GameObject;
                        if (chef == null)
                        {
                            Log($"[WARP] Failed to warp Workstation: chef entity {desiredInteracter.ChefEntityId} does not exist");
                            return;
                        }
                        bool found = false;
                        foreach (var workstationInteracter in interacters)
                        {
                            if (workstationInteracter.m_object == chef)
                            {
                                found = true;
                                workstationInteracter.m_actionTimer = (float)desiredInteracter.ActionTimer;
                                Log($"[WARP] Warped Workstation: chef entity {desiredInteracter.ChefEntityId} action timer set to {desiredInteracter.ActionTimer}");
                            }
                        }
                        if (!found)
                        {
                            Log($"[WARP] Failed to warp Workstation: chef entity {desiredInteracter.ChefEntityId} is not interacting with workstation");
                        }
                    }
                }
            }
            if (entity.m_GameObject.GetComponent<ServerWashingStation>() is ServerWashingStation sws)
            {
                var client = sws.GetComponent<ClientWashingStation>();
                var washingStation = sws.GetComponent<WashingStation>();
                var thrift = entityThrift.WashingStation;
                if (thrift == null)
                {
                    Log($"[WARP] Failed to warp WashingStation: no washing station specific data");
                    return;
                }
                var serverInteractable = sws.GetComponent<ServerInteractable>();
                var clientInteractable = client.GetComponent<ClientInteractable>();
                var isBeingInteractedWith = serverInteractable.IsBeingInteractedWith();
                sws.SetPlateCount(thrift.PlateCount);
                sws.SetCleaningTimer((float)thrift.Progress);
                serverInteractable.enabled = thrift.PlateCount > 0;

                client.SetPlateCount(thrift.PlateCount);
                client.SetCleaningTimer((float)thrift.Progress);
                client.SetIsWashing(isBeingInteractedWith);
                var progressUI = client.m_progressUI();
                progressUI.SetVisibility(thrift.PlateCount > 0);
                clientInteractable.enabled = thrift.PlateCount > 0;
                progressUI.SetProgress(Mathf.Clamp01((float)thrift.Progress / washingStation.m_cleanPlateTime));
                client.UpdateCosmetics();
            }
        }
    }

}
