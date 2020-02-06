using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000527 RID: 1319
public class PilotRotationMessage : Serialisable
{
	// Token: 0x060018C4 RID: 6340 RVA: 0x0007DBA5 File Offset: 0x0007BFA5
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_angle);
	}

	// Token: 0x060018C5 RID: 6341 RVA: 0x0007DBB3 File Offset: 0x0007BFB3
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_angle = reader.ReadFloat32();
		return true;
	}

	// Token: 0x040013C2 RID: 5058
	public float m_angle;
}
