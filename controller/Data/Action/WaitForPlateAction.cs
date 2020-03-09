using System.Linq;

namespace Hpmv {
    public class WaitForCleanPlateAction : GameAction {
        public GameEntityRecord DryingPart { get; set; }

        public override string Describe() {
            return $"Wait for clean plate";
        }

        public override GameActionOutput Step(GameActionInput input) {
            foreach (var stack in DryingPart.spawned) {
                foreach (var plate in stack.spawned) {
                    if (plate.existed[input.Frame] && plate.spawnOwner[input.Frame] == -1) {
                        return new GameActionOutput {
                            SpawningClaim = plate,
                            Done = true
                        };
                    }
                }
            }
            return default;
        }

        public new Save.WaitForCleanPlateAction ToProto() {
            return new Save.WaitForCleanPlateAction {
                DryingPart = DryingPart.path.ToProto()
            };
        }
    }

    public static class WaitForCleanPlateActionFromProto {
        public static WaitForCleanPlateAction FromProto(this Save.WaitForCleanPlateAction action, LoadContext context) {
            return new WaitForCleanPlateAction { DryingPart = action.DryingPart.FromProtoRef(context) };
        }
    }

    public class WaitForDirtyPlateAction : GameAction {
        public GameEntityRecord DirtyPlateSpawner { get; set; }
        public int Count { get; set; }

        public override string Describe() {
            return $"Wait for {Count} dirty stack";
        }

        public override GameActionOutput Step(GameActionInput input) {
            foreach (var stack in DirtyPlateSpawner.spawned) {
                if (stack.existed[input.Frame] && stack.spawnOwner[input.Frame] == -1 && stack.spawned.Count(s => s.existed[input.Frame]) >= Count) {
                    return new GameActionOutput {
                        SpawningClaim = stack,
                        Done = true
                    };
                }
            }
            return default;
        }

        public new Save.WaitForDirtyPlateAction ToProto() {
            return new Save.WaitForDirtyPlateAction {
                DirtyPlateSpawner = DirtyPlateSpawner.path.ToProto(),
                Count = Count
            };
        }
    }

    public static class WaitForDirtyPlateActionFromProto {
        public static WaitForDirtyPlateAction FromProto(this Save.WaitForDirtyPlateAction action, LoadContext context) {
            return new WaitForDirtyPlateAction { DirtyPlateSpawner = action.DirtyPlateSpawner.FromProtoRef(context), Count = action.Count };
        }
    }
}
