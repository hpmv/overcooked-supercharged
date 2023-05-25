using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

public class EntityAuxMessage : Serialisable
{
    public bool Deserialise(BitStreamReader reader)
    {
        if (!this.m_entityHeader.Deserialise(reader)) {
            return false;
        }
        m_auxEntityType = (AuxEntityType)reader.ReadByte(8);
        if (!SerialisationRegistry<AuxEntityType>.Deserialise(out this.m_payload, m_auxEntityType, reader)) {
            Console.WriteLine(
                $"Unable to deserialize EntityAuxMessage for entity {this.m_entityHeader.m_uEntityID} entity type {m_auxEntityType}: deserialization failed");
            return false;
        }
        return true;
    }

    public void Serialise(BitStreamWriter writer)
    {
        writer.Write((uint)m_auxEntityType, 8);
        m_entityHeader.Serialise(writer);
        m_payload.Serialise(writer);
    }

    public EntityMessageHeader m_entityHeader = new EntityMessageHeader();
    public AuxEntityType m_auxEntityType;
    public Serialisable m_payload;
}

public enum AuxEntityType: byte
{
    ThrowableItemAux,
}
