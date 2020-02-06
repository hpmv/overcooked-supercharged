using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x02000552 RID: 1362
public class RespawnColliderMessage : Serialisable {

	// Token: 0x060019C0 RID: 6592 RVA: 0x0008154C File Offset: 0x0007F94C
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x060019C1 RID: 6593 RVA: 0x00081580 File Offset: 0x0007F980
	public bool Deserialise(BitStreamReader reader) {
		this.m_targetObjectHeader.Deserialise(reader);
		m_targetObject = (int) this.m_targetObjectHeader.m_uEntityID;
		reader.ReadVector3(ref this.m_killPosition);
		return true;
	}

	// Token: 0x04001450 RID: 5200
	public int m_targetObject;

	// Token: 0x04001451 RID: 5201
	public Vector3 m_killPosition = Vector3.zero;

	// Token: 0x04001452 RID: 5202
	private EntityMessageHeader m_targetObjectHeader = new EntityMessageHeader();
}