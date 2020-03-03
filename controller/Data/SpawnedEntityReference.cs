using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {

    public class SpawnedEntityReference : IEntityReference {
        public ISpawnClaimingAction Spawner;

        public SpawnedEntityReference(ISpawnClaimingAction spawner) {
            Spawner = spawner;
        }

        public GameEntityRecord GetEntityRecord(GameActionInput input) {
            var spawnerEntity = Spawner.GetSpawner().GetEntityRecord(input);
            foreach (var child in spawnerEntity.spawned) {
                if (child.spawnOwner[input.Frame] == Spawner) {
                    return child;
                }
            }
            return null;
        }

        public PrefabRecord GetPrefabRecord() {
            var prefab = Spawner.GetSpawner().GetPrefabRecord();
            if (prefab != null && prefab.Spawns.Count > 0) {
                return prefab.Spawns[0];
            }
            return null;
        }

        public override string ToString() {
            if (GetPrefabRecord() is PrefabRecord rec) {
                return rec.Name;
            }
            return $"Spawn of '{Spawner}'";
        }

        public Save.EntityReference ToProto() {
            return new Save.EntityReference {
                Spawner = (Spawner as GameAction).ActionId
            };
        }
    }
}