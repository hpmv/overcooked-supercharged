using System;
using BitStream;
using OrderController;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x020008B7 RID: 2231
public class KitchenFlowMessage : Serialisable {
	// Token: 0x06002B7B RID: 11131 RVA: 0x000CAFCC File Offset: 0x000C93CC
	public void Serialise(BitStreamWriter writer) {
		throw new NotImplementedException();
	}

	// Token: 0x06002B7C RID: 11132 RVA: 0x000CB0C8 File Offset: 0x000C94C8
	public bool Deserialise(BitStreamReader reader) {
		this.m_msgType = (KitchenFlowMessage.MsgType) reader.ReadUInt32(2);
		this.m_teamID = (TeamID) reader.ReadUInt32(2);
		switch (this.m_msgType) {
			case KitchenFlowMessage.MsgType.Delivery:
				this.m_success = reader.ReadBit();
				this.m_teamScore.Deserialise(reader);
				if (this.m_success) {
					this.m_plateStationHeader.Deserialise(reader);
					this.m_plateStation = (int) this.m_plateStationHeader.m_uEntityID;
					this.m_orderID.Deserialise(reader);
					this.m_wasCombo = reader.ReadBit();
					this.m_timePropRemainingPercentage = reader.ReadFloat32();
					this.m_tip = (int) reader.ReadUInt32(6);
				}
				break;
			case KitchenFlowMessage.MsgType.OrderAdded:
				this.m_orderData.Deserialise(reader);
				break;
			case KitchenFlowMessage.MsgType.OrderExpired:
				this.m_orderID.Deserialise(reader);
				this.m_teamScore.Deserialise(reader);
				break;
			case KitchenFlowMessage.MsgType.ScoreOnly:
				this.m_teamScore.Deserialise(reader);
				break;
		}
		return true;
	}

	// Token: 0x04002280 RID: 8832
	public const int kMsgTypeBits = 2;

	// Token: 0x04002281 RID: 8833
	public const int kTeamIDBits = 2;

	// Token: 0x04002282 RID: 8834
	public const int kTipBits = 6;

	// Token: 0x04002283 RID: 8835
	public KitchenFlowMessage.MsgType m_msgType;

	// Token: 0x04002284 RID: 8836
	public TeamID m_teamID;

	// Token: 0x04002285 RID: 8837
	public TeamMonitor.TeamScoreStats m_teamScore = new TeamMonitor.TeamScoreStats();

	// Token: 0x04002286 RID: 8838
	public OrderID m_orderID;

	// Token: 0x04002287 RID: 8839
	public ServerOrderData m_orderData = new ServerOrderData();

	// Token: 0x04002288 RID: 8840
	public int m_plateStation;

	// Token: 0x04002289 RID: 8841
	public bool m_success;

	// Token: 0x0400228A RID: 8842
	public bool m_wasCombo;

	// Token: 0x0400228B RID: 8843
	public int m_tip;

	// Token: 0x0400228C RID: 8844
	public float m_timePropRemainingPercentage;

	// Token: 0x0400228D RID: 8845
	private EntityMessageHeader m_plateStationHeader = new EntityMessageHeader();

	// Token: 0x020008B8 RID: 2232
	public enum MsgType {
		// Token: 0x0400228F RID: 8847
		Delivery,
		// Token: 0x04002290 RID: 8848
		OrderAdded,
		// Token: 0x04002291 RID: 8849
		OrderExpired,
		// Token: 0x04002292 RID: 8850
		ScoreOnly
	}
}

// Token: 0x02000846 RID: 2118
public enum TeamID {
	// Token: 0x040020DE RID: 8414
	One,
	// Token: 0x040020DF RID: 8415
	Two,
	// Token: 0x040020E0 RID: 8416
	None,
	// Token: 0x040020E1 RID: 8417
	Count
}