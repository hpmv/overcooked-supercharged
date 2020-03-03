namespace Hpmv {
    class WaitForSpawnAction : GameAction, ISpawnClaimingAction {
        public IEntityReference Spawner;
        public string Label { get; set; }

        public override string Describe() {
            return $"Spawn {Label} from {Spawner}";
        }

        public IEntityReference GetSpawner() {
            return Spawner;
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Spawner.GetEntityRecord(input);
            foreach (var child in entity.spawned) {
                if (child.existed[input.Frame] && child.spawnOwner[input.Frame] == null) {
                    return new GameActionOutput {
                        SpawningClaim = child,
                        Done = true
                    };
                }
            }
            return new GameActionOutput();
        }
    }
}