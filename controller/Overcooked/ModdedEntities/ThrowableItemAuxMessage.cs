using BitStream;
using System;
using Team17.Online.Multiplayer.Messaging;

class ThrowableItemAuxMessage : Serialisable
{
    public bool Deserialise(BitStreamReader reader)
    {
        var length = reader.ReadUInt32(8);
        m_colliders = new EncodedCollider[length];
        for (var i = 0; i < length; i++)
        {
            m_colliders[i].entity = new EntityMessageHeader();
            m_colliders[i].entity.Deserialise(reader);
            m_colliders[i].colliderIndex = (int)reader.ReadUInt32(8);
        }
        return true;
    }

    public void Serialise(BitStreamWriter writer)
    {
        throw new NotImplementedException();
    }

    public EncodedCollider[] m_colliders;

    public struct EncodedCollider
    {
        public EntityMessageHeader entity;
        public int colliderIndex;
    }
}
