using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv {
    public class GameActionNode {
        public int Id { get; set; }
        public GameAction Action { get; set; }
        public List<int> Deps { get; set; }
        public GameActionPredictions Predictions { get; set; }

        public Save.GameActionNode ToProto() {
            var proto = new Save.GameActionNode {
                ActionId = Id,
                Action = Action.ToProto(),
                ChefIndex = Action.Chef.path.ids[0],
                Predictions = Predictions.ToProto(),
            };
            proto.Deps.AddRange(Deps);
            return proto;
        }
    }

    public static class GameActionNodeFromProto {
        public static GameActionNode FromProto(this Save.GameActionNode node, LoadContext context) {
            var result = new GameActionNode {
                Id = node.ActionId,
                Deps = node.Deps.ToList(),
                Predictions = node.Predictions.FromProto(),
                Action = node.Action.FromProto(context)
            };
            result.Action.ActionId = node.ActionId;
            result.Action.Chef = context.GetRootRecord(node.ChefIndex);
            return result;
        }
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

        public void CleanTimingsOnOrAfterFrame(int frame) {
            foreach (var actions in Actions) {
                foreach (var action in actions) {
                    if (action.Predictions.StartFrame != null && action.Predictions.StartFrame >= frame) {
                        action.Predictions.StartFrame = null;
                    }
                    if (action.Predictions.EndFrame != null && action.Predictions.EndFrame >= frame) {
                        action.Predictions.EndFrame = null;
                    }
                }
            }
        }

        public void DeleteAction((int chef, int index) selectedIndex) {
            var (chef, index) = selectedIndex;
            var action = Actions[chef][index];
            Actions[chef].RemoveAt(index);
            NodeById.Remove(action.Id);
            foreach (var actions in Actions) {
                foreach (var other in actions) {
                    other.Deps.Remove(action.Id);
                }
            }
        }

        public int InsertAction((int chef, int index) insertBefore, GameAction action) {
            var (chef, index) = insertBefore;
            var id = NextId++;
            action.ActionId = id;
            action.Chef = Chefs[chef];
            var node = new GameActionNode {
                Id = id,
                Action = action,
                Deps = new List<int>(),
                Predictions = new GameActionPredictions { }
            };
            Actions[chef].Insert(index, node);
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

        public Save.GameActionSequences ToProto() {
            var result = new Save.GameActionSequences();
            foreach (var (chef, index) in ChefIndexByChef) {
                var chefActions = new Save.GameActionChefList();
                result.Chefs.Add(chefActions);
                chefActions.Chef = chef.path.ToProto();
                foreach (var action in Actions[index]) {
                    chefActions.Actions.Add(action.ToProto());
                }
            }
            return result;
        }
    }

    public static class GameActionSequencesFromProto {
        public static GameActionSequences FromProto(this Save.GameActionSequences sequences, LoadContext context) {
            var result = new GameActionSequences();
            foreach (var chef in sequences.Chefs) {
                var chefEntity = chef.Chef.FromProtoRef(context);
                result.ChefIndexByChef[chefEntity] = result.Chefs.Count;
                result.Chefs.Add(chefEntity);
                var actions = new List<GameActionNode>();
                result.Actions.Add(actions);
                foreach (var action in chef.Actions) {
                    var node = action.FromProto(context);
                    actions.Add(node);
                    result.NodeById[node.Id] = node;
                    result.NextId = Math.Max(result.NextId, node.Id + 1);
                }
            }
            return result;
        }
    }
}