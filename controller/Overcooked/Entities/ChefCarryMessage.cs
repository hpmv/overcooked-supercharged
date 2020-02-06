using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000898 RID: 2200
public class ChefCarryMessage : Serialisable {
	// Token: 0x06002AFB RID: 11003 RVA: 0x000C9516 File Offset: 0x000C7916
	public void Serialise(BitStreamWriter writer) {
		writer.Write(this.m_carriableItem, 10);
		writer.Write((uint) this.m_playerAttachTarget, ChefCarryMessage.playerAttachTargetBits);
	}

	// Token: 0x06002AFC RID: 11004 RVA: 0x000C9537 File Offset: 0x000C7937
	public bool Deserialise(BitStreamReader reader) {
		this.m_carriableItem = reader.ReadUInt32(10);
		this.m_playerAttachTarget = (PlayerAttachTarget) reader.ReadUInt32(ChefCarryMessage.playerAttachTargetBits);
		return true;
	}

	// Token: 0x040021C8 RID: 8648
	private static readonly int playerAttachTargetBits = GameUtils.GetRequiredBitCount(2);

	// Token: 0x040021C9 RID: 8649
	public uint m_carriableItem;

	// Token: 0x040021CA RID: 8650
	public PlayerAttachTarget m_playerAttachTarget;
}

// Token: 0x020008D6 RID: 2262
public enum PlayerAttachTarget {
	// Token: 0x04002396 RID: 9110
	Default,
	// Token: 0x04002397 RID: 9111
	Back,
	// Token: 0x04002398 RID: 9112
	COUNT
}