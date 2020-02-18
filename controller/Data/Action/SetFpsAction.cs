using System;
using System.Collections.Generic;

namespace Hpmv {
    public class SetFpsAction : GameAction {
        public int Fps { get; set; } = 1000;

        public override string Describe() {
            return $"Set FPS to ${Fps}";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            context.FramerateController.Delay = TimeSpan.FromMilliseconds(1000.0 / Fps);
            yield break;
        }
    }
}