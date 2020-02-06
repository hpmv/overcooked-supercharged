using System;
using System.Collections.Generic;
using BitStream;

namespace Team17.Online.Multiplayer.Messaging {
	// Token: 0x020008C6 RID: 2246
	public class SerialisationRegistry<T> {

		// Token: 0x06002BAD RID: 11181 RVA: 0x000CBA92 File Offset: 0x000C9E92
		public static void RegisterMessageType(T type, Func<Serialisable> factory) {
			SerialisationRegistry<T>.factories[type] = factory;
		}

		// Token: 0x06002BAE RID: 11182 RVA: 0x000CBAA0 File Offset: 0x000C9EA0
		public static bool Deserialise(out Serialisable message, T type, BitStreamReader reader) {
			if (SerialisationRegistry<T>.factories.ContainsKey(type)) {
				message = SerialisationRegistry<T>.factories[type]();
				return message.Deserialise(reader);
			}
			message = null;
			return false;
		}

		// Token: 0x040022E6 RID: 8934
		public static Dictionary<T, Func<Serialisable>> factories = new Dictionary<T, Func<Serialisable>>();
	}
}