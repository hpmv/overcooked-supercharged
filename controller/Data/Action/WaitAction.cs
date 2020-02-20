namespace Hpmv {
    class WaitAction : GameAction {
        public int NumFrames { get; set; }

        public override string Describe() {
            return $"Wait {NumFrames} frames";
        }

        public override GameActionOutput Step(GameActionInput input) {
            if (input.FrameWithinAction < NumFrames) {
                return new GameActionOutput {
                    Done = true
                };
            }
            return default;
        }
    }
}