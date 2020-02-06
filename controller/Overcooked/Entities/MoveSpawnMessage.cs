using System;
using System.Collections.Generic;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020005D8 RID: 1496
public class MoveSpawnMessage : Serialisable {
	// Token: 0x06001C97 RID: 7319 RVA: 0x0008BBA0 File Offset: 0x00089FA0
	public void Serialise(BitStreamWriter _writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06001C98 RID: 7320 RVA: 0x0008BC00 File Offset: 0x0008A000
	public bool Deserialise(BitStreamReader _reader) {
		int num = (int) _reader.ReadUInt32(3);
		this.m_players = new int[num];
		this.m_indexes = new int[num];
		for (int i = 0; i < num; i++) {
			this.m_entityHeader.Deserialise(_reader);
			this.m_players[i] = (int) this.m_entityHeader.m_uEntityID;
			this.m_indexes[i] = (int) _reader.ReadUInt32(6);
		}
		return true;
	}

	// Token: 0x04001623 RID: 5667
	private const int c_BitsPerMapLength = 3;

	// Token: 0x04001624 RID: 5668
	private const int c_BitsPerSpawnPoint = 6;

	// Token: 0x04001625 RID: 5669
	private int[] m_players;

	// Token: 0x04001626 RID: 5670
	private int[] m_indexes;

	// Token: 0x04001627 RID: 5671
	private EntityMessageHeader m_entityHeader = new EntityMessageHeader();
}