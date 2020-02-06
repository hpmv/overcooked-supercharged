using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008A2 RID: 2210
public class CookingStateMessage : Serialisable {
	// Token: 0x06002B2A RID: 11050 RVA: 0x000C9FBE File Offset: 0x000C83BE
	public void Serialise(BitStreamWriter writer) {
		writer.Write((uint) this.m_cookingState, 4);
		writer.Write(this.m_cookingProgress);
	}

	// Token: 0x06002B2B RID: 11051 RVA: 0x000C9FD9 File Offset: 0x000C83D9
	public bool Deserialise(BitStreamReader reader) {
		this.m_cookingState = (CookingUIController.State) reader.ReadUInt32(4);
		this.m_cookingProgress = reader.ReadFloat32();
		return true;
	}

	// Token: 0x04002200 RID: 8704
	private const int kNumStateBits = 4;

	// Token: 0x04002201 RID: 8705
	public CookingUIController.State m_cookingState;

	// Token: 0x04002202 RID: 8706
	public float m_cookingProgress;
}

// Token: 0x02000C84 RID: 3204
public class CookingUIController {
	// Token: 0x02000C85 RID: 3205
	public enum State {
		// Token: 0x04003512 RID: 13586
		Idle,
		// Token: 0x04003513 RID: 13587
		Progressing,
		// Token: 0x04003514 RID: 13588
		Completed,
		// Token: 0x04003515 RID: 13589
		OverDoing,
		// Token: 0x04003516 RID: 13590
		Ruined
	}
}