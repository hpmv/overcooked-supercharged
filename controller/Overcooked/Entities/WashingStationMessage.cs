using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// THIS CLASS IS MODIFIED FROM THE ORIGINAL.
public class WashingStationMessage : Serialisable {
    // Token: 0x06001FC9 RID: 8137
    public void Serialise(BitStreamWriter writer) {
        writer.Write((uint)this.m_msgType, 2);
        if (this.m_msgType == WashingStationMessage.MessageType.InteractionState) {
            writer.Write(this.m_interacting);
            writer.Write(this.m_progress);
            return;
        }
        writer.Write((uint)this.m_plateCount, 4);
    }

    // Token: 0x06001FCA RID: 8138
    public bool Deserialise(BitStreamReader reader) {
        this.m_msgType = (WashingStationMessage.MessageType)reader.ReadUInt32(2);
        if (this.m_msgType == WashingStationMessage.MessageType.InteractionState) {
            this.m_interacting = reader.ReadBit();
            this.m_progress = reader.ReadFloat32();
        } else {
            this.m_plateCount = (int)reader.ReadUInt32(4);
        }
        return true;
    }

    // Token: 0x040019C3 RID: 6595
    private const int c_msgTypeBits = 2;

    // Token: 0x040019C4 RID: 6596
    private const int c_plateCountBits = 4;

    // Token: 0x040019C5 RID: 6597
    public WashingStationMessage.MessageType m_msgType;

    // Token: 0x040019C6 RID: 6598
    public bool m_interacting;

    // Token: 0x040019C7 RID: 6599
    public float m_progress;

    // Token: 0x040019C8 RID: 6600
    public int m_plateCount;

    // Token: 0x02000697 RID: 1687
    public enum MessageType {
        // Token: 0x040019CA RID: 6602
        InteractionState,
        // Token: 0x040019CB RID: 6603
        AddPlates,
        // Token: 0x040019CC RID: 6604
        CleanedPlate
    }
}
