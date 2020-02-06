using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020008C9 RID: 2249
public class PhysicalAttachMessage : Serialisable {
	// Token: 0x06002BB7 RID: 11191 RVA: 0x000CBB78 File Offset: 0x000C9F78
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06002BB8 RID: 11192 RVA: 0x000CBBD4 File Offset: 0x000C9FD4
	public bool Deserialise(BitStreamReader reader) {
		uint num = reader.ReadUInt32(10);
		if (num != 0U) {
			m_parent = (int) num;
		} else {
			this.m_parent = -1;
		}
		return true;
	}

	// Token: 0x040022EC RID: 8940
	public int m_parent;
}