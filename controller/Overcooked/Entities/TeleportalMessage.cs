using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020008D7 RID: 2263
public class TeleportalMessage : Serialisable {
	// Token: 0x06002C09 RID: 11273 RVA: 0x000CCDFC File Offset: 0x000CB1FC
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06002C0A RID: 11274 RVA: 0x000CCE98 File Offset: 0x000CB298
	public bool Deserialise(BitStreamReader reader) {
		this.m_msgType = (TeleportalMessage.MsgType) reader.ReadUInt32(2);
		TeleportalMessage.MsgType msgType = this.m_msgType;
		if (msgType != TeleportalMessage.MsgType.PortalState) {
			if (msgType == TeleportalMessage.MsgType.TeleportStart || msgType == TeleportalMessage.MsgType.TeleportEnd) {
				this.m_clientSender = this.DeserialiseComponentByIndex(reader);
				this.m_clientReceiver = this.DeserialiseComponentByIndex(reader);
				this.m_entityHeader.Deserialise(reader);
				this.m_object = this.m_entityHeader.m_uEntityID;
			}
		} else {
			this.m_canTeleport = reader.ReadBit();
			this.m_isTeleporting = reader.ReadBit();
		}
		return true;
	}

	// Token: 0x06002C0C RID: 11276 RVA: 0x000CCFA0 File Offset: 0x000CB3A0
	private(uint entity, int index) DeserialiseComponentByIndex(BitStreamReader reader) {
		this.m_entityHeader.Deserialise(reader);
		uint entity = this.m_entityHeader.m_uEntityID;
		int index = (int) reader.ReadUInt32(8);
		return (entity, index);
	}

	// Token: 0x0400232C RID: 9004
	public const int kMsgTypeBits = 2;

	// Token: 0x0400232D RID: 9005
	public const int kComponentIndexBits = 8;

	// Token: 0x0400232E RID: 9006
	public TeleportalMessage.MsgType m_msgType;

	// Token: 0x0400232F RID: 9007
	public bool m_canTeleport;

	// Token: 0x04002330 RID: 9008
	public bool m_isTeleporting;

	// Token: 0x04002333 RID: 9011
	public uint m_object;

	// Token: 0x04002334 RID: 9012
	public(uint entity, int index) m_clientSender;

	// Token: 0x04002335 RID: 9013
	public(uint entity, int index) m_clientReceiver;

	// Token: 0x04002336 RID: 9014
	private EntityMessageHeader m_entityHeader = new EntityMessageHeader();

	// Token: 0x020008D8 RID: 2264
	public enum MsgType {
		// Token: 0x04002338 RID: 9016
		PortalState,
		// Token: 0x04002339 RID: 9017
		TeleportStart,
		// Token: 0x0400233A RID: 9018
		TeleportEnd
	}
}