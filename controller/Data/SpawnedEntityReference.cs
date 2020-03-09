using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {

    public class SpawnedEntityReference : IEntityReference {
        public int Claimer { get; set; }

        public SpawnedEntityReference(int claimer) {
            Claimer = claimer;
        }

        public GameEntityRecord GetEntityRecord(GameActionInput input) {
            foreach (var child in input.Entities.GenAllEntities()) {
                if (child.spawnOwner[input.Frame] == Claimer) {
                    return child;
                }
            }
            return null;
        }

        // public PrefabRecord GetPrefabRecord() {
        //     var prefab = Spawner.GetSpawner().GetPrefabRecord();
        //     if (prefab != null && prefab.Spawns.Count > 0) {
        //         return prefab.Spawns[0];
        //     }
        //     return null;
        // }

        public override string ToString() {
            // if (GetPrefabRecord() is PrefabRecord rec) {
            //     return rec.Name;
            // }
            // TODO - make this better.
            return $"Object claimed by {Claimer}";
        }

        public Save.EntityReference ToProto() {
            return new Save.EntityReference {
                Spawner = Claimer
            };
        }
    }
}