using System.Collections.Generic;

namespace Hpmv {
    public class GameActionGraph {
        public Dictionary<int, GameAction> actions = new Dictionary<int, GameAction>();
        public Dictionary<int, List<int>> deps = new Dictionary<int, List<int>>();
        public Dictionary<int, List<int>> fwds = new Dictionary<int, List<int>>();

        public void AddAction(GameAction action) {
            actions[action.ActionId] = action;
            this.deps[action.ActionId] = new List<int>();
            fwds[action.ActionId] = new List<int>();
        }

        public void AddDep(int early, int late) {
            deps[late].Add(early);
            fwds[early].Add(late);
        }
    }
}