using System.Collections.Generic;

namespace Hpmv {
    class WaitAction : GameAction {
        public int NumFrames { get; set; }

        public override string Describe() {
            return $"Wait {NumFrames} frames";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            for (int i = 0; i < NumFrames; i++) {
                yield return null;
            }
        }
    }
}