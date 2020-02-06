using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x0200016F RID: 367
public class TriggerColourCycleMessage : Serialisable
{
	// Token: 0x06000680 RID: 1664 RVA: 0x0002CF60 File Offset: 0x0002B360
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_colourIndex, 4);
	}

	// Token: 0x06000681 RID: 1665 RVA: 0x0002CF6F File Offset: 0x0002B36F
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_colourIndex = (int)reader.ReadUInt32(4);
		return true;
	}

	// Token: 0x0400055C RID: 1372
	public int m_colourIndex;

	// Token: 0x0400055D RID: 1373
	private const int indexBitSize = 4;
}
