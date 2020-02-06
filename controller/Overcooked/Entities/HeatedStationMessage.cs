using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020004A1 RID: 1185
public class HeatedStationMessage : Serialisable
{
	// Token: 0x06001619 RID: 5657 RVA: 0x0007593D File Offset: 0x00073D3D
	public void Serialise(BitStreamWriter _writer)
	{
		_writer.Write((uint)this.m_msgType, 2);
		if (this.m_msgType == HeatedStationMessage.MsgType.Heat)
		{
			_writer.Write(this.m_heat);
		}
	}

	// Token: 0x0600161A RID: 5658 RVA: 0x00075963 File Offset: 0x00073D63
	public bool Deserialise(BitStreamReader _reader)
	{
		this.m_msgType = (HeatedStationMessage.MsgType)_reader.ReadUInt32(2);
		if (this.m_msgType == HeatedStationMessage.MsgType.Heat)
		{
			this.m_heat = _reader.ReadFloat32();
		}
		return true;
	}

	// Token: 0x040010A6 RID: 4262
	public HeatedStationMessage.MsgType m_msgType;

	// Token: 0x040010A7 RID: 4263
	public float m_heat;

	// Token: 0x020004A2 RID: 1186
	public enum MsgType
	{
		// Token: 0x040010A9 RID: 4265
		Heat,
		// Token: 0x040010AA RID: 4266
		ItemAdded
	}
}
