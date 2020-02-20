namespace Hpmv {
    public class WaitForProgressAction : GameAction {
        public IEntityReference Entity { get; set; }
        public double Progress { get; set; }

        public override string Describe() {
            return $"Wait {Entity} progress {Progress} ";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var entity = Entity.GetEntityRecord(input);
            var specific = entity.data[input.Frame];
            if (specific.progress >= Progress) {
                return new GameActionOutput {
                    Done = true,
                };
            }
            return default;
        }
    }
}