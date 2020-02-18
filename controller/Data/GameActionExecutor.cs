using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv {
    public class GameActionExecutor {
        public GameActionGraph Graph = new GameActionGraph();

        public IEnumerator<InputData> GenUpdate(GameActionGraphState state, GameActionContext context, GameActionSequences recordTimings) {
            int frame = 0;
            var initialTime = DateTime.Now;
            var visited = new HashSet<int>();
            var done = new HashSet<int>();

            var inProgress = new List < (int action, IEnumerator<ControllerInput> gen) > ();
            for (int i = 0; i < Graph.actions.Count; i++) {
                if (Graph.actions[i] != null && Graph.deps[i].Count == 0) {
                    state.States[i].IsActive = true;
                    state.States[i].StartTime = DateTime.Now - initialTime;
                    inProgress.Add((i, Graph.actions[i].Perform(state.States[i], context)));
                    visited.Add(i);
                    if (recordTimings != null) {
                        recordTimings.NodeById[i].Predictions.StartFrame = frame;
                    }
                }
            }

            for (; inProgress.Count > 0; frame++) {
                var inputs = new Dictionary<int, ControllerInput>();
                var names = new List<string>();
                var newInProgress = new List < (int action, IEnumerator<ControllerInput> gen) > ();
                foreach (var(action, gen) in inProgress) {
                    if (gen.MoveNext()) {
                        var cur = gen.Current;
                        if (cur != null) {
                            if (inputs.ContainsKey(cur.chef)) {
                                Console.WriteLine("Conflicting actions emitting inputs for the same chef");
                            } else {
                                inputs[cur.chef] = cur;
                            }
                        }
                        newInProgress.Add((action, gen));
                    } else {
                        state.States[action].IsActive = false;
                        state.States[action].Duration = DateTime.Now - initialTime - state.States[action].StartTime!;
                        if (recordTimings != null) {
                            recordTimings.NodeById[state.States[action].ActionId].Predictions.EndFrame = frame;
                        }
                        done.Add(action);
                        var fwds = Graph.fwds[action];
                        foreach (var fwd in fwds) {
                            if (!visited.Contains(fwd)) {
                                if (Graph.deps[fwd].All(d => done.Contains(d))) {
                                    state.States[fwd].IsActive = true;
                                    state.States[fwd].StartTime = DateTime.Now - initialTime;
                                    newInProgress.Add((fwd, Graph.actions[fwd].Perform(state.States[fwd], context)));
                                    visited.Add(fwd);
                                    if (recordTimings != null) {
                                        recordTimings.NodeById[fwd].Predictions.StartFrame = frame;
                                    }
                                }
                            }
                        }
                    }
                }
                inProgress = newInProgress;
                var inputData = new InputData {
                    Input = new Dictionary<int, OneInputData>()
                };
                foreach (var entry in inputs) {
                    var input = entry.Value;
                    inputData.Input[input.chef] = new OneInputData {
                        Pad = new PadDirection { X = input.axes.X, Y = input.axes.Y },
                        PickDown = input.primary.justPressed,
                        ChopDown = input.secondary.isDown,
                        ThrowDown = input.secondary.justPressed,
                        ThrowUp = input.secondary.justReleased,
                        DashDown = input.dash.justPressed
                    };
                    if (input.GameSpeed != 0) {
                        inputData.GameSpeed = input.GameSpeed;
                    }
                }
                yield return inputData;
            }
        }
    }
}