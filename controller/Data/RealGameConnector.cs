using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hpmv {
    class RealGameConnector : Interceptor.IAsync {
        private FramerateController FramerateController = new FramerateController { Delay = TimeSpan.FromSeconds(1) / Config.FRAMERATE };
        private GameSetup setup;
        public RealGameSimulator simulator = new RealGameSimulator();
        public RealGameState state = RealGameState.NotInLevel;

        // Queue of state transition requests; enqueued by the UI thread, dequeued by the RPC handling thread.
        private ConcurrentQueue<RealGameStateRequest> requests = new ConcurrentQueue<RealGameStateRequest>();
        // Current request that is being processed by the RPC handling thread; only used by RPC handling thread.
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
            simulator.setup = setup;

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
            var entityIdToPathWhenRestoringState = new Dictionary<int, EntityPath>();
            if (state == RealGameState.RestoringState) {
                foreach (var entry in output.Items) {
                    if (entry.Value.__isset.entityPathReference) {
                        var path =  entry.Value.EntityPathReference.FromThrift();
                        entityIdToPathWhenRestoringState[entry.Key] = path;
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
                        if (state == RealGameState.Running) {
                            simulator.ApplyGameUpdate(item);
                        } else if (state == RealGameState.RestoringState) {
                            simulator.ApplyGameUpdateWhenRestoringState(item, entityIdToPathWhenRestoringState);
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

            if (state == RealGameState.Running || state == RealGameState.AwaitingStart) {
                var temp = OnFrameUpdate;
                if (temp != null) {
                    temp();
                }
            }

            InputData inputs;
            if (state == RealGameState.Running) {
                setup.LastEmpiricalFrame = setup.LastSimulatedFrame = simulator.Frame;
                inputs = simulator.Step();
            } else {
                inputs = new InputData();
            }
            if (restarted) inputs.ResetOrderSeed = 12347;
            
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
                        Console.WriteLine($"Current state: {state}; frame is {output.FrameNumber}");
                        if (state != RealGameState.RestoringState) {
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                        }
                        // TODO: check that the frame is correct
                        state = RealGameState.Paused;
                        currentRequest.Completed.SetResult(true);
                        currentRequest = null;
                        break;
                }
            } else {
                RealGameStateRequest request;
                if (requests.TryDequeue(out request)) {
                    currentRequest = request;
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
                            var warp = WarpCalculator.CalculateWarp(simulator.entityIdToRecord, setup.entityRecords, simulator.Frame, request.FrameToRestoreTo);
                            inputs.Warp = warp;
                            state = RealGameState.RestoringState;
                            simulator.ChangeStateAfterWarping(warp);
                            break;
                    }
                }
            }

            FramerateController.WaitTillNextFrame();
            return inputs;
        }

        public Task<bool> RequestPause() {
            var request = RealGameStateRequest.Pause();
            requests.Enqueue(request);
            return request.Completed.Task;
        }

        public Task RequestResume() {
            var request = RealGameStateRequest.Resume();
            requests.Enqueue(request);
            return request.Completed.Task;
        }

        public Task RequestRestoreState(int frame) {
            var request = RealGameStateRequest.RestoreState(frame);
            requests.Enqueue(request);
            return request.Completed.Task;
        }
    }
}