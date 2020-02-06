using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x0200061B RID: 1563
public class WashableMessage : Serialisable
{
	// Token: 0x06001DAB RID: 7595 RVA: 0x0008FF87 File Offset: 0x0008E387
	public void Initialise_Progress(float _progress, float _targetProgress)
	{
		this.m_msgType = WashableMessage.MsgType.Progress;
		this.m_progress = _progress;
		this.m_target = _targetProgress;
	}

	// Token: 0x06001DAC RID: 7596 RVA: 0x0008FF9E File Offset: 0x0008E39E
	public void Initialise_Rate(float _rate, float _currentProgress)
	{
		this.m_msgType = WashableMessage.MsgType.Rate;
		this.m_rate = _rate;
		this.m_progress = _currentProgress;
	}

	// Token: 0x06001DAD RID: 7597 RVA: 0x0008FFB8 File Offset: 0x0008E3B8
	public void Serialise(BitStreamWriter _writer)
	{
		_writer.Write((uint)this.m_msgType, 1);
		WashableMessage.MsgType msgType = this.m_msgType;
		if (msgType != WashableMessage.MsgType.Progress)
		{
			if (msgType == WashableMessage.MsgType.Rate)
			{
				_writer.Write(this.m_rate);
				_writer.Write(this.m_progress);
			}
		}
		else
		{
			_writer.Write(this.m_progress);
			_writer.Write(this.m_target);
		}
	}

	// Token: 0x06001DAE RID: 7598 RVA: 0x00090028 File Offset: 0x0008E428
	public bool Deserialise(BitStreamReader _reader)
	{
		this.m_msgType = (WashableMessage.MsgType)_reader.ReadUInt32(1);
		WashableMessage.MsgType msgType = this.m_msgType;
		if (msgType != WashableMessage.MsgType.Progress)
		{
			if (msgType == WashableMessage.MsgType.Rate)
			{
				this.m_rate = _reader.ReadFloat32();
				this.m_progress = _reader.ReadFloat32();
			}
		}
		else
		{
			this.m_progress = _reader.ReadFloat32();
			this.m_target = _reader.ReadFloat32();
		}
		return true;
	}

	// Token: 0x040016B7 RID: 5815
	private const int c_msgTypeBits = 1;

	// Token: 0x040016B8 RID: 5816
	public WashableMessage.MsgType m_msgType;

	// Token: 0x040016B9 RID: 5817
	public float m_progress;

	// Token: 0x040016BA RID: 5818
	public float m_target;

	// Token: 0x040016BB RID: 5819
	public float m_rate;

	// Token: 0x0200061C RID: 1564
	public enum MsgType
	{
		// Token: 0x040016BD RID: 5821
		Progress,
		// Token: 0x040016BE RID: 5822
		Rate
	}
}
