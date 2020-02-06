using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x02000166 RID: 358
public class TriggerAnimatorVariableMessage : Serialisable {
	// Token: 0x06000660 RID: 1632 RVA: 0x0002CA2C File Offset: 0x0002AE2C
	public void Serialise(BitStreamWriter writer) {
		writer.Write((uint) this.m_type, 2);
		TriggerAnimatorVariableMessage.RandomValueType type = this.m_type;
		if (type != TriggerAnimatorVariableMessage.RandomValueType.Bool) {
			if (type != TriggerAnimatorVariableMessage.RandomValueType.Float) {
				if (type == TriggerAnimatorVariableMessage.RandomValueType.Int) {
					writer.Write((float) ((int) this.m_randomValue));
				}
			} else {
				writer.Write((float) this.m_randomValue);
			}
		} else {
			writer.Write((bool) this.m_randomValue);
		}
	}

	// Token: 0x06000661 RID: 1633 RVA: 0x0002CAAC File Offset: 0x0002AEAC
	public bool Deserialise(BitStreamReader reader) {
		this.m_type = (TriggerAnimatorVariableMessage.RandomValueType) reader.ReadUInt32(2);
		TriggerAnimatorVariableMessage.RandomValueType type = this.m_type;
		if (type != TriggerAnimatorVariableMessage.RandomValueType.Bool) {
			if (type != TriggerAnimatorVariableMessage.RandomValueType.Float) {
				if (type == TriggerAnimatorVariableMessage.RandomValueType.Int) {
					this.m_randomValue = (int) reader.ReadFloat32();
				}
			} else {
				this.m_randomValue = reader.ReadFloat32();
			}
		} else {
			this.m_randomValue = reader.ReadBit();
		}
		return true;
	}

	// Token: 0x04000538 RID: 1336
	private TriggerAnimatorVariableMessage.RandomValueType m_type;

	// Token: 0x04000539 RID: 1337
	private const int m_kBitsPerType = 2;

	// Token: 0x0400053A RID: 1338
	public object m_randomValue;

	// Token: 0x02000167 RID: 359
	public enum RandomValueType {
		// Token: 0x0400053C RID: 1340
		None,
		// Token: 0x0400053D RID: 1341
		Bool,
		// Token: 0x0400053E RID: 1342
		Int,
		// Token: 0x0400053F RID: 1343
		Float
	}
}