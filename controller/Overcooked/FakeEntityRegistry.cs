using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    class FakeEntityRegistry {
        public static Dictionary<int, List<EntityType>> entityToTypes = new Dictionary<int, List<EntityType>>();
    }
}