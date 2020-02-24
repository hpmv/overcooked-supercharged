using System.Collections.Generic;

namespace Hpmv {

    public abstract class GameAction {
        public int ActionId { get; set; }
        public GameEntityRecord Chef { get; set; }
        public abstract string Describe();
        public abstract GameActionOutput Step(GameActionInput input);

        public override string ToString() {
            return $"Action#{ActionId}";
        }
    }
}