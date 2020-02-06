using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000A47 RID: 2631
public class DialogueStateMessage : Serialisable {
	// Token: 0x0600340E RID: 13326 RVA: 0x000F466E File Offset: 0x000F2A6E
	public void Serialise(BitStreamWriter _writer) {
		_writer.Write((uint) this.m_dialogueID, 24);
		_writer.Write((uint) this.m_state, 8);
	}

	// Token: 0x0600340F RID: 13327 RVA: 0x000F468B File Offset: 0x000F2A8B
	public bool Deserialise(BitStreamReader _reader) {
		this.m_dialogueID = (int) _reader.ReadUInt32(24);
		this.m_state = (int) _reader.ReadUInt32(8);
		return true;
	}

	// Token: 0x0400299B RID: 10651
	private const int c_bitsPerDialogueID = 24;

	// Token: 0x0400299C RID: 10652
	private const int c_bitsPerState = 8;

	// Token: 0x0400299D RID: 10653
	public int m_dialogueID;

	// Token: 0x0400299E RID: 10654
	public int m_state;
}