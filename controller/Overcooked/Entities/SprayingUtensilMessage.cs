using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008D6 RID: 2262
public class SprayingUtensilMessage : Serialisable
{
	// Token: 0x06002C03 RID: 11267 RVA: 0x000CCD32 File Offset: 0x000CB132
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_bSpraying);
		if (this.m_bSpraying)
		{
			writer.Write(this.m_Carrier, 10);
		}
	}

	// Token: 0x06002C04 RID: 11268 RVA: 0x000CCD59 File Offset: 0x000CB159
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_bSpraying = reader.ReadBit();
		if (this.m_bSpraying)
		{
			this.m_Carrier = reader.ReadUInt32(10);
		}
		return true;
	}

	// Token: 0x0400232A RID: 9002
	public bool m_bSpraying;

	// Token: 0x0400232B RID: 9003
	public uint m_Carrier;
}
