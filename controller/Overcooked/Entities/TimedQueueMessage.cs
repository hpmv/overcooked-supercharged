using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000156 RID: 342
public class TimedQueueMessage : Serialisable
{
	// Token: 0x0600061E RID: 1566 RVA: 0x0002BEED File Offset: 0x0002A2ED
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_msgType, 1);
		if (this.m_msgType == TimedQueueMessage.MsgType.QueueEvent)
		{
			writer.Write((uint)this.m_index, 4);
			writer.Write(this.m_time);
		}
	}

	// Token: 0x0600061F RID: 1567 RVA: 0x0002BF20 File Offset: 0x0002A320
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_msgType = (TimedQueueMessage.MsgType)reader.ReadUInt32(1);
		if (this.m_msgType == TimedQueueMessage.MsgType.QueueEvent)
		{
			this.m_index = (int)reader.ReadUInt32(4);
			this.m_time = reader.ReadFloat32();
		}
		return true;
	}

	// Token: 0x04000502 RID: 1282
	public const int kBitsPerMsgType = 1;

	// Token: 0x04000503 RID: 1283
	public const int kBitsPerIndex = 4;

	// Token: 0x04000504 RID: 1284
	public TimedQueueMessage.MsgType m_msgType;

	// Token: 0x04000505 RID: 1285
	public int m_index;

	// Token: 0x04000506 RID: 1286
	public float m_time;

	// Token: 0x02000157 RID: 343
	public enum MsgType
	{
		// Token: 0x04000508 RID: 1288
		QueueEvent,
		// Token: 0x04000509 RID: 1289
		Cancel
	}
}
