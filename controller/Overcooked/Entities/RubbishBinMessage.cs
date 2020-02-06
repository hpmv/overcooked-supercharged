using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000558 RID: 1368
public class RubbishBinMessage : Serialisable
{
	// Token: 0x060019D1 RID: 6609 RVA: 0x000817BE File Offset: 0x0007FBBE
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.BinnedItemEntityID, 10);
		writer.Write(this.m_alive);
	}

	// Token: 0x060019D2 RID: 6610 RVA: 0x000817DA File Offset: 0x0007FBDA
	public bool Deserialise(BitStreamReader reader)
	{
		this.BinnedItemEntityID = reader.ReadUInt32(10);
		this.m_alive = reader.ReadBit();
		return true;
	}

	// Token: 0x04001463 RID: 5219
	public uint BinnedItemEntityID;

	// Token: 0x04001464 RID: 5220
	public bool m_alive = true;
}
