using System;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A3 RID: 2467
    public class DestroyEntityMessage : Serialisable {
        // Token: 0x06002F33 RID: 12083 RVA: 0x0001EAAF File Offset: 0x0001CCAF
        public void Initialise(EntityMessageHeader header) {
            this.m_Header = header;
        }

        // Token: 0x06002F34 RID: 12084 RVA: 0x0001EAB8 File Offset: 0x0001CCB8
        public void Serialise(BitStreamWriter writer) {
            this.m_Header.Serialise(writer);
        }

        // Token: 0x06002F35 RID: 12085 RVA: 0x0001EAC6 File Offset: 0x0001CCC6
        public bool Deserialise(BitStreamReader reader) {
            return this.m_Header.Deserialise(reader);
        }

        // Token: 0x04002726 RID: 10022
        public EntityMessageHeader m_Header = new EntityMessageHeader();
    }
}