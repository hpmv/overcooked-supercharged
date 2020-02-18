using System.Collections.Generic;

namespace Hpmv {
    public class GameActionNode {
        public int Id { get; set; }
        public GameAction Action { get; set; }
        public List<int> Deps { get; set; }
        public GameActionPredictions Predictions { get; set; }
    }

    public class GameActionSequences {
        public List<List<GameActionNode>> Actions = new List<List<GameActionNode>>();
        public List<int> Chefs = new List<int>();
        public Dictionary<int, int> ChefIndexById = new Dictionary<int, int>();
        public Dictionary<int, GameActionNode> NodeById = new Dictionary<int, GameActionNode>();
        public int NextId = 1;

        public void AddChef(int id) {
            ChefIndexById[id] = Chefs.Count;
            Chefs.Add(id);
            Actions.Add(new List<GameActionNode>());
        }

        public int AddAction(int chef, GameAction action, List<int> deps) {
            var chefIndex = ChefIndexById[chef];
            var id = NextId++;
            var node = new GameActionNode {
                Id = id,
                Action = action,
                Deps = new List<int>(deps),
                Predictions = new GameActionPredictions { }
            };
            Actions[chefIndex].Add(node);
            NodeById[id] = node;
            return id;
        }

        public GameActionGraph ToGraph() {
            GameActionGraph graph = new GameActionGraph();
            for (int i = 0; i < NextId; i++) {
                if (!NodeById.ContainsKey(i)) {
                    graph.AddAction(null);
                } else {
                    var node = NodeById[i];
                    graph.AddAction(node.Action, node.Deps.ToArray());
                }
            }
            return graph;
        }

    }
}