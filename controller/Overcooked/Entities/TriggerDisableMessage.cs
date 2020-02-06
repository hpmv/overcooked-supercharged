using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000179 RID: 377
public class TriggerDisableMessage : Serialisable
{
	// Token: 0x0600069B RID: 1691 RVA: 0x0002D2FB File Offset: 0x0002B6FB
	public void Initialise(bool _enabled)
	{
		this.m_enabled = _enabled;
	}

	// Token: 0x0600069C RID: 1692 RVA: 0x0002D304 File Offset: 0x0002B704
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_enabled);
	}

	// Token: 0x0600069D RID: 1693 RVA: 0x0002D312 File Offset: 0x0002B712
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_enabled = reader.ReadBit();
		return true;
	}

	// Token: 0x0400056F RID: 1391
	public bool m_enabled;
}
