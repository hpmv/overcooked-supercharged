using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    // This is a lightweight placeholder for the game's EntitySerialisationRegistry class.
    // The game's message serialization and deserialization code relies on static data in the EntitySerialisationRegistry,
    // and it's impractical to refactor the code to not rely on such static data. Instead, we have
    // this fake registry and we synchronize the enties in EntitySerialisationRegistry to the fake registry.
    class FakeEntityRegistry {
        public static Dictionary<int, List<EntityType>> entityToTypes = new Dictionary<int, List<EntityType>>();
    }
}