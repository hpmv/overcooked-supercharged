using System;
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
        public List<GameEntityRecord> Chefs = new List<GameEntityRecord>();
        public Dictionary<GameEntityRecord, int> ChefIndexById = new Dictionary<GameEntityRecord, int>();
        public Dictionary<int, GameActionNode> NodeById = new Dictionary<int, GameActionNode>();
        public int NextId = 1;

        public void AddChef(GameEntityRecord chef) {
            ChefIndexById[chef] = Chefs.Count;
            Chefs.Add(chef);
            Actions.Add(new List<GameActionNode>());
        }

        [Obsolete]
        public int AddAction(GameEntityRecord chef, GameAction action, List<int> deps) {
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


        public int InsertAction(GameEntityRecord chef, int beforeIndex, GameAction action) {
            var chefIndex = ChefIndexById[chef];
            var id = NextId++;
            action.ActionId = id;
            action.Chef = chef;
            var node = new GameActionNode {
                Id = id,
                Action = action,
                Deps = new List<int>(),
                Predictions = new GameActionPredictions { }
            };
            Actions[chefIndex].Insert(beforeIndex, node);
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