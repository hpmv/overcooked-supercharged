namespace Hpmv {
    public class WaitForSpawnAction : GameAction {
        public IEntityReference Spawner;

        public override string Describe() {
            return $"Spawn from {Spawner}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Spawner.GetEntityRecord(input);
            if (entity != null) {
                foreach (var child in entity.spawned) {
                    if (child.existed[input.Frame] && child.spawnOwner[input.Frame] == -1) {
                        return new GameActionOutput {
                            SpawningClaim = child,
                            Done = true
                        };
                    }
                }
            }
            return new GameActionOutput();
        }

        public new Save.WaitForSpawnAction ToProto() {
            return new Save.WaitForSpawnAction {
                Spawner = Spawner.ToProto(),
            };
        }
    }

    public static class WaitForSpawnActionFromProto {
        public static WaitForSpawnAction FromProto(this Save.WaitForSpawnAction action, LoadContext context) {
            return new WaitForSpawnAction {
                Spawner = action.Spawner.FromProto(context),
            };
        }
    }
}