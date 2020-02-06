using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging
{
	// Token: 0x020008CA RID: 2250
	public class PhysicsObjectMessage : Serialisable
	{
		// Token: 0x06002BBA RID: 11194 RVA: 0x000CBC7C File Offset: 0x000CA07C
		public virtual void Serialise(BitStreamWriter writer)
		{
			writer.Write(ref this.Velocity);
			writer.Write(this.ContactCount, 3);
			int num = 0;
			while ((long)num < (long)((ulong)this.ContactCount))
			{
				writer.Write(this.Contacts[num], 10);
				writer.Write(ref this.RelativePositions[num]);
				writer.Write(ref this.ContactVelocitys[num]);
				writer.Write(this.ContactTimes[num]);
				num++;
			}
			this.WorldObject.Serialise(writer);
		}

		// Token: 0x06002BBB RID: 11195 RVA: 0x000CBD0C File Offset: 0x000CA10C
		public virtual bool Deserialise(BitStreamReader reader)
		{
			reader.ReadVector3(ref this.Velocity);
			this.ContactCount = reader.ReadUInt32(3);
			int num = 0;
			while ((long)num < (long)((ulong)this.ContactCount))
			{
				this.Contacts[num] = reader.ReadUInt32(10);
				reader.ReadVector3(ref this.RelativePositions[num]);
				reader.ReadVector3(ref this.ContactVelocitys[num]);
				this.ContactTimes[num] = reader.ReadFloat32();
				num++;
			}
			return this.WorldObject.Deserialise(reader);
		}

		// Token: 0x06002BBC RID: 11196 RVA: 0x000CBD9C File Offset: 0x000CA19C
		public void Copy(PhysicsObjectMessage _other)
		{
			this.Velocity = _other.Velocity;
			this.ContactCount = _other.ContactCount;
			int num = 0;
			while ((long)num < (long)((ulong)this.ContactCount))
			{
				this.Contacts[num] = _other.Contacts[num];
				this.RelativePositions[num] = _other.RelativePositions[num];
				this.ContactVelocitys[num] = _other.ContactVelocitys[num];
				this.ContactTimes[num] = _other.ContactTimes[num];
				num++;
			}
			this.WorldObject.Copy(_other.WorldObject);
		}

		// Token: 0x040022ED RID: 8941
		public const int kTrackedChefCount = 4;

		// Token: 0x040022EE RID: 8942
		public Vector3 Velocity = default(Vector3);

		// Token: 0x040022EF RID: 8943
		public uint ContactCount;

		// Token: 0x040022F0 RID: 8944
		public uint[] Contacts = new uint[4];

		// Token: 0x040022F1 RID: 8945
		public Vector3[] RelativePositions = new Vector3[4];

		// Token: 0x040022F2 RID: 8946
		public Vector3[] ContactVelocitys = new Vector3[4];

		// Token: 0x040022F3 RID: 8947
		public float[] ContactTimes = new float[4];

		// Token: 0x040022F4 RID: 8948
		public WorldObjectMessage WorldObject = new WorldObjectMessage();
	}
}
