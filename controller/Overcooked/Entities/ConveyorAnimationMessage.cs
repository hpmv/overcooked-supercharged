using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020005A7 RID: 1447
public class ConveyorAnimationMessage : Serialisable {
	// Token: 0x06001B96 RID: 7062 RVA: 0x00087429 File Offset: 0x00085829
	public void Initialise(TriggerAnimationOnConveyor.State _state) {
		this.m_state = _state;
	}

	// Token: 0x06001B97 RID: 7063 RVA: 0x00087432 File Offset: 0x00085832
	public void Serialise(BitStreamWriter writer) {
		writer.Write((uint) this.m_state, 2);
	}

	// Token: 0x06001B98 RID: 7064 RVA: 0x00087441 File Offset: 0x00085841
	public bool Deserialise(BitStreamReader reader) {
		this.m_state = (TriggerAnimationOnConveyor.State) reader.ReadUInt32(2);
		return true;
	}

	// Token: 0x04001578 RID: 5496
	public const int kBitsPerState = 2;

	// Token: 0x04001579 RID: 5497
	public TriggerAnimationOnConveyor.State m_state;
}
public class TriggerAnimationOnConveyor {
	// Token: 0x02000601 RID: 1537
	public enum State {
		// Token: 0x040017BB RID: 6075
		Idle,
		// Token: 0x040017BC RID: 6076
		Pending,
		// Token: 0x040017BD RID: 6077
		Animating
	}
}