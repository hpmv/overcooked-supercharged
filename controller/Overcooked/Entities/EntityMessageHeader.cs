using System;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A8 RID: 2472
    public class EntityMessageHeader : Serialisable {
        // Token: 0x06002F42 RID: 12098 RVA: 0x0001EB60 File Offset: 0x0001CD60
        public void Serialise(BitStreamWriter writer) {
            writer.Write(this.m_uEntityID, 10);
        }

        // Token: 0x06002F43 RID: 12099 RVA: 0x0001EB70 File Offset: 0x0001CD70
        public bool Deserialise(BitStreamReader reader) {
            this.m_uEntityID = reader.ReadUInt32(10);
            return true;
        }

        // Token: 0x04002770 RID: 10096
        public uint m_uEntityID;
    }
}