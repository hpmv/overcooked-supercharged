using System.Collections.Generic;

namespace Hpmv {
    public class GameScaleAction : GameAction {
        public double Scale { get; set; } = 1;
        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.Description = $"Set game speed to {Scale}";
        }

        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            yield return new ControllerInput { GameSpeed = Scale };
        }
    }
}