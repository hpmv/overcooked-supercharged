namespace Hpmv {
    public class WaitForProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double Progress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} progress {Progress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            if (entity != null) {
                if (entity.progress[input.Frame] >= Progress) {
                    return new GameActionOutput {
                        Done = true,
                    };
                }
            }
            return default;
        }

        public Save.WaitForProgressAction ToProto() {
            return new Save.WaitForProgressAction {
                Entity = Entity.ToProto(),
                Progress = Progress
            };
        }
    }

    public static class WaitForProgressActionFromProto {
        public static WaitForProgressAction FromProto(this Save.WaitForProgressAction action, LoadContext context) {
            return new WaitForProgressAction {
                Entity = action.Entity.FromProto(context),
                Progress = action.Progress
            };
        }
    }
}