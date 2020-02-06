using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000192 RID: 402
public class TriggerToggleOnAnimatorMessage : Serialisable
{
	// Token: 0x060006E6 RID: 1766 RVA: 0x0002DD6F File Offset: 0x0002C16F
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_value);
	}

	// Token: 0x060006E7 RID: 1767 RVA: 0x0002DD7D File Offset: 0x0002C17D
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_value = reader.ReadBit();
		return true;
	}

	// Token: 0x040005AE RID: 1454
	public bool m_value;
}
