using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hpmv {
    class RealGameConnector : Interceptor.IAsync {
        private FramerateController FramerateController = new FramerateController { Delay = TimeSpan.FromSeconds(1) / Config.FRAMERATE };
        private GameSetup setup;
        public RealGameSimulator simulator = new RealGameSimulator();
        public RealGameState state = RealGameState.NotInLevel;
        private Queue<RealGameStateRequest> requests = new Queue<RealGameStateRequest>();
        private RealGameStateRequest currentRequest;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private Connector connector;

        public event Action OnFrameUpdate;

        public async void Start() {
            connector = new Connector(this);
            await connector.Connect(cancellationTokenSource.Token);
        }

        public void Stop() {
            cancellationTokenSource.Cancel();
            setup.sequences.FillSaneFutureTimings(simulator.Frame);
        }

        public RealGameConnector(GameSetup level) {
            this.setup = level;

            simulator.Graph = setup.sequences.ToGraph();
            simulator.Geometry = setup.geometry;
            simulator.MapByChef = setup.mapByChef;
            simulator.Timings = setup.sequences;
            simulator.Records = setup.entityRecords;
            simulator.InputHistory = setup.inputHistory;

            FakeEntityRegistry.entityToTypes.Clear(); // sigh.
        }

        private void RestartLevel() {
            simulator.Reset();
            setup.entityRecords.CleanRecordsFromFrame(0);
            setup.sequences.CleanTimingsFromFrame(0);
            setup.inputHistory.CleanHistoryFromFrame(0);
            setup.LastEmpiricalFrame = 0;
            setup.LastSimulatedFrame = 0;
            state = RealGameState.AwaitingStart;
        }

        public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
            bool restarted = false;
            var time = DateTime.Now;

            if (state != RealGameState.NotInLevel) {
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        simulator.ApplyEntityRegistryUpdateEarly(entity);
                    }
                }
            }
            if (output.ServerMessages != null) {
                foreach (var msg in output.ServerMessages) {
                    var item = Deserializer.Deserialize(msg.Type, msg.Message);
                    if (item is LevelLoadByIndexMessage llbim) {
                        RestartLevel();
                        restarted = true;
                    } else if (item is GameStateMessage gsm) {
                        Console.WriteLine($"Transitioned to {gsm.m_State}");
                        if (gsm.m_State == GameState.InLevel && state == RealGameState.AwaitingStart) {
                            state = RealGameState.Running;
                        }
                    } else {
                        if (state == RealGameState.Running || state == RealGameState.AwaitingStart) {
                            simulator.ApplyGameUpdate(item);
                        }
                    }
                }
            }
            if (state == RealGameState.Running || state == RealGameState.AwaitingStart) {
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        simulator.ApplyEntityRegistryUpdateLate(entity);
                    }
                }
                if (output.CharPos != null) {
                    foreach (var entry in output.CharPos) {
                        // Console.WriteLine(new {entry.Key, entry.Value});
                        simulator.ApplyChefUpdate(entry.Key, entry.Value);
                    }
                }
                if (output.Items != null) {
                    foreach (var entry in output.Items) {
                        simulator.ApplyPositionUpdate(entry.Key, entry.Value);
                    }
                }
            }
            if (state == RealGameState.Running) {
                simulator.AdvanceFrameAfterReceivingGameMessages();
            }

            InputData inputs;
            if (state == RealGameState.Running) {
                setup.LastEmpiricalFrame = setup.LastSimulatedFrame = simulator.Frame;
                inputs = simulator.Step();
            } else {
                inputs = new InputData();
            }

            if (currentRequest != null) {
                switch (currentRequest.Kind) {
                    case RealGameStateRequestKind.Pause:
                        if (state != RealGameState.AwaitingPause) {
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                        }
                        if (output.NextFramePaused) {
                            state = RealGameState.Paused;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        }
                        break;
                    case RealGameStateRequestKind.Resume:
                        if (state != RealGameState.AwaitingResume) {
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                        }
                        if (!output.NextFramePaused) {
                            state = RealGameState.Running;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        }
                        break;
                    case RealGameStateRequestKind.RestoreState:
                        if (state != RealGameState.RestoringState) {
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                        }
                        // TODO: check that the frame is correct
                        currentRequest.Completed.SetResult(true);
                        currentRequest = null;
                        break;
                }
            } else {
                if (requests.Count > 0) {
                    currentRequest = requests.Dequeue();
                    switch (currentRequest.Kind) {
                        case RealGameStateRequestKind.Pause:
                            inputs.RequestPause = true;
                            state = RealGameState.AwaitingPause;
                            break;
                        case RealGameStateRequestKind.Resume:
                            inputs.RequestResume = true;
                            state = RealGameState.AwaitingResume;
                            break;
                        case RealGameStateRequestKind.RestoreState:
                            // TODO.
                            state = RealGameState.RestoringState;
                            break;
                    }
                }
            }

            var temp = OnFrameUpdate;
            if (temp != null) {
                temp();
            }

            if (restarted) inputs.ResetOrderSeed = 12347;
            FramerateController.WaitTillNextFrame();
            return inputs;
        }

        public void RequestPause() {
            requests.Enqueue(RealGameStateRequest.Pause());
        }

        public void RequestResume() {
            requests.Enqueue(RealGameStateRequest.Resume());
        }

        public void RequestRestoreState(int frame) {
            requests.Enqueue(RealGameStateRequest.RestoreState(frame));
        }
    }
}