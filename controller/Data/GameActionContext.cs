using System.Collections.Generic;

namespace Hpmv {
    public class GameActionContext {
        public GameEntities Entities;
        public GameMap Map;
        public Dictionary<int, int> EntityIdReturnValueForAction = new Dictionary<int, int>();
        public Dictionary<int, Humanizer> ChefHumanizers = new Dictionary<int, Humanizer>();
        public FramerateController FramerateController;
    }
}