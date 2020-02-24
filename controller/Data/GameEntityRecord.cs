using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {

    public class GameEntityRecord {
        public EntityPath path;
        public string displayName = "";
        public string className = "";
        public PrefabRecord prefab = null;
        public GameEntityRecord spawner = null;
        public List<GameEntityRecord> spawned = new List<GameEntityRecord>();
        public Versioned<int> nextSpawnId = new Versioned<int>(0);
        public Versioned<ISpawnClaimingAction> spawnOwner = new Versioned<ISpawnClaimingAction>(null);
        public Versioned<Vector3> position;
        public Versioned<bool> existed;
        public Versioned<SpecificEntityData> data = new Versioned<SpecificEntityData>(new SpecificEntityData());
        public Versioned<ChefState> chefState = null;

        public override string ToString() {
            return displayName;
        }

        public IEnumerable<GameEntityRecord> GenAllEntities() {
            yield return this;
            foreach (var spawn in spawned) {
                foreach (var record in spawn.GenAllEntities()) {
                    yield return record;
                }
            }
        }

        public bool IsChef() {
            return chefState != null;
        }

        public IEntityReference ReverseEngineerStableEntityReference(int frame) {
            if (spawner == null) {
                return new LiteralEntityReference(this);
            }
            if (spawnOwner[frame] == null) {
                return null;
            }
            return new SpawnedEntityReference(spawnOwner[frame]);
        }
    }

    public struct SpecificEntityData {
        public GameEntityRecord attachmentParent;
        public GameEntityRecord carriedItem;
        public double progress;
        public List<int> contents;
    }

    public struct ChefState {
        public Vector2 forward;
        public GameEntityRecord highlightedForPickup;
        public GameEntityRecord highlightedForUse;
        public GameEntityRecord highlightedForPlacement;
        public double dashTimer;
    }
}