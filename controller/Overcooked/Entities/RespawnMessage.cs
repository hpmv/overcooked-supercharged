using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020008CB RID: 2251
public class RespawnMessage : Serialisable
{
	// Token: 0x06002BBE RID: 11198 RVA: 0x000CBE72 File Offset: 0x000CA272
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_RespawnType, 4);
		writer.Write((uint)this.m_Phase, 4);
		if (this.m_Phase == RespawnMessage.Phase.End)
		{
			writer.Write(ref this.m_RespawnPosition);
		}
	}

	// Token: 0x06002BBF RID: 11199 RVA: 0x000CBEA6 File Offset: 0x000CA2A6
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_RespawnType = (RespawnCollider.RespawnType)reader.ReadUInt32(4);
		this.m_Phase = (RespawnMessage.Phase)reader.ReadUInt32(4);
		if (this.m_Phase == RespawnMessage.Phase.End)
		{
			reader.ReadVector3(ref this.m_RespawnPosition);
		}
		return true;
	}

	// Token: 0x040022F5 RID: 8949
	public RespawnCollider.RespawnType m_RespawnType;

	// Token: 0x040022F6 RID: 8950
	public RespawnMessage.Phase m_Phase;

	// Token: 0x040022F7 RID: 8951
	public Vector3 m_RespawnPosition = default(Vector3);

	// Token: 0x020008CC RID: 2252
	public enum Phase
	{
		// Token: 0x040022F9 RID: 8953
		Begin,
		// Token: 0x040022FA RID: 8954
		End
	}
}
