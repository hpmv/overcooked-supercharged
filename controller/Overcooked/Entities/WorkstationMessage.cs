using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000628 RID: 1576
public class WorkstationMessage : Serialisable {
	// Token: 0x06001E0E RID: 7694 RVA: 0x000916BA File Offset: 0x0008FABA
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001E0F RID: 7695 RVA: 0x000916F5 File Offset: 0x0008FAF5
	public bool Deserialise(BitStreamReader reader) {
		this.m_interacting = reader.ReadBit();
		this.m_interactorHeader.Deserialise(reader);
		if (this.m_interacting) {
			this.m_itemHeader.Deserialise(reader);
		}
		return true;
	}

	// Token: 0x04001700 RID: 5888
	public bool m_interacting;

	// Token: 0x04001701 RID: 5889
	public EntityMessageHeader m_interactorHeader = new EntityMessageHeader();

	// Token: 0x04001702 RID: 5890
	public EntityMessageHeader m_itemHeader = new EntityMessageHeader();
}