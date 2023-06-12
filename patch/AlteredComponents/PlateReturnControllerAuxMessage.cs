using BitStream;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch.AlteredComponents
{
    public class PlateReturnControllerAuxMessage : AuxMessageBase
    {
        public override AuxEntityType GetAuxEntityType()
        {
            return AuxEntityType.PlateReturnControllerAux;
        }

        public override void Serialise(BitStreamWriter writer)
        {
            writer.Write((byte)type, 8);
            if (type == PlateReturnControllerAuxMessageType.PlateDelivered)
            {
                m_plateReturnStation.Serialise(writer);
                writer.Write(m_timeToReturn);
            } else
            {
                writer.Write((uint)indicesToRemove.Count, 32);
                foreach (var index in indicesToRemove)
                {
                    writer.Write((uint)index, 32);
                }
            }
        }

        public void InitializePlateDelivered(uint plateReturnStation, float timeToReturn)
        {
            type = PlateReturnControllerAuxMessageType.PlateDelivered;
            m_plateReturnStation.m_uEntityID = plateReturnStation;
            m_timeToReturn = timeToReturn;
        }

        public void InitializeRemovePlates(List<int> indicesToRemove)
        {
            type = PlateReturnControllerAuxMessageType.RemovePlates;
            this.indicesToRemove = indicesToRemove;
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
}
