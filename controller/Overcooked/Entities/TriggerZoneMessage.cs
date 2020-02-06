using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000196 RID: 406
public class TriggerZoneMessage : Serialisable
{
	// Token: 0x060006F5 RID: 1781 RVA: 0x0002DFAF File Offset: 0x0002C3AF
	public void Initialise(bool _occupied)
	{
		this.m_occupied = _occupied;
	}

	// Token: 0x060006F6 RID: 1782 RVA: 0x0002DFB8 File Offset: 0x0002C3B8
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_occupied);
	}

	// Token: 0x060006F7 RID: 1783 RVA: 0x0002DFC6 File Offset: 0x0002C3C6
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_occupied = reader.ReadBit();
		return true;
	}

	// Token: 0x040005B8 RID: 1464
	public bool m_occupied;
}
