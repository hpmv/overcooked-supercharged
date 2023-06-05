using BepInEx.Harmony;
using BitStream;
using HarmonyLib;
using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using Team17.Online.Multiplayer.Connection;
using UnityEngine;
using SuperchargedPatch.Extensions;

namespace SuperchargedPatch
{
    public static class ServerInterceptionPatches
    {

        public static void LateUpdate()
        {
            // Make sure to flush any network messages before capturing state. Normally this
            // is done as part of MultiplayerController.LateUpdate(), but we're also executing in
            // LateUpdate so the order is not deterministic.
            GameObject.FindObjectOfType<MultiplayerController>()?.FlushAllPendingBatchedMessages();

            if (Helpers.IsPaused())
            {
                WarpHandler.HandleWarpRequestIfAny();
            }
            var data = Injector.Server.CurrentFrameData;
            if (!Helpers.IsPaused())
            {
                if (Injector.Server.CurrentInput.__isset.nextFrame)
                {
                    ActiveStateCollector.NotifyFrame(Injector.Server.CurrentInput.NextFrame);
                }
            }
            ActiveStateCollector.CollectDataForFrame(data);
            data.LastFramePaused = Helpers.IsPaused();
            if (Injector.Server.CurrentInput.RequestPause)
            {
                Helpers.Pause();
            } else if (Injector.Server.CurrentInput.RequestResume)
            {
                Helpers.Resume();
            }

            data.NextFramePaused = TimeManager.IsPaused(TimeManager.PauseLayer.Main);
            Injector.Server.CommitFrame();
        }

