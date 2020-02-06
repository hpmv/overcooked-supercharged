using System;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging
{
	// Token: 0x020008C2 RID: 2242
	public class MapAvatarControlsMessage : Serialisable
	{
		// Token: 0x06002BA6 RID: 11174 RVA: 0x000CB9DC File Offset: 0x000C9DDC
		public void Serialise(BitStreamWriter writer)
		{
			for (int i = 0; i < 4; i++)
			{
				writer.Write(this.m_bHorns[i]);
			}
			writer.Write(this.m_bDash);
			writer.Write(this.CurrentSelectableEntityId, 10);
		}

		// Token: 0x06002BA7 RID: 11175 RVA: 0x000CBA24 File Offset: 0x000C9E24
		public bool Deserialise(BitStreamReader reader)
		{
			for (int i = 0; i < 4; i++)
			{
				this.m_bHorns[i] = reader.ReadBit();
			}
			this.m_bDash = reader.ReadBit();
			this.CurrentSelectableEntityId = reader.ReadUInt32(10);
			return true;
		}

		// Token: 0x040022B0 RID: 8880
		public bool[] m_bHorns = new bool[4];

		// Token: 0x040022B1 RID: 8881
		public bool m_bDash;

		// Token: 0x040022B2 RID: 8882
		public uint CurrentSelectableEntityId;
	}
}
