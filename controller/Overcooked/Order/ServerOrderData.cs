using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

namespace OrderController {
    // Token: 0x0200081B RID: 2075
    public class ServerOrderData : Serialisable {
        // Token: 0x06002773 RID: 10099 RVA: 0x0001982A File Offset: 0x00017A2A
        public ServerOrderData() { }

        // Token: 0x06002774 RID: 10100 RVA: 0x0001983D File Offset: 0x00017A3D
        public ServerOrderData(OrderID _id, RecipeList.Entry _entry, float _lifetime) {
            this.ID = _id;
            this.RecipeListEntry = _entry;
            this.Lifetime = _lifetime;
            this.Remaining = this.Lifetime;
        }

        // Token: 0x06002775 RID: 10101 RVA: 0x00019871 File Offset: 0x00017A71
        public void Serialise(BitStreamWriter writer) {
            this.ID.Serialise(writer);
            this.RecipeListEntry.Serialise(writer);
            writer.Write(this.Lifetime);
            writer.Write(this.Remaining);
        }

        // Token: 0x06002776 RID: 10102 RVA: 0x000198A3 File Offset: 0x00017AA3
        public bool Deserialise(BitStreamReader reader) {
            this.ID.Deserialise(reader);
            if (this.RecipeListEntry.Deserialise(reader)) {
                this.Lifetime = reader.ReadFloat32();
                this.Remaining = reader.ReadFloat32();
                return true;
            }
            return false;
        }

        // Token: 0x04001FE8 RID: 8168
        public OrderID ID;

        // Token: 0x04001FE9 RID: 8169
        public RecipeList.Entry RecipeListEntry = new RecipeList.Entry();

        // Token: 0x04001FEA RID: 8170
        public float Lifetime;

        // Token: 0x04001FEB RID: 8171
        public float Remaining;
    }
}