using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    internal class CannonModMessage : Serialisable
	{
		// Token: 0x06001430 RID: 5168 RVA: 0x0006E42C File Offset: 0x0006C82C
		public void Serialise(BitStreamWriter writer)
		{
			writer.Write((uint)this.m_state, m_stateBits);
			writer.Write(this.m_angle);
			bool flag = this.m_loadedObject != null;
			writer.Write(flag);
			if (flag)
			{
				EntitySerialisationEntry entry = EntitySerialisationRegistry.GetEntry(this.m_loadedObject);
				entry.m_Header.Serialise(writer);
			}
			writer.Write(ref m_startingPosition);
			writer.Write(ref m_targetPosition);
			writer.Write(ref m_startingRotation);
			writer.Write(ref m_targetRotation);
			writer.Write(m_startingHeight);
			writer.Write(m_maxHeight);
			writer.Write(m_flyingTime);

			writer.Write(ref m_exitPosition);
			writer.Write(ref m_exitRotation);

			writer.Write(m_loadingTime);
		}

		// Token: 0x06001431 RID: 5169 RVA: 0x0006E484 File Offset: 0x0006C884
		public bool Deserialise(BitStreamReader reader)
		{
			this.m_state = (CannonState)reader.ReadUInt32(m_stateBits);
			this.m_angle = reader.ReadFloat32();
			if (reader.ReadBit())
			{
				this.m_entityHeader.Deserialise(reader);
				this.m_loadedObject = EntitySerialisationRegistry.GetEntry(this.m_entityHeader.m_uEntityID).m_GameObject;
			}
			else
			{
				this.m_loadedObject = null;
			}
			reader.ReadVector3(ref m_startingPosition);
			reader.ReadVector3(ref m_targetPosition);
			reader.ReadQuaternion(ref m_startingRotation);
			reader.ReadQuaternion(ref m_targetRotation);
			m_startingHeight = reader.ReadFloat32();
			m_maxHeight = reader.ReadFloat32();
			m_flyingTime = reader.ReadFloat32();

			reader.ReadVector3(ref m_exitPosition);
			reader.ReadQuaternion(ref m_exitRotation);
			m_loadingTime = reader.ReadFloat32();
			return true;
		}

		public void ClearDataOnExit()
		{
			m_startingPosition = default;
			m_targetPosition = default;
			m_startingRotation = default;
			m_targetRotation = default;
			m_startingHeight = default;
			m_maxHeight = default;
			m_flyingTime = default;
			m_exitPosition = default;
			m_exitRotation = default;
			m_loadingTime = default;
		}

		// Token: 0x04000FA6 RID: 4006
		public CannonState m_state;

		// Token: 0x04000FA7 RID: 4007
		public float m_angle;

		// Token: 0x04000FA8 RID: 4008
		public GameObject m_loadedObject;

		// Token: 0x04000FA9 RID: 4009
		public const int m_stateBits = 3;

		// Token: 0x04000FAA RID: 4010
		private EntityMessageHeader m_entityHeader = new EntityMessageHeader();

		// Token: 0x02000449 RID: 1097
		public enum CannonState
		{
			NotLoaded,
			Loaded,
			Loading,
			BeginLaunching,
			Flying,
		}

		public Vector3 m_startingPosition;
		public Vector3 m_targetPosition;
		public Quaternion m_startingRotation;
		public Quaternion m_targetRotation;
		public float m_startingHeight;
		public float m_maxHeight;
		public float m_flyingTime;

		public Vector3 m_exitPosition;
		public Quaternion m_exitRotation;

		public float m_loadingTime;
	}
}
