using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x02000545 RID: 1349
public class PlateStationMessage : Serialisable {
	// Token: 0x06001984 RID: 6532 RVA: 0x0007FDBC File Offset: 0x0007E1BC
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001985 RID: 6533 RVA: 0x0007FDF0 File Offset: 0x0007E1F0
	public bool Deserialise(BitStreamReader reader) {
		bool flag = this.m_deliveredHeader.Deserialise(reader);
		if (flag) {
			this.m_delivered = this.m_deliveredHeader.m_uEntityID;
			this.m_success = reader.ReadBit();
		}
		return flag;
	}

	// Token: 0x04001414 RID: 5140
	public uint m_delivered;

	// Token: 0x04001415 RID: 5141
	public bool m_success;

	// Token: 0x04001416 RID: 5142
	private EntityMessageHeader m_deliveredHeader = new EntityMessageHeader();
}