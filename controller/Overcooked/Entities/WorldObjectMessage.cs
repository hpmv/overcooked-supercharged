using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging
{
	// Token: 0x020008DE RID: 2270
	public class WorldObjectMessage : Serialisable
	{
		// Token: 0x06002C28 RID: 11304 RVA: 0x000CD5DC File Offset: 0x000CB9DC
		public virtual void Serialise(BitStreamWriter writer)
		{
			writer.Write(this.HasParent);
			if (this.HasParent)
			{
				writer.Write(this.ParentEntityID, 10);
			}
			writer.Write(this.HasPositions);
			if (this.HasPositions)
			{
				writer.Write(ref this.LocalPosition);
				writer.Write(ref this.LocalRotation);
			}
		}

		// Token: 0x06002C29 RID: 11305 RVA: 0x000CD640 File Offset: 0x000CBA40
		public virtual bool Deserialise(BitStreamReader reader)
		{
			this.HasParent = reader.ReadBit();
			if (this.HasParent)
			{
				this.ParentEntityID = reader.ReadUInt32(10);
			}
			else
			{
				this.ParentEntityID = 0U;
			}
			this.HasPositions = reader.ReadBit();
			if (this.HasPositions)
			{
				reader.ReadVector3(ref this.LocalPosition);
				reader.ReadQuaternion(ref this.LocalRotation);
			}
			return true;
		}

		// Token: 0x06002C2A RID: 11306 RVA: 0x000CD6AE File Offset: 0x000CBAAE
		public void Copy(WorldObjectMessage _other)
		{
			this.LocalPosition = _other.LocalPosition;
			this.LocalRotation = _other.LocalRotation;
			this.HasParent = _other.HasParent;
			this.HasPositions = _other.HasPositions;
			this.ParentEntityID = _other.ParentEntityID;
		}

		// Token: 0x04002353 RID: 9043
		public Vector3 LocalPosition = default(Vector3);

		// Token: 0x04002354 RID: 9044
		public Quaternion LocalRotation = default(Quaternion);

		// Token: 0x04002355 RID: 9045
		public bool HasParent;

		// Token: 0x04002356 RID: 9046
		public bool HasPositions;

		// Token: 0x04002357 RID: 9047
		public uint ParentEntityID;
	}
}
