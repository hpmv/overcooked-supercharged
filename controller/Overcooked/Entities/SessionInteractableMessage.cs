using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x0200059C RID: 1436
public class SessionInteractableMessage : Serialisable
{
	// Token: 0x06001B60 RID: 7008 RVA: 0x000871D4 File Offset: 0x000855D4
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_msgType, 2);
		if (this.m_msgType == SessionInteractableMessage.MessageType.InteractionState)
		{
			writer.Write(this.m_interacterID, 10);
		}
	}

	// Token: 0x06001B61 RID: 7009 RVA: 0x000871FC File Offset: 0x000855FC
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_msgType = (SessionInteractableMessage.MessageType)reader.ReadUInt32(2);
		if (this.m_msgType == SessionInteractableMessage.MessageType.InteractionState)
		{
			this.m_interacterID = reader.ReadUInt32(10);
		}
		return true;
	}

	// Token: 0x0400155A RID: 5466
	private const int c_msgTypeBits = 2;

	// Token: 0x0400155B RID: 5467
	public SessionInteractableMessage.MessageType m_msgType;

	// Token: 0x0400155C RID: 5468
	public uint m_interacterID;

	// Token: 0x0200059D RID: 1437
	public enum MessageType
	{
		// Token: 0x0400155E RID: 5470
		InteractionState
	}
}
