using BitStream;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    public class EntityAuxMessage : Serialisable
    {
        public bool Deserialise(BitStreamReader reader)
        {
            throw new System.NotImplementedException();
        }

        public void Serialise(BitStreamWriter writer)
        {
            m_entityHeader.Serialise(writer);
            writer.Write((uint)m_auxEntityType, 8);
            m_payload.Serialise(writer);
        }

        public EntityMessageHeader m_entityHeader;
        public AuxEntityType m_auxEntityType;
        public Serialisable m_payload;
    }
    
    public enum AuxEntityType: byte
    {
        ThrowableItemAux,
    }
}
