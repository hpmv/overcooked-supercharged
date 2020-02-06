using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging
{
	// Token: 0x0200089E RID: 2206
	public class ChefPositionMessage : Serialisable
	{
		// Token: 0x06002B09 RID: 11017 RVA: 0x000C9760 File Offset: 0x000C7B60
		public void Serialise(BitStreamWriter writer)
		{
			this.WorldObject.Serialise(writer);
			writer.Write(this.Velocity.x);
			writer.Write(this.Velocity.y);
			writer.Write(this.Velocity.z);
			writer.Write(this.NetworkTime);
			writer.Write(this.ClientTimeStamp);
		}

		// Token: 0x06002B0A RID: 11018 RVA: 0x000C97C4 File Offset: 0x000C7BC4
		public bool Deserialise(BitStreamReader reader)
		{
			this.WorldObject.Deserialise(reader);
			this.Velocity.Set(reader.ReadFloat32(), reader.ReadFloat32(), reader.ReadFloat32());
			this.NetworkTime = reader.ReadFloat32();
			this.ClientTimeStamp = reader.ReadFloat32();
			return true;
		}

		// Token: 0x06002B0B RID: 11019 RVA: 0x000C9814 File Offset: 0x000C7C14
		public void Copy(ChefPositionMessage _other)
		{
			this.WorldObject.Copy(_other.WorldObject);
			this.Velocity = _other.Velocity;
			this.NetworkTime = _other.NetworkTime;
			this.ClientTimeStamp = _other.ClientTimeStamp;
		}

		// Token: 0x040021E8 RID: 8680
		public WorldObjectMessage WorldObject = new WorldObjectMessage();

		// Token: 0x040021E9 RID: 8681
		public Vector3 Velocity = Vector3.zero;

		// Token: 0x040021EA RID: 8682
		public float NetworkTime;

		// Token: 0x040021EB RID: 8683
		public float ClientTimeStamp;
	}
}
