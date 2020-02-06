using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x0200080C RID: 2060
public class ThrowableItemMessage : Serialisable {

	// Token: 0x06002785 RID: 10117 RVA: 0x000B9860 File Offset: 0x000B7C60
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06002786 RID: 10118 RVA: 0x000B989C File Offset: 0x000B7C9C
	public bool Deserialise(BitStreamReader reader) {
		this.m_inFlight = reader.ReadBit();
		if (this.m_inFlight) {
			this.m_entityHeader.Deserialise(reader);
			this.m_thrower = (int) this.m_entityHeader.m_uEntityID;
		}
		return true;
	}

	// Token: 0x04001EE1 RID: 7905
	public int m_thrower = -1;

	// Token: 0x04001EE2 RID: 7906
	public bool m_inFlight;

	// Token: 0x04001EE3 RID: 7907
	private EntityMessageHeader m_entityHeader = new EntityMessageHeader();
}