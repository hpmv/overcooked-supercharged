using BitStream;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    public class EntityRetirementMessage : Serialisable
    {
        public bool Deserialise(BitStreamReader reader)
        {
            return m_entityHeader.Deserialise(reader);
        }

        public void Serialise(BitStreamWriter writer)
        {
            m_entityHeader.Serialise(writer);
        }

        public EntityMessageHeader m_entityHeader;
    }
}