        [HarmonyPatch(typeof(PlayerInputLookup), "GetButton")]
        public static class PatchPlayerInputLookupGetButton
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerInputLookup.LogicalButtonID _id, PlayerInputLookup.Player _player, ref ILogicalButton __result)
            {
                int? foundEntityId = null;
                EntitySerialisationRegistry.m_EntitiesList.ForEach(entry =>
                {
                    if (entry.m_GameObject.GetComponent<PlayerIDProvider>() is PlayerIDProvider pip)
                    {
                        if (pip.GetID() == _player)
                        {
                            foundEntityId = (int)entry.m_Header.m_uEntityID;
                        }
                    }
                });
                if (foundEntityId == null)
                {
                    Debug.Log($"Unable to find entity ID for player input ID {_player}");
                    return;
                }

                TASLogicalButtonType buttonType;
                switch (_id)
                {
                    case PlayerInputLookup.LogicalButtonID.PickupAndDrop:
                        buttonType = TASLogicalButtonType.Pickup;
                        break;
                    case PlayerInputLookup.LogicalButtonID.WorkstationInteract:
                        buttonType = TASLogicalButtonType.Use;
                        break;
                    case PlayerInputLookup.LogicalButtonID.Dash:
                        buttonType = TASLogicalButtonType.Dash;
                        break;
                    default:
                        return;
                }
                Debug.Log($"Overriding button {_id} for player {_player} with entity ID {foundEntityId.Value}");
                __result = new TASLogicalButton(foundEntityId.Value, buttonType, __result);
            }
        }

        [HarmonyPatch(typeof(PlayerInputLookup), "GetValue")]
        public static class PatchPlayerInputLookupGetValue
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerInputLookup.LogicalValueID _id, PlayerInputLookup.Player _player, ref ILogicalValue __result)
            {
                int? foundEntityId = null;
                EntitySerialisationRegistry.m_EntitiesList.ForEach(entry =>
                {
                    if (entry.m_GameObject.GetComponent<PlayerIDProvider>() is PlayerIDProvider pip)
                    {
                        if (pip.GetID() == _player)
                        {
                            foundEntityId = (int)entry.m_Header.m_uEntityID;
                        }
                    }
                });
                if (foundEntityId == null)
                {
                    Debug.Log($"Unable to find entity ID for player input ID {_player}");
                    return;
                }

                TASLogicalValueType valueType;
                switch (_id)
                {
                    case PlayerInputLookup.LogicalValueID.MovementX:
                        valueType = TASLogicalValueType.MovementX;
                        break;
                    case PlayerInputLookup.LogicalValueID.MovementY:
                        valueType = TASLogicalValueType.MovementY;
                        break;
                    default:
                        return;
                }
                Debug.Log($"Overriding value {_id} for player {_player} with entity ID {foundEntityId.Value}");
                __result = new TASLogicalValue(foundEntityId.Value, valueType, __result);
                return;
            }
        }

        [HarmonyPatch(typeof(PlayerControls), "CanButtonBePressed")]
        public static class PatchPlayerControlsCanButtonBePressed
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerControls __instance, ref bool __result)
            {
                // The original method queries a lot of stuff that isn't useful for TAS.
                // GetDirectlyUnderPlayerControl() is the only thing that is related to
                // game logic.
                __result = __instance.GetDirectlyUnderPlayerControl();
                return false;
            }
        }

        [HarmonyPatch(typeof(ClientInputTransmitter), "GetGated", new Type[] { typeof(ILogicalButton) } )]
        public static class PatchClientInputTransmitterGetGatedButton
        {
            [HarmonyPrefix]
            public static bool Prefix(ILogicalButton _toProtect, ref ILogicalButton __result)
            {
                // TODO: revisit this. We're skipping CanButtonBePressed here.
                __result = _toProtect;
                return false;
            }
        }

        [HarmonyPatch(typeof(ClientInputTransmitter), "GetGated", new Type[] { typeof(ILogicalValue) })]
        public static class PatchClientInputTransmitterGetGatedValue
        {
            [HarmonyPrefix]
            public static bool Prefix(ILogicalValue _toProtect, ref ILogicalValue __result)
            {
                // TODO: revisit this. We're skipping CanButtonBePressed here.
                __result = _toProtect;
                return false;
            }
        }

        private static FieldInfo m_m_spawnables = typeof(SpawnableEntityCollection).GetField("m_spawnables", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPatch(typeof(EntitySerialisationRegistry), "StartSynchronisingEntry")]
        public static class EntitySerialisationRegistryStartSynchronisingEntryPatch
        {
            [HarmonyPrefix]
            public static void Prefix(EntitySerialisationEntry entry)
            {
                try
                {
                    var currentFrameData = Injector.Server.CurrentFrameData;
                    GameObject mGameObject = entry.m_GameObject;
                    if (currentFrameData.EntityRegistry == null)
                    {
                        currentFrameData.EntityRegistry = new List<EntityRegistryData>();
                    }
                    EntityRegistryData entityRegistryDatum = new EntityRegistryData()
                    {
                        EntityId = (int)EntitySerialisationRegistry.GetId(mGameObject),
                        Name = mGameObject.name,
                        Pos = mGameObject.transform.position.ToThrift(),  // TODO: needed?
                        SyncEntityTypes = new List<int>(),
                        Components = (
                            from c in mGameObject.GetComponents<Component>()
                            select c.GetType().Name).ToList<string>()
                    };
                    if (mGameObject.GetComponent<SpawnableEntityCollection>() != null)
                    {
                        SpawnableEntityCollection component = mGameObject.GetComponent<SpawnableEntityCollection>();
                        entityRegistryDatum.SpawnNames = (
                            from s in (List<GameObject>)m_m_spawnables.GetValue(component)
                            select s.name).ToList<string>();
                    }
                    for (int i = 0; i < entry.m_ServerSynchronisedComponents.Count; i++)
                    {
                        entityRegistryDatum.SyncEntityTypes.Add((int)entry.m_ServerSynchronisedComponents._items[i].GetEntityType());
                    }
                    currentFrameData.EntityRegistry.Add(entityRegistryDatum);
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            }
        }

        private static Type t_RoundInstanceData = typeof(RoundData).GetNestedType("RoundInstanceData", BindingFlags.NonPublic);
        private static FieldInfo m_RecipeCount = t_RoundInstanceData.GetField("RecipeCount", BindingFlags.Instance | BindingFlags.Public);
        private static FieldInfo m_CumulativeFrequencies = t_RoundInstanceData.GetField("CumulativeFrequencies", BindingFlags.Instance | BindingFlags.Public);
        private static MethodInfo f_GetWeight = typeof(RoundData).GetMethod("GetWeight", BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPatch(typeof(RoundData), "GetNextRecipe")]
        public static class GetNextRecipePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref RecipeList.Entry[] __result, RoundInstanceDataBase _data, RecipeList ___m_recipes, RoundData __instance)
            {
                m_RecipeCount.SetValue(_data, (int)m_RecipeCount.GetValue(_data) + 1);
                KeyValuePair<int, RecipeList.Entry> weightedRandomElement =
                    OrderRandom.GetWeightedRandomElement(___m_recipes.m_recipes, (int i, RecipeList.Entry e) => (float)f_GetWeight.Invoke(__instance, new object[] { _data, i }));
                ((int[])m_CumulativeFrequencies.GetValue(_data))[weightedRandomElement.Key]++;
                __result = new RecipeList.Entry[] { weightedRandomElement.Value };
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkstationMessage), "Serialise")]
        public static class WorkstationMessageSerialisePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(BitStreamWriter writer, WorkstationMessage __instance)
            {
                // This patch is needed because WorkstationMessage is not supposed to be reserialized on the client side.
                // Not all messages do this... this is perhaps an exception.
                writer.Write(__instance.m_interacting);
                if (__instance.m_interactor != null)
                {
                    __instance.m_interactor.m_Header.Serialise(writer);
                }
                else
                {
                    __instance.m_interactorHeader.Serialise(writer);
                }
                if (__instance.m_interacting)
                {
                    if (__instance.m_item != null)
                    {
                        __instance.m_item.m_Header.Serialise(writer);
                    }
                    else
                    {
                        __instance.m_itemHeader.Serialise(writer);
                    }
                }
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Server), "SendMessageToClient", new Type[]{typeof(NetworkConnection), typeof(MessageType), typeof(Serialisable), typeof(bool)})]
    public static class PatchSendMessageToClient {
        public static void Prefix(NetworkConnection connection, MessageType type, Serialisable message, bool bReliable) {
            try
            {
                var currentFrameData = Injector.Server.CurrentFrameData;
                if (currentFrameData.ServerMessages == null)
                {
                    currentFrameData.ServerMessages = new List<ServerMessage>();
                }
                FastList<byte> fastList = new FastList<byte>();
                if (message == null)
                {
                    Console.WriteLine("Message of type " + type + " is null!");
                    return;
                }
                message.Serialise(new BitStreamWriter(fastList));
                currentFrameData.ServerMessages.Add(new ServerMessage()
                {
                    Type = (int)type,
                    Message = fastList.ToArray()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
