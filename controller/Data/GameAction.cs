using System.Collections.Generic;

namespace Hpmv {
    public abstract class GameAction {
        public string DiagInfo { get; set; } = "";
        public int Chef { get; set; } = -1;
        public abstract string Describe();
        public abstract void InitializeState(GameActionState state);
        public abstract IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context);
    }
}