using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x02000425 RID: 1061
public class AttachStationMessage : Serialisable {
	// Token: 0x06001354 RID: 4948 RVA: 0x0006BF10 File Offset: 0x0006A310
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001355 RID: 4949 RVA: 0x0006BF60 File Offset: 0x0006A360
	public bool Deserialise(BitStreamReader reader) {
		bool flag = reader.ReadBit();
		if (flag) {
			this.m_itemHeader.Deserialise(reader);
			m_item = (int) this.m_itemHeader.m_uEntityID;
		} else {
			this.m_item = -1;
		}
		return true;
	}

	// Token: 0x04000F23 RID: 3875
	public int m_item;

	// Token: 0x04000F24 RID: 3876
	private EntityMessageHeader m_itemHeader = new EntityMessageHeader();
}