using HarmonyLib;
using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using Team17.Online.Multiplayer.Connection;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using SuperchargedPatch.Extensions;
using BitStream;

namespace SuperchargedPatch.AlteredComponents
{
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
                        select c.GetType().Name).ToList()
                };
                if (mGameObject.GetComponent<SpawnableEntityCollection>() != null)
                {
                    SpawnableEntityCollection component = mGameObject.GetComponent<SpawnableEntityCollection>();
                    entityRegistryDatum.SpawnNames = (
                        from s in component.GetSpawnables()
                        select s.name).ToList();
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

    [HarmonyPatch(typeof(Server), "SendMessageToClient", new Type[] { typeof(NetworkConnection), typeof(MessageType), typeof(Serialisable), typeof(bool) })]
    public static class PatchSendMessageToClient
    {
        public static void Prefix(NetworkConnection connection, MessageType type, Serialisable message, bool bReliable)
        {
            if (type == MessageType.EntitySynchronisation)
            {
                // Don't need these. We synchronize these ourselves.
                return;
            }
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
