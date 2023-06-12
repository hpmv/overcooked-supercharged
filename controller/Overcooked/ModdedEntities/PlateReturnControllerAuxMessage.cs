using BitStream;
using System;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

public class PlateReturnControllerAuxMessage : Serialisable
{
    public void Serialise(BitStreamWriter writer)
    {
        throw new NotImplementedException();
    }

    public bool Deserialise(BitStreamReader reader)
    {
        type = (PlateReturnControllerAuxMessageType)reader.ReadUInt32(8);
        if (type == PlateReturnControllerAuxMessageType.PlateDelivered)
        {
            m_plateReturnStation.Deserialise(reader);
            m_timeToReturn = reader.ReadFloat32();
        } else
        {
            var length = reader.ReadUInt32(32);
            indicesToRemove = new List<int>();
            for (var i = 0; i < length; i++)
            {
                indicesToRemove.Add((int)reader.ReadUInt32(32));
            }
        }
        return true;
    }

    public PlateReturnControllerAuxMessageType type;
    public EntityMessageHeader m_plateReturnStation = new EntityMessageHeader();
    public float m_timeToReturn;
    public List<int> indicesToRemove;
}

public enum PlateReturnControllerAuxMessageType : byte
{
    PlateDelivered,
    RemovePlates,
}
