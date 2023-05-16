using System.Collections.Generic;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public static class BitStreamHelpers {
        public static byte[] ToBytes(this Serialisable serialisable) {
            var bytes = new FastList<byte>();
            var writer = new BitStreamWriter(bytes);
            serialisable.Serialise(writer);
            return bytes.ToArray();
        }

        public static T FromBytes<T>(this T obj, byte[] bytes) where T : Serialisable {
            var reader = new BitStreamReader(bytes);
            obj.Deserialise(reader);
            return obj;
        }
    }
}