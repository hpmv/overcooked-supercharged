using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class GameEntity {
        public int entityId;
        public int spawnSourceEntityId = -1;
        public bool spawnClaimed = false;

        public Dictionary<EntityType, Serialisable> data = new Dictionary<EntityType, Serialisable>();

        public Vector3 pos = Vector3.Zero;
        public Quaternion orientation = Quaternion.Identity;
        public string name = "(unknown)";
    }

    public class ChefState {
        public Vector2 forward;
        public int highlightedForPickup = -1;
        public int highlightedForUse = -1;
        public int highlightedForPlacement = -1;
        public double dashTimer = 0;
    }

    public class GameEntities {
        public Dictionary<int, GameEntity> entities = new Dictionary<int, GameEntity>();
        public Dictionary<int, ChefState> chefs = new Dictionary<int, ChefState>();

        public GameEntity EntityOrCreate(int id) {
            return entities.ComputeIfAbsent(id, () => new GameEntity() { entityId = id });
        }
    }
}