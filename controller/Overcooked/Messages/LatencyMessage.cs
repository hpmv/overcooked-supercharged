using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008B9 RID: 2233
public class LatencyMessage : Serialisable
{
	// Token: 0x06002B7E RID: 11134 RVA: 0x000CB1EF File Offset: 0x000C95EF
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_Stage, 1);
		writer.Write(this.m_bReliable);
		writer.Write(this.m_fTime);
	}

	// Token: 0x06002B7F RID: 11135 RVA: 0x000CB216 File Offset: 0x000C9616
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_Stage = (LatencyMessage.Stage)reader.ReadUInt32(1);
		this.m_bReliable = reader.ReadBit();
		this.m_fTime = reader.ReadFloat32();
		return true;
	}

	// Token: 0x04002293 RID: 8851
	public LatencyMessage.Stage m_Stage;

	// Token: 0x04002294 RID: 8852
	public bool m_bReliable;

	// Token: 0x04002295 RID: 8853
	public float m_fTime;

	// Token: 0x020008BA RID: 2234
	public enum Stage
	{
		// Token: 0x04002297 RID: 8855
		Ping,
		// Token: 0x04002298 RID: 8856
		Pong
	}
}
