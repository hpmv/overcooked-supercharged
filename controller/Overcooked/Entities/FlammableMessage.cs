using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000577 RID: 1399
public class FlammableMessage : Serialisable
{
	// Token: 0x06001A8C RID: 6796 RVA: 0x00084FFE File Offset: 0x000833FE
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write(this.m_playerExtinguished);
		writer.Write(this.m_onFire);
		writer.Write(this.m_fireStrength);
		writer.Write(this.m_fireStrengthVelocity);
	}

	// Token: 0x06001A8D RID: 6797 RVA: 0x00085030 File Offset: 0x00083430
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_playerExtinguished = reader.ReadBit();
		this.m_onFire = reader.ReadBit();
		this.m_fireStrength = reader.ReadFloat32();
		this.m_fireStrengthVelocity = reader.ReadFloat32();
		return true;
	}

	// Token: 0x040014E1 RID: 5345
	public bool m_playerExtinguished;

	// Token: 0x040014E2 RID: 5346
	public bool m_onFire;

	// Token: 0x040014E3 RID: 5347
	public float m_fireStrength;

	// Token: 0x040014E4 RID: 5348
	public float m_fireStrengthVelocity;
}
