using System.Collections.Generic;

namespace Hpmv {
    public struct GameActionOutput {
        public bool Done { get; set; }
        public DesiredControllerInput ControllerInput { get; set; }
        public GameEntityRecord SpawningClaim { get; set; }
    }

    public abstract class GameAction {
        public GameEntityRecord Chef { get; set; }
        public abstract string Describe();
        public abstract GameActionOutput Step(GameActionInput input);
    }
}