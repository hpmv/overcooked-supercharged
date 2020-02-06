using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020008B5 RID: 2229
public class InputEventMessage : Serialisable
{
	// Token: 0x06002B6F RID: 11119 RVA: 0x000CAE04 File Offset: 0x000C9204
	public InputEventMessage()
	{
	}

	// Token: 0x06002B70 RID: 11120 RVA: 0x000CAE0C File Offset: 0x000C920C
	public InputEventMessage(InputEventMessage.InputEventType inputEventType)
	{
		this.m_inputEventType = inputEventType;
	}

	// Token: 0x17000339 RID: 825
	// (get) Token: 0x06002B71 RID: 11121 RVA: 0x000CAE1B File Offset: 0x000C921B
	public InputEventMessage.InputEventType inputEventType
	{
		get
		{
			return this.m_inputEventType;
		}
	}

	// Token: 0x06002B72 RID: 11122 RVA: 0x000CAE24 File Offset: 0x000C9224
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_inputEventType, 10);
		writer.Write(this.entityId, 10);
		if (this.m_inputEventType == InputEventMessage.InputEventType.DashCollision)
		{
			writer.Write(this.collisionContactPoint.x);
			writer.Write(this.collisionContactPoint.y);
			writer.Write(this.collisionContactPoint.z);
		}
	}

	// Token: 0x06002B73 RID: 11123 RVA: 0x000CAE8C File Offset: 0x000C928C
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_inputEventType = (InputEventMessage.InputEventType)reader.ReadUInt32(10);
		this.entityId = reader.ReadUInt32(10);
		if (this.m_inputEventType == InputEventMessage.InputEventType.DashCollision)
		{
			this.collisionContactPoint.x = reader.ReadFloat32();
			this.collisionContactPoint.y = reader.ReadFloat32();
			this.collisionContactPoint.z = reader.ReadFloat32();
		}
		return true;
	}

	// Token: 0x04002273 RID: 8819
	private InputEventMessage.InputEventType m_inputEventType;

	// Token: 0x04002274 RID: 8820
	public uint entityId;

	// Token: 0x04002275 RID: 8821
	public Vector3 collisionContactPoint;

	// Token: 0x020008B6 RID: 2230
	public enum InputEventType
	{
		// Token: 0x04002277 RID: 8823
		Dash,
		// Token: 0x04002278 RID: 8824
		DashCollision,
		// Token: 0x04002279 RID: 8825
		Catch,
		// Token: 0x0400227A RID: 8826
		Curse,
		// Token: 0x0400227B RID: 8827
		BeginInteraction,
		// Token: 0x0400227C RID: 8828
		EndInteraction,
		// Token: 0x0400227D RID: 8829
		TriggerInteraction,
		// Token: 0x0400227E RID: 8830
		StartThrow,
		// Token: 0x0400227F RID: 8831
		EndThrow
	}
}
