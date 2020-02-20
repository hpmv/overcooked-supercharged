using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class EntityPath {
        public int id;
        public EntityPath parent;
    }

    public class GameEntityRecord {
        public EntityPath path;
        public string displayName = "";
        public GameEntityRecord spawner = null;
        public List<GameEntityRecord> spawned = new List<GameEntityRecord>();
        public Versioned<ISpawnClaimingAction> spawnOwner = new Versioned<ISpawnClaimingAction>(null);
        public Versioned<Vector3> position = new Versioned<Vector3>(Vector3.Zero);
        public Versioned<bool> existed = new Versioned<bool>(false);
        public Versioned<SpecificEntityData> data = new Versioned<SpecificEntityData>(new SpecificEntityData());
        public Versioned<ChefState> chefState = null;
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

    public class GameEntityRecords {
        public readonly List<GameEntityRecord> FixedEntities = new List<GameEntityRecord>();
        public readonly Dictionary<GameEntityRecord, Versioned<ControllerState>> Chefs = new Dictionary<GameEntityRecord, Versioned<ControllerState>>();

        public GameEntityRecord RegisterKnownObject(string name, int entityId, Vector2 pos) {
            var record = new GameEntityRecord {
                displayName = name,
                path = new EntityPath { id = entityId }
            };
            record.position.ChangeTo(pos.ToXZVector3(), 0);
            record.existed.ChangeTo(true, 0);
            FixedEntities[entityId] = record;
            return record;
        }

        public GameEntityRecord RegisterKnownObject(string name, int entityId, Vector3 pos) {
            var record = new GameEntityRecord {
                displayName = name,
                path = new EntityPath { id = entityId }
            };
            record.position.ChangeTo(pos, 0);
            record.existed.ChangeTo(true, 0);
            FixedEntities[entityId] = record;
            return record;
        }

        public GameEntityRecord RegisterChef(string name, int entityId, Vector2 pos) {
            var record = new GameEntityRecord {
                displayName = name,
                path = new EntityPath { id = entityId }
            };
            record.position.ChangeTo(pos.ToXZVector3(), 0);
            record.existed.ChangeTo(true, 0);
            record.chefState = new Versioned<ChefState>(new ChefState { forward = -Vector2.UnitY });
            FixedEntities[entityId] = record;
            Chefs[record] = new Versioned<ControllerState>(new ControllerState());
            return record;
        }

        private void CleanRecordsFromFrame(GameEntityRecord record, int frame) {
            record.spawnOwner.RemoveAllFrom(frame);
            record.position.RemoveAllFrom(frame);
            record.existed.RemoveAllFrom(frame);
            record.data.RemoveAllFrom(frame);
            record.chefState.RemoveAllFrom(frame);
            foreach (var spawned in record.spawned) {
                CleanRecordsFromFrame(spawned, frame);
            }
        }


        public void CleanRecordsFromFrame(int frame) {
            foreach (var entity in FixedEntities) {
                CleanRecordsFromFrame(entity, frame);
            }
            foreach (var state in Chefs) {
                state.Value.RemoveAllFrom(frame);
            }
        }
    }
}