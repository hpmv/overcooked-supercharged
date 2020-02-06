using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace Hpmv {
    class GameEntity {
        public int entityId;

        public Dictionary<EntityType, Serialisable> data = new Dictionary<EntityType, Serialisable>();

        public Vector3 pos = Vector3.zero;
        public Quaternion orientation = new Quaternion(0, 0, 0, 0);
        public string name = "(unknown)";
    }

    class GameEntities {
        public Dictionary<int, GameEntity> entities = new Dictionary<int, GameEntity>();
    }
}