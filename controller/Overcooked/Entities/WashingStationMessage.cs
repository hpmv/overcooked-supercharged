using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000621 RID: 1569
public class WashingStationMessage : Serialisable
{
	// Token: 0x06001DD2 RID: 7634 RVA: 0x00090824 File Offset: 0x0008EC24
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_msgType, 2);
		if (this.m_msgType == WashingStationMessage.MessageType.InteractionState)
		{
			writer.Write(this.m_interacting);
			writer.Write(this.m_progress);
		}
		else
		{
			writer.Write((uint)this.m_plateCount, 4);
		}
	}

	// Token: 0x06001DD3 RID: 7635 RVA: 0x00090874 File Offset: 0x0008EC74
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_msgType = (WashingStationMessage.MessageType)reader.ReadUInt32(2);
		if (this.m_msgType == WashingStationMessage.MessageType.InteractionState)
		{
			this.m_interacting = reader.ReadBit();
			this.m_progress = reader.ReadFloat32();
		}
		else
		{
			this.m_plateCount = (int)reader.ReadUInt32(4);
		}
		return true;
	}

	// Token: 0x040016D5 RID: 5845
	private const int c_msgTypeBits = 2;

	// Token: 0x040016D6 RID: 5846
	private const int c_plateCountBits = 4;

	// Token: 0x040016D7 RID: 5847
	public WashingStationMessage.MessageType m_msgType;

	// Token: 0x040016D8 RID: 5848
	public bool m_interacting;

	// Token: 0x040016D9 RID: 5849
	public float m_progress;

	// Token: 0x040016DA RID: 5850
	public int m_plateCount;

	// Token: 0x02000622 RID: 1570
	public enum MessageType
	{
		// Token: 0x040016DC RID: 5852
		InteractionState,
		// Token: 0x040016DD RID: 5853
		AddPlates,
		// Token: 0x040016DE RID: 5854
		CleanedPlate
	}
}
