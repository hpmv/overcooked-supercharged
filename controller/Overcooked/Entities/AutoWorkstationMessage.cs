using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000567 RID: 1383
public class AutoWorkstationMessage : Serialisable {
	// Token: 0x06001A23 RID: 6691 RVA: 0x00082E24 File Offset: 0x00081224
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001A24 RID: 6692 RVA: 0x00082EA4 File Offset: 0x000812A4
	public bool Deserialise(BitStreamReader reader) {
		this.m_working = reader.ReadBit();
		if (this.m_working) {
			int num = (int) reader.ReadUInt32(4);
			Array.Resize<int>(ref this.m_items, num);
			for (int i = 0; i < num; i++) {
				bool flag = this.m_itemHeader.Deserialise(reader);
				if (flag) {
					if (this.m_itemHeader.m_uEntityID != 0U) {
						this.m_items[i] = (int) this.m_itemHeader.m_uEntityID;
					} else {
						this.m_items[i] = -1;
					}
				}
			}
		}
		return true;
	}

	// Token: 0x0400148E RID: 5262
	private const int kBitsPerItemCount = 4;

	// Token: 0x0400148F RID: 5263
	public int[] m_items = new int[0];

	// Token: 0x04001490 RID: 5264
	public bool m_working;

	// Token: 0x04001491 RID: 5265
	public EntityMessageHeader m_itemHeader = new EntityMessageHeader();
}