using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x0200043E RID: 1086
public class CannonMessage : Serialisable {
	// Token: 0x0600141A RID: 5146 RVA: 0x0006DED4 File Offset: 0x0006C2D4
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x0600141B RID: 5147 RVA: 0x0006DF2C File Offset: 0x0006C32C
	public bool Deserialise(BitStreamReader reader) {
		this.m_state = (CannonMessage.CannonState) reader.ReadUInt32(2);
		this.m_angle = reader.ReadFloat32();
		if (reader.ReadBit()) {
			this.m_entityHeader.Deserialise(reader);
			this.m_loadedObject = (int) this.m_entityHeader.m_uEntityID;
		} else {
			this.m_loadedObject = -1;
		}
		return true;
	}

	// Token: 0x04000F7F RID: 3967
	public CannonMessage.CannonState m_state;

	// Token: 0x04000F80 RID: 3968
	public float m_angle;

	// Token: 0x04000F81 RID: 3969
	public int m_loadedObject;

	// Token: 0x04000F82 RID: 3970
	public const int m_stateBits = 2;

	// Token: 0x04000F83 RID: 3971
	private EntityMessageHeader m_entityHeader = new EntityMessageHeader();

	// Token: 0x0200043F RID: 1087
	public enum CannonState {
		// Token: 0x04000F85 RID: 3973
		Launched,
		// Token: 0x04000F86 RID: 3974
		Load,
		// Token: 0x04000F87 RID: 3975
		Unload
	}
}