using System;
using System.Collections.Generic;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A4 RID: 2468
    public class DestroyEntitiesMessage : Serialisable {

        // Token: 0x06002F39 RID: 12089 RVA: 0x000D2818 File Offset: 0x000D0A18
        public void Serialise(BitStreamWriter writer) {
            writer.Write(this.m_rootId, 10);
            writer.Write((uint) this.m_ids.Count, 5);
            for (int i = 0; i < this.m_ids.Count; i++) {
                writer.Write(this.m_ids[i], 10);
            }
        }

        // Token: 0x06002F3A RID: 12090 RVA: 0x000D2878 File Offset: 0x000D0A78
        public bool Deserialise(BitStreamReader reader) {
            this.m_rootId = reader.ReadUInt32(10);
            uint num = reader.ReadUInt32(5);
            this.m_ids.Clear();
            int num2 = 0;
            while ((long) num2 < (long) ((ulong) num)) {
                uint item = reader.ReadUInt32(10);
                this.m_ids.Add(item);
                num2++;
            }
            return true;
        }

        // Token: 0x04002727 RID: 10023
        public const int k_idCountBitCount = 5;

        // Token: 0x04002728 RID: 10024
        public const int k_idCapacity = 16;

        // Token: 0x04002729 RID: 10025
        public uint m_rootId;

        // Token: 0x0400272A RID: 10026
        public List<uint> m_ids = new List<uint>(16);
    }
}