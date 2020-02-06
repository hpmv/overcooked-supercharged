using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000522 RID: 1314
public class PickupItemSwitcherMessage : Serialisable
{
	// Token: 0x060018B2 RID: 6322 RVA: 0x0007D68A File Offset: 0x0007BA8A
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_itemIndex, 4);
	}

	// Token: 0x060018B3 RID: 6323 RVA: 0x0007D699 File Offset: 0x0007BA99
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_itemIndex = (int)reader.ReadUInt32(4);
		return true;
	}

	// Token: 0x040013B2 RID: 5042
	public int m_itemIndex;

	// Token: 0x040013B3 RID: 5043
	private const int indexBitSize = 4;
}
