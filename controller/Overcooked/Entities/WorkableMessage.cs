using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000810 RID: 2064
public class WorkableMessage : Serialisable
{
	// Token: 0x060027AF RID: 10159 RVA: 0x000B9FE4 File Offset: 0x000B83E4
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_onWorkstation);
		writer.Write((uint)this.m_progress, 4);
		writer.Write((uint)this.m_subProgress, 4);
	}

	// Token: 0x060027B0 RID: 10160 RVA: 0x000BA00C File Offset: 0x000B840C
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_onWorkstation = reader.ReadBit();
		this.m_progress = (int)reader.ReadUInt32(4);
		this.m_subProgress = (int)reader.ReadUInt32(4);
		return true;
	}

	// Token: 0x04001F05 RID: 7941
	public bool m_onWorkstation;

	// Token: 0x04001F06 RID: 7942
	public int m_progress;

	// Token: 0x04001F07 RID: 7943
	public int m_subProgress;
}
