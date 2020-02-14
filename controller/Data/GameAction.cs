using System.Collections.Generic;

namespace Hpmv {
    public interface GameAction {
        void InitializeState(GameActionState state);
        IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context);
    }
}