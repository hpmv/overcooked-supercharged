using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x0200056D RID: 1389
public class ConveyorStationMessage : Serialisable {
	// Token: 0x06001A52 RID: 6738 RVA: 0x00083FA8 File Offset: 0x000823A8
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001A53 RID: 6739 RVA: 0x00083FF4 File Offset: 0x000823F4
	public bool Deserialise(BitStreamReader reader) {
		this.m_receiverHeader.Deserialise(reader);
		this.m_receiverEntityID = this.m_receiverHeader.m_uEntityID;
		this.m_receiverItem.Deserialise(reader);
		this.m_itemEntityID = this.m_receiverItem.m_uEntityID;
		this.m_arriveTime = reader.ReadFloat32();
		return true;
	}

	// Token: 0x040014AB RID: 5291
	public uint m_receiverEntityID;

	// Token: 0x040014AC RID: 5292
	public uint m_itemEntityID;

	// Token: 0x040014AD RID: 5293
	public float m_arriveTime;

	// Token: 0x040014AE RID: 5294
	private EntityMessageHeader m_receiverHeader = new EntityMessageHeader();

	// Token: 0x040014AF RID: 5295
	private EntityMessageHeader m_receiverItem = new EntityMessageHeader();
}