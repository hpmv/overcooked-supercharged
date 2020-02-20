using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {

    public interface IEntityReference {
        GameEntityRecord GetEntityRecord(GameActionInput input);
    }

    public class LiteralEntityReference : IEntityReference {
        public GameEntityRecord Record;

        public LiteralEntityReference(GameEntityRecord record) {
            Record = record;
        }

        public GameEntityRecord GetEntityRecord(GameActionInput input) {
            return Record;
        }
    }

    public interface ISpawnClaimingAction {
        IEntityReference GetSpawner();

    }

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
    }
}