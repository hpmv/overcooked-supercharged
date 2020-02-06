using System;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009CE RID: 2510
    public interface Serialisable {
        // Token: 0x06002FCA RID: 12234
        void Serialise(BitStreamWriter writer);

        // Token: 0x06002FCB RID: 12235
        bool Deserialise(BitStreamReader reader);
    }
}