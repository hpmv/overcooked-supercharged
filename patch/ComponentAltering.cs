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
    // Native cannon synchronizer registrations and the native CannonMessage codec
    // execute unchanged. Authoring observations use a separate auxiliary channel.

    public static class AuxMessageSender
    {
        public static void SendAuxMessage(this ServerSynchroniserBase self, AuxMessageBase message)
        {
            SendAuxMessage(self.GetEntityId(), message);
        }

        public static void SendAuxMessage(uint entityId, AuxMessageBase message)
        {
            var entityEventMessage = new EntityAuxMessage()
            {
                m_entityHeader = new EntityMessageHeader
                {
                    m_uEntityID = entityId
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

    public static class EntityRetirementMessageSender
    {
        public static void SendMessageToRetireEntity(this ServerSynchroniserBase self)
        {
            SendMessageToRetireEntity(self.GetEntityId());
        }
        public static void SendMessageToRetireEntity(uint entityId)
        {
            var entityRetirementMessage = new EntityRetirementMessage()
            {
                m_entityHeader = new EntityMessageHeader
                {
                    m_uEntityID = entityId
                },
            };
            var serialized = new FastList<byte>();
            entityRetirementMessage.Serialise(new BitStreamWriter(serialized));
            var data = Injector.Server.CurrentFrameData;
            if (data.ServerMessages == null)
            {
                data.ServerMessages = new List<ServerMessage>();
            }
            data.ServerMessages.Add(new ServerMessage
            {
                Type = (int)MessageType.COUNT + 2,
                Message = serialized.ToArray(),
            });
        }
    }
}
