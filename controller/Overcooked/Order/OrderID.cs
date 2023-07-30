using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

namespace OrderController {
    // Token: 0x0200081A RID: 2074
    public struct OrderID : Serialisable {
        // Token: 0x0600276E RID: 10094 RVA: 0x000197DB File Offset: 0x000179DB
        public OrderID(uint _id) {
            this.m_id = _id;
        }

        // Token: 0x0600276F RID: 10095 RVA: 0x000197E4 File Offset: 0x000179E4
        public static bool operator ==(OrderID _id, OrderID _other) {
            return _id.m_id == _other.m_id;
        }

        // Token: 0x06002770 RID: 10096 RVA: 0x000197F6 File Offset: 0x000179F6
        public static bool operator !=(OrderID _id, OrderID _other) {
            return _id.m_id != _other.m_id;
        }

        // Token: 0x06002771 RID: 10097 RVA: 0x0001980B File Offset: 0x00017A0B
        public void Serialise(BitStreamWriter writer) {
            writer.Write(this.m_id, 8);
        }

        // Token: 0x06002772 RID: 10098 RVA: 0x0001981A File Offset: 0x00017A1A
        public bool Deserialise(BitStreamReader reader) {
            this.m_id = reader.ReadUInt32(8);
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is OrderID iD &&
                   m_id == iD.m_id;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(m_id);
        }

        // Token: 0x04001FE6 RID: 8166
        private const int kBitsPerID = 8;

        // Token: 0x04001FE7 RID: 8167
        public uint m_id;
    }
}