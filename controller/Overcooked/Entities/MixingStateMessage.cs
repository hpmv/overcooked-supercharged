using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008C8 RID: 2248
public class MixingStateMessage : Serialisable
{
	// Token: 0x06002BB4 RID: 11188 RVA: 0x000CBB36 File Offset: 0x000C9F36
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_mixingState, 4);
		writer.Write(this.m_mixingProgress);
	}

	// Token: 0x06002BB5 RID: 11189 RVA: 0x000CBB51 File Offset: 0x000C9F51
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_mixingState = (CookingUIController.State)reader.ReadUInt32(4);
		this.m_mixingProgress = reader.ReadFloat32();
		return true;
	}

	// Token: 0x040022E9 RID: 8937
	private const int kNumStateBits = 4;

	// Token: 0x040022EA RID: 8938
	public CookingUIController.State m_mixingState;

	// Token: 0x040022EB RID: 8939
	public float m_mixingProgress;
}
