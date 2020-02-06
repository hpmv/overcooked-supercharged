using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000517 RID: 1303
public class MixingStationMessage : Serialisable
{
	// Token: 0x0600186A RID: 6250 RVA: 0x0007C0F5 File Offset: 0x0007A4F5
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_isTurnedOn);
	}

	// Token: 0x0600186B RID: 6251 RVA: 0x0007C103 File Offset: 0x0007A503
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_isTurnedOn = reader.ReadBit();
		return true;
	}

	// Token: 0x04001378 RID: 4984
	public bool m_isTurnedOn;
}
