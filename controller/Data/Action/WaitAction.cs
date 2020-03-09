namespace Hpmv {
    public class WaitAction : GameAction {
        public int NumFrames { get; set; }

        public override string Describe() {
            return $"Wait {NumFrames} frames";
        }

        public override GameActionOutput Step(GameActionInput input) {
            if (input.FrameWithinAction >= NumFrames) {
                return new GameActionOutput {
                    Done = true
                };
            }
            return default;
        }

        public Save.WaitAction ToProto() {
            return new Save.WaitAction {
                NumFrames = NumFrames
            };
        }
    }

    public static class WaitActionFromProto {
        public static WaitAction FromProto(this Save.WaitAction action) {
            return new WaitAction {
                NumFrames = action.NumFrames
            };
        }
    }
}