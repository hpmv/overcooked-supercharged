using System.Collections.Generic;

namespace Hpmv {

    public struct GameActionInput {
        public GameEntityRecords Entities;
        public int Frame;
        public int FrameWithinAction;
        public GameMapGeometry Geometry;  // no path finding
        public Dictionary<int, GameMap> MapByChef;
        public ControllerState ControllerState;
    }
}