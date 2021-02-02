using System;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    class Deserializer {
        static Deserializer() {
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.WorldObject, () => new WorldObjectMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.WorldPopup, () => new WorldMapInfoPopupMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PhysicsObject, () => new PhysicsObjectMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Chef, () => new ChefPositionMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.SprayingUtensil, () => new SprayingUtensilMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.RespawnBehaviour, () => new RespawnMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Flammable, () => new FlammableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Workstation, () => new WorkstationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Workable, () => new WorkableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PlateStation, () => new PlateStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PlateStack, () => new PlateStackMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.WashingStation, () => new WashingStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PhysicalAttach, () => new PhysicalAttachMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.IngredientContainer, () => new IngredientContainerMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.CookingState, () => new CookingStateMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.MixingState, () => new MixingStateMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.AttachStation, () => new AttachStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.CookingStation, () => new CookingStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.ConveyorStation, () => new ConveyorStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.ConveyorAnimator, () => new ConveyorAnimationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TimedQueue, () => new TimedQueueMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerZone, () => new TriggerZoneMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerDisable, () => new TriggerDisableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerOnAnimator, () => new TriggerOnAnimatorMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerToggleOnAnimator, () => new TriggerToggleOnAnimatorMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerMoveSpawn, () => new MoveSpawnMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerDialogue, () => new TriggerDialogueMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.AnimatorVariable, () => new TriggerAnimatorVariableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.ChefCarry, () => new ChefCarryMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.InputEvent, () => new InputEventMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.FlowController, () => new KitchenFlowMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.ThrowableItem, () => new ThrowableItemMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Teleportal, () => new TeleportalMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Cutscene, () => new CutsceneStateMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Dialogue, () => new DialogueStateMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TutorialPopup, () => new TutorialDismissMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.AttachCatcher, () => new AttachmentCatcherMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.SessionInteractable, () => new SessionInteractableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.WorldMapVanControls, () => new MapAvatarControlsMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.WorldMapVanAvatar, () => new AvatarPositionMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.SwitchMapNode, () => new SwitchMapNodeMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.LevelPortalMapNode, () => new LevelPortalMapNodeMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.RubbishBin, () => new RubbishBinMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.MixingStation, () => new MixingStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Washable, () => new WashableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.HeatedStation, () => new HeatedStationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.RespawnCollider, () => new RespawnColliderMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.AutoWorkstation, () => new AutoWorkstationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PlacementItemSpawner, () => new PlacementItemSpawnerMessage());
            // SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.HordeFlowController, default(HordeFlowMessage));
            // SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.HordeTarget, default(HordeTargetMessage));
            // SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.HordeEnemy, default(HordeEnemyMessage));
            // SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.HordeLockable, new HordeLockableMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PickupItemSwitcher, () => new PickupItemSwitcherMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.Cannon, () => new CannonMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.PilotRotation, () => new PilotRotationMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.TriggerColourCycle, () => new TriggerColourCycleMessage());
            SerialisationRegistry<EntityType>.RegisterMessageType(EntityType.MultiTriggerDisable, () => new TriggerDisableMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.EntitySynchronisation, () => new EntitySynchronisationMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.EntityEvent, () => new EntityEventMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.SpawnEntity, () => new SpawnEntityMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.SpawnPhysicalAttachment, () => new SpawnPhysicalAttachmentMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.DestroyEntity, () => new DestroyEntityMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.DestroyEntities, () => new DestroyEntitiesMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.LevelLoadByIndex, () => new LevelLoadByIndexMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.GameState, () => new GameStateMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.TimeSync, () => new TimeSyncMessage());
            SerialisationRegistry<MessageType>.RegisterMessageType(MessageType.LatencyMeasure, () => new LatencyMessage());
        }

        public static Serialisable Deserialize(int type, byte[] bytes) {
            var messageType = (MessageType)type;
            var bitstream = new BitStream.BitStreamReader(bytes);
            Serialisable result;
            if (!SerialisationRegistry<MessageType>.Deserialise(out result, messageType, bitstream)) {
                //Console.WriteLine($"Failed to deserialize {messageType}");
                return null;
            }
            //Console.WriteLine($"Succeeded to deserialize {messageType}");
            return result;
        }
    }
}