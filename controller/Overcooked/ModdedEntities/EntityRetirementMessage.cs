using BitStream;
using Team17.Online.Multiplayer.Messaging;

public class EntityRetirementMessage : Serialisable
{
    public bool Deserialise(BitStreamReader reader)
    {
        return this.m_entityHeader.Deserialise(reader);
    }

    public void Serialise(BitStreamWriter writer)
    {
        m_entityHeader.Serialise(writer);
    }

    public EntityMessageHeader m_entityHeader = new EntityMessageHeader();
}

