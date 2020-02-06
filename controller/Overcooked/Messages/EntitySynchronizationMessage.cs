using System;
using System.Collections.Generic;
using BitStream;
using Hpmv;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A9 RID: 2473
    public class EntitySynchronisationMessage : Serialisable {

        // Token: 0x06002F46 RID: 12102 RVA: 0x000D2E14 File Offset: 0x000D1014
        public void Serialise(BitStreamWriter writer) {
            throw new NotImplementedException();
        }

        // Token: 0x06002F47 RID: 12103 RVA: 0x000D2E84 File Offset: 0x000D1084
        public bool Deserialise(BitStreamReader reader) {
            this.m_Payloads.Clear();
            if (this.m_Header.Deserialise(reader)) {
                var entry = FakeEntityRegistry.entityToTypes.GetValueOrDefault((int) this.m_Header.m_uEntityID);
                if (entry != null) {
                    for (int i = 0; i < entry.Count; i++) {
                        bool flag = reader.ReadBit();
                        if (flag) {
                            Serialisable item;
                            SerialisationRegistry<EntityType>.Deserialise(out item, entry[i], reader);
                            this.m_Payloads.Add((entry[i], item));
                        } else {
                            this.m_Payloads.Add((entry[i], null));
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        // Token: 0x04002771 RID: 10097
        public EntityMessageHeader m_Header = new EntityMessageHeader();

        // Token: 0x04002772 RID: 10098
        public List < (EntityType, Serialisable) > m_Payloads = new List < (EntityType, Serialisable) > ();
    }
}