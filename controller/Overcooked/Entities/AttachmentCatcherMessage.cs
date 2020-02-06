using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020009BF RID: 2495
public class AttachmentCatcherMessage : Serialisable {

	// Token: 0x060030F9 RID: 12537 RVA: 0x000E587C File Offset: 0x000E3C7C
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x060030FA RID: 12538 RVA: 0x000E58B8 File Offset: 0x000E3CB8
	public bool Deserialise(BitStreamReader reader) {
		this.m_hasObject = reader.ReadBit();
		if (this.m_hasObject) {
			this.m_entityHeader.Deserialise(reader);
			m_object = (int) this.m_entityHeader.m_uEntityID;
		} else {
			this.m_object = -1;
		}
		return true;
	}

	// Token: 0x04002712 RID: 10002
	public int m_object;

	// Token: 0x04002713 RID: 10003
	public bool m_hasObject;

	// Token: 0x04002714 RID: 10004
	private EntityMessageHeader m_entityHeader = new EntityMessageHeader();
}