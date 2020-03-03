using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020009DC RID: 2524
public class TimeSyncMessage : Serialisable {
    // Token: 0x06003019 RID: 12313 RVA: 0x0001F2F0 File Offset: 0x0001D4F0
    public void Initialise(float _fTime) {
        this.fTime = _fTime;
    }

    // Token: 0x0600301A RID: 12314 RVA: 0x0001F2F9 File Offset: 0x0001D4F9
    public void Serialise(BitStreamWriter writer) {
        writer.Write(this.fTime);
    }

    // Token: 0x0600301B RID: 12315 RVA: 0x0001F307 File Offset: 0x0001D507
    public bool Deserialise(BitStreamReader reader) {
        this.fTime = reader.ReadFloat32();
        return true;
    }

    // Token: 0x0400285A RID: 10330
    public float fTime;
}
