using BitStream;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.AlteredComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    [HarmonyPatch(typeof(SerialisationRegistry<EntityType>), "RegisterMessageType")]
    public static class PatchSerialisationRegistry
    {
        public static void Prefix(EntityType type, ref Serialisable message)
        {
            switch (type)
            {
                case EntityType.Cannon:
                    message = new CannonModMessage();
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(EntitySerialisationRegistry), "AddSynchronisedType", new[] {typeof(Type), typeof(SynchroniserConfig)} )]
    public static class PatchEntitySerialisationRegistry
    {
        public static void Prefix(Type gameType, ref SynchroniserConfig config)
        {
            if (gameType == typeof(Cannon))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonMod), typeof(ClientCannonMod));
            } else if (gameType == typeof(CannonCosmeticDecisions))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonCosmeticDecisionsMod), typeof(ClientCannonCosmeticDecisionsMod));
            } else if (gameType == typeof(CannonSessionInteractable))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonSessionInteractableMod), typeof(ClientCannonSessionInteractableMod));
            } else if (gameType == typeof(CannonPlayerHandler))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonPlayerHandlerMod), typeof(ClientCannonPlayerHandlerMod));
            }
        }
    }

    public static class AuxMessageSender
    {
        public static void SendAuxMessage(this ServerSynchroniserBase self, AuxMessageBase message)
        {
            var entityEventMessage = new EntityAuxMessage()
            {
                m_entityHeader = new EntityMessageHeader
                {
                    m_uEntityID = self.GetEntityId()
                },
                m_auxEntityType = message.GetAuxEntityType(),
                m_payload = message,
            };
            var serialized = new FastList<byte>();
            entityEventMessage.Serialise(new BitStreamWriter(serialized));
            var data = Injector.Server.CurrentFrameData;
            if (data.ServerMessages == null)
            {
                data.ServerMessages = new List<ServerMessage>();
            }
            data.ServerMessages.Add(new ServerMessage
            {
                Type = (int)MessageType.COUNT + 1,
                Message = serialized.ToArray(),
            });
        }
    }

    public abstract class AuxMessageBase : Serialisable
    {
        public bool Deserialise(BitStreamReader reader)
        {
            throw new NotImplementedException();
        }

        public abstract AuxEntityType GetAuxEntityType();
        public abstract void Serialise(BitStreamWriter writer);
    }
}
