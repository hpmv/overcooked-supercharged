namespace Hpmv {
    class WaitForCleanPlateAction : GameAction, ISpawnClaimingAction {
        class InnerHelper : ISpawnClaimingAction {
            IEntityReference DryingPart;

            public InnerHelper(GameEntityRecord dryingPart) {
                DryingPart = new LiteralEntityReference(dryingPart);
            }

            public IEntityReference GetSpawner() {
                return DryingPart;
            }
        }

        public readonly GameEntityRecord DryingPart;
        private readonly InnerHelper helper;
        private readonly IEntityReference plateStack;


        public WaitForCleanPlateAction(GameEntityRecord dryingPart) {
            DryingPart = dryingPart;
            helper = new InnerHelper(dryingPart);
            plateStack = new SpawnedEntityReference(helper);
        }

        public override string Describe() {
            return $"Wait for clean plate";
        }

        public IEntityReference GetSpawner() {
            return plateStack;
        }

        public override GameActionOutput Step(GameActionInput input) {
            foreach (var stack in DryingPart.spawned) {
                foreach (var plate in stack.spawned) {
                    if (plate.spawnOwner[input.Frame] == null) {
                        return new GameActionOutput {
                            SpawningClaim = plate,
                            Done = true
                        };
                    }
                }
            }
            return default;
        }
    }

    class WaitForDirtyPlateAction : GameAction, ISpawnClaimingAction {
        public readonly GameEntityRecord DirtyPlateSpawner;
        private readonly IEntityReference spawner;
        public readonly int Count;

        public WaitForDirtyPlateAction(GameEntityRecord dirtyPlateSpawner, int count) {
            DirtyPlateSpawner = dirtyPlateSpawner;
            Count = count;
            this.spawner = new LiteralEntityReference(dirtyPlateSpawner);
        }

        public override string Describe() {
            return $"Wait for {Count} dirty stack";
        }

        public IEntityReference GetSpawner() {
            return spawner;
        }

        public override GameActionOutput Step(GameActionInput input) {
            foreach (var stack in DirtyPlateSpawner.spawned) {
                if (stack.spawnOwner[input.Frame] == null && stack.spawned.Count == Count) {
                    return new GameActionOutput {
                        SpawningClaim = stack,
                        Done = true
                    };
                }
            }
            return default;
        }
    }
}