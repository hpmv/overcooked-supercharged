using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GameEntityRecords {
        public readonly List<GameEntityRecord> FixedEntities = new List<GameEntityRecord>();
        public readonly Dictionary<GameEntityRecord, Versioned<ControllerState>> Chefs = new Dictionary<GameEntityRecord, Versioned<ControllerState>>();
        public readonly Dictionary<int, Vector3> CapturedInitialPositions = new Dictionary<int, Vector3>();

        public IEnumerable<GameEntityRecord> GenAllEntities() {
            foreach (var record in FixedEntities) {
                foreach (var entity in record.GenAllEntities()) {
                    yield return entity;
                }
            }
        }

        public GameEntityRecord GetRecordFromPath(int[] path) {
            List<GameEntityRecord> rootEntitiesById = new List<GameEntityRecord>();
            foreach (var entity in FixedEntities) {
                var id = entity.path.ids[0];
                while (rootEntitiesById.Count <= id) {
                    rootEntitiesById.Add(null);
                }
                rootEntitiesById[id] = entity;
            }
            var list = rootEntitiesById;
            GameEntityRecord item = null;
            foreach (int index in path) {
                item = list[index];
                list = item.spawned;
            }
            return item;
        }

        public GameEntityRecord RegisterKnownObject(int entityId) {
            var prefab = new PrefabRecord("", "unknown") { IsAttachStation = true };
            var pos = CapturedInitialPositions[entityId];
            CapturedInitialPositions.Remove(entityId);
            return RegisterKnownObject(prefab.Name, prefab.ClassName, entityId, pos, prefab);
        }

        public GameEntityRecord RegisterKnownObject(int entityId, PrefabRecord prefab) {
            var name = prefab.Name;
            var className = prefab.ClassName;
            var pos = CapturedInitialPositions[entityId];
            CapturedInitialPositions.Remove(entityId);
            return RegisterKnownObject(name, className, entityId, pos, prefab);
        }

        private GameEntityRecord RegisterKnownObject(string name, string className, int entityId, Vector3 pos, PrefabRecord prefab) {
            var record = new GameEntityRecord {
                displayName = name,
                className = className,
                prefab = prefab,
                path = new EntityPath { ids = new[] { entityId } },
                position = new Versioned<Vector3>(pos),
                existed = new Versioned<bool>(true)
            };
            FixedEntities.Add(record);
            return record;
        }

        public void AttachInitialObjectTo(GameEntityRecord child, GameEntityRecord parent) {
            // Using -1 is not the best thing to do but this is convenient because attachment/attachmentParent would produce
            // a circular reference if we were to use constructor initialization.
            child.data.AppendWith(-1, d => {
                d.attachmentParent = parent;
                return d;
            });
            parent.data.AppendWith(-1, d => {
                d.attachment = child;
                return d;
            });
        }

        public void RegisterOtherInitialObjects() {
            foreach (var entry in CapturedInitialPositions) {
                RegisterKnownObject(entry.Key);
            }
        }

        public GameEntityRecord RegisterChef(string name, int entityId, PrefabRecord prefab) {
            var record = new GameEntityRecord {
                displayName = name,
                className = prefab.ClassName,
                prefab = prefab,
                path = new EntityPath { ids = new[] { entityId } },
                position = new Versioned<Vector3>(CapturedInitialPositions[entityId]),
                existed = new Versioned<bool>(true),
                chefState = new Versioned<ChefState>(new ChefState { forward = -Vector2.UnitY })
            };
            CapturedInitialPositions.Remove(entityId);
            FixedEntities.Add(record);
            Chefs[record] = new Versioned<ControllerState>(new ControllerState());
            return record;
        }

        public void CleanRecordsAfterFrame(int frame) {
            foreach (var entity in FixedEntities) {
                entity.CleanRecordsAfterFrameRecursively(frame);
            }
            foreach (var state in Chefs) {
                state.Value.RemoveAllAfter(frame);
            }
        }

        public void CalculateSpawningPathsForPrefabs() {
            foreach (var entity in FixedEntities) {
                if (entity.prefab != null) {
                    for (int i = 0; i < entity.prefab.Spawns.Count; i++) {
                        var spawn = entity.prefab.Spawns[i];
                        if (spawn.SpawningPath == null) {
                            spawn.SpawningPath = new SpawningPath{InitialFixedEntityId = entity.path.ids[0], SpawnableIds = {i}};
                            spawn.CalculateSpawningPathsForSpawnsRecursively();
                        }
                    }
                }
            }
        }

        public Save.GameEntityRecords ToProto() {
            var result = new Save.GameEntityRecords();
            foreach (var entity in GenAllEntities()) {
                result.AllRecords.Add(entity.ToProto());
            }
            foreach (var (chef, controller) in Chefs) {
                result.Controllers[chef.path.ids[0]] = controller.ToProto();
            }
            return result;
        }
    }
}