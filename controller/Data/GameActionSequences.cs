using System;
using System.Collections.Generic;
using System.Linq;

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
        public Dictionary<GameEntityRecord, int> ChefIndexByChef = new Dictionary<GameEntityRecord, int>();
        public Dictionary<int, GameActionNode> NodeById = new Dictionary<int, GameActionNode>();
        public int NextId = 1;

        public void AddChef(GameEntityRecord chef) {
            ChefIndexByChef[chef] = Chefs.Count;
            Chefs.Add(chef);
            Actions.Add(new List<GameActionNode>());
        }

        [Obsolete]
        public int AddAction(GameEntityRecord chef, GameAction action, List<int> deps) {
            var chefIndex = ChefIndexByChef[chef];
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

        public void CleanTimingsFromFrame(int frame) {
            foreach (var actions in Actions) {
                foreach (var action in actions) {
                    if (action.Predictions.StartFrame != null && action.Predictions.StartFrame > frame) {
                        action.Predictions.StartFrame = null;
                    }
                    if (action.Predictions.EndFrame != null && action.Predictions.EndFrame > frame) {
                        action.Predictions.EndFrame = null;
                    }
                }
            }
        }

        public void FillSaneFutureTimings(int lastSimulatedFrame) {
            foreach (var actions in Actions) {
                var lastFrame = 0;
                foreach (var action in actions) {
                    if (action.Predictions.StartFrame == null) {
                        action.Predictions.StartFrame = lastFrame;
                    }
                    lastFrame = action.Predictions.StartFrame ?? 0;

                    if (action.Predictions.StartFrame <= lastSimulatedFrame && action.Predictions.EndFrame == null) {
                        action.Predictions.EndFrame = lastSimulatedFrame + 1;
                    }
                    if (action.Predictions.EndFrame == null) {
                        action.Predictions.EndFrame = lastFrame;
                    }
                    lastFrame = action.Predictions.EndFrame ?? 0;
                }
            }

        }

        public void DeleteAction(GameEntityRecord chef, int index) {
            var action = Actions[ChefIndexByChef[chef]][index];
            Actions[ChefIndexByChef[chef]].RemoveAt(index);
            NodeById.Remove(action.Id);
            foreach (var actions in Actions) {
                foreach (var other in actions) {
                    other.Deps.Remove(action.Id);
                }
            }
        }

        public int InsertAction(GameEntityRecord chef, int beforeIndex, GameAction action) {
            var chefIndex = ChefIndexByChef[chef];
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
            foreach (var actions in Actions) {
                foreach (var action in actions) {
                    graph.AddAction(action.Action);
                }
            }
            foreach (var actions in Actions) {
                GameActionNode prevAction = null;
                foreach (var action in actions) {
                    foreach (var dep in action.Deps) {
                        graph.AddDep(dep, action.Id);
                    }
                    if (prevAction != null) {
                        graph.AddDep(prevAction.Id, action.Id);
                    }
                    prevAction = action;
                }
            }
            return graph;
        }

    }
}