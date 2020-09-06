using System;
using System.Collections.Generic;
using BitStream;
using Hpmv;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A5 RID: 2469
    public class EntityEventMessage : Serialisable {
        // Token: 0x06002F3B RID: 12091 RVA: 0x0001EB21 File Offset: 0x0001CD21
        public void Initialise(EntityMessageHeader header, uint uComponentId, Serialisable payload) {
            this.m_Header = header;
            this.m_ComponentId = uComponentId;
            this.m_Payload = payload;
        }

        // Token: 0x06002F3C RID: 12092 RVA: 0x0001EB38 File Offset: 0x0001CD38
        public void Serialise(BitStreamWriter writer) {
            this.m_Header.Serialise(writer);
            writer.Write((byte) this.m_ComponentId, 4);
            this.m_Payload.Serialise(writer);
        }

        // Token: 0x06002F3D RID: 12093 RVA: 0x000D2DA8 File Offset: 0x000D0FA8
        public bool Deserialise(BitStreamReader reader) {
            if (!this.m_Header.Deserialise(reader)) {
                return false;
            }
            this.m_ComponentId = (uint) reader.ReadByte(4);
            var entry = FakeEntityRegistry.entityToTypes.GetValueOrDefault((int) this.m_Header.m_uEntityID);
            if (entry != null) {
                if ((int)this.m_ComponentId >= entry.Count || (int)this.m_ComponentId < 0) {
                    Console.WriteLine(
                        $"Unable to deserialize EntityEventMessage for entity {this.m_Header.m_uEntityID} component {this.m_ComponentId}");
                        return false;
                }
                m_EntityType = entry[(int) this.m_ComponentId];
                return SerialisationRegistry<EntityType>.Deserialise(out this.m_Payload, entry[(int) this.m_ComponentId], reader);
            }
            return false;
        }

        // Token: 0x0400272B RID: 10027
        public EntityMessageHeader m_Header = new EntityMessageHeader();

        // Token: 0x0400272C RID: 10028
        public uint m_ComponentId;

        // Token: 0x0400272D RID: 10029
        public Serialisable m_Payload;

        public EntityType m_EntityType;
    }
}