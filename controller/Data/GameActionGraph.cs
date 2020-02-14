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

        public GameActionGraphBuilder Builder(GameMap map) {
            return new GameActionGraphBuilder(this, map, -1, new List<int>());
        }

        public GameActionGraphState NewState() {
            var state = new GameActionGraphState();
            for (int i = 0; i < actions.Count; i++) {
                var oneState = new GameActionState();
                oneState.ActionId = i;
                actions[i].InitializeState(oneState);
                state.States.Add(oneState);
            }
            return state;
        }
    }

    public class GameActionGraphState {
        public List<GameActionState> States = new List<GameActionState>();
    }
}