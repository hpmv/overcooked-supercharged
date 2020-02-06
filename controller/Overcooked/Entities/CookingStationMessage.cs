using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000456 RID: 1110
public class CookingStationMessage : Serialisable
{
	// Token: 0x060014B0 RID: 5296 RVA: 0x0007139D File Offset: 0x0006F79D
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_isTurnedOn);
		writer.Write(this.m_isCooking);
	}

	// Token: 0x060014B1 RID: 5297 RVA: 0x000713B7 File Offset: 0x0006F7B7
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_isTurnedOn = reader.ReadBit();
		this.m_isCooking = reader.ReadBit();
		return true;
	}

	// Token: 0x04000FDB RID: 4059
	public bool m_isTurnedOn;

	// Token: 0x04000FDC RID: 4060
	public bool m_isCooking;
}
