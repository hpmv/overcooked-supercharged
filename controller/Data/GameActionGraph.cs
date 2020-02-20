using System.Collections.Generic;

namespace Hpmv {
    public class GameActionGraph {
        public List<GameAction> actions = new List<GameAction>();
        public List<List<int>> deps = new List<List<int>>();
        public List<List<int>> fwds = new List<List<int>>();

        public int AddAction(GameAction action, params int[] deps) {
            int id = actions.Count;
            actions.Add(action);
            this.deps.Add(new List<int>());
            fwds.Add(new List<int>());
            foreach (int dep in deps) {
                AddDep(dep, id);
            }
            return id;
        }

        public void AddDep(int early, int late) {
            deps[late].Add(early);
            fwds[early].Add(late);
        }
    }
}