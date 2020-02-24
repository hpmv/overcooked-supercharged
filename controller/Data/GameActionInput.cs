using System.Collections.Generic;

namespace Hpmv {

    public struct GameActionInput {
        public GameEntityRecords Entities;
        public int Frame;
        public int FrameWithinAction;
        public GameMap Map;
        public ControllerState ControllerState;
    }
}