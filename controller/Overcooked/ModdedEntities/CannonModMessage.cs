using BitStream;
using Hpmv;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

public class CannonModMessage : Serialisable
{
	// Token: 0x06001430 RID: 5168 RVA: 0x0006E42C File Offset: 0x0006C82C
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)m_state, m_stateBits);
		writer.Write(m_angle);
		if (m_state != CannonState.NotLoaded)
		{
			m_entityHeader.Serialise(writer);
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
		m_state = (CannonState)reader.ReadUInt32(m_stateBits);
		m_angle = reader.ReadFloat32();
		if (m_state != CannonState.NotLoaded)
		{
			m_entityHeader.Deserialise(reader);
		} else {
			m_entityHeader.m_uEntityID = 0;  // default value for determinism
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

	public string ToJson() {
		return (
			"{\n" +
			"  \"m_state\": " + m_state + ",\n" +
			"  \"m_angle\": " + m_angle + ",\n" +
			"  \"m_entityHeader\": " + m_entityHeader.m_uEntityID + ",\n" +
			"  \"m_startingPosition\": " + m_startingPosition.ToNumerics().ToString() + ",\n" +
			"  \"m_targetPosition\": " + m_targetPosition.ToNumerics().ToString() + ",\n" +
			"  \"m_startingRotation\": " + m_startingRotation.ToNumerics().ToString() + ",\n" +
			"  \"m_targetRotation\": " + m_targetRotation.ToNumerics().ToString() + ",\n" +
			"  \"m_startingHeight\": " + m_startingHeight + ",\n" +
			"  \"m_maxHeight\": " + m_maxHeight + ",\n" +
			"  \"m_flyingTime\": " + m_flyingTime + ",\n" +
			"  \"m_exitPosition\": " + m_exitPosition.ToNumerics().ToString() + ",\n" +
			"  \"m_exitRotation\": " + m_exitRotation.ToNumerics().ToString() + ",\n" +
			"  \"m_loadingTime\": " + m_loadingTime + "\n" +
			"}"
		);
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

	// Token: 0x04000FA9 RID: 4009
	public const int m_stateBits = 3;

	// Token: 0x04000FAA RID: 4010
	public EntityMessageHeader m_entityHeader = new EntityMessageHeader();

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
	public UnityEngine.Quaternion m_startingRotation;
	public UnityEngine.Quaternion m_targetRotation;
	public float m_startingHeight;
	public float m_maxHeight;
	public float m_flyingTime;

	public Vector3 m_exitPosition;
	public UnityEngine.Quaternion m_exitRotation;

	public float m_loadingTime;
}