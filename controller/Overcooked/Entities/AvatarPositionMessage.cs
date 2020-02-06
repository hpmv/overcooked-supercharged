using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging
{
	// Token: 0x02000896 RID: 2198
	public class AvatarPositionMessage : Serialisable
	{
		// Token: 0x06002AF4 RID: 10996 RVA: 0x000C93EC File Offset: 0x000C77EC
		public void Serialise(BitStreamWriter writer)
		{
			this.WorldObject.Serialise(writer);
			writer.Write(this.Velocity.x);
			writer.Write(this.Velocity.y);
			writer.Write(this.Velocity.z);
		}

		// Token: 0x06002AF5 RID: 10997 RVA: 0x000C9438 File Offset: 0x000C7838
		public bool Deserialise(BitStreamReader reader)
		{
			this.WorldObject.Deserialise(reader);
			this.Velocity.Set(reader.ReadFloat32(), reader.ReadFloat32(), reader.ReadFloat32());
			return true;
		}

		// Token: 0x040021C2 RID: 8642
		public WorldObjectMessage WorldObject = new WorldObjectMessage();

		// Token: 0x040021C3 RID: 8643
		public Vector3 Velocity = Vector3.zero;
	}
}
