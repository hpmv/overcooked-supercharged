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

        // Queue of state transition requests; enqueued by the UI thread, dequeued by the RPC handling thread.
        private ConcurrentQueue<RealGameStateRequest> requests = new ConcurrentQueue<RealGameStateRequest>();
        // Current request that is being processed by the RPC handling thread; only used by RPC handling thread.
        private RealGameStateRequest currentRequest;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private Connector connector;

        public RealGameState State { get; set; } = RealGameState.NotInLevel;

        public event Action OnFrameUpdate;
        public event Action OnConnectionTerminated;

        public async void Start() {
            connector = new Connector(this);
            while (!cancellationTokenSource.Token.IsCancellationRequested) {
                try {
                    await connector.Connect(cancellationTokenSource.Token);
                } catch (Exception e) {
                    Console.WriteLine("Connection error: {0}", e);
                }
                if (this.State != RealGameState.Paused) {
                    break;
                }
                Console.WriteLine("Reestablishing connection");
            }
            OnConnectionTerminated?.Invoke();
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
            setup.entityRecords.CleanRecordsAfterFrame(0);
            setup.sequences.CleanTimingsAfterFrame(0);
            setup.inputHistory.CleanHistoryAfterFrame(0);
            setup.LastEmpiricalFrame = 0;
            State = RealGameState.AwaitingStart;
        }

        public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
            // We do this step first because this is needed to deserialize further game messages.
            // See FakeEntityRegistry for more information.
            //
            // We do this even before the first frame, because these messages are constructed by intercepting
            // StartSynchronisingEntry, which happens before the level timer begins. We need to keep applying
            // these in the middle of the level, because these are needed for newly constructed entities.
            if (State != RealGameState.NotInLevel) {
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        simulator.ApplyEntityRegistryUpdateEarly(entity);
                    }
                }
            }

            // InputData is what we return to the game on the next frame.
            InputData inputs = new InputData();

            // First handle major game state transitions.
            var didGameJustStart = false;
            if (output.ServerMessages != null) {
                foreach (var msg in output.ServerMessages) {
                    var item = Deserializer.Deserialize(msg.Type, msg.Message);
                    if (item is LevelLoadByIndexMessage || item is LevelLoadByNameMessage) {
                        RestartLevel();
                        inputs.ResetOrderSeed = 12347;
                    } else if (item is GameStateMessage gsm) {
                        Console.WriteLine($"Transitioned to {gsm.m_State}");
                        if (gsm.m_State == GameState.InLevel && State == RealGameState.AwaitingStart) {
                            State = RealGameState.Running;
                            didGameJustStart = true;
                        }
                    }
                }
            }
 
            // For both Running and AwaitingPause, the logical game state advanced by one frame
            // so we need to do the same.
            if (State == RealGameState.Running || State == RealGameState.AwaitingPause) {
                // Don't advance frame if we just started running. This is a special case.
                if (!didGameJustStart) {
                    simulator.AdvanceFrame();
                    setup.LastEmpiricalFrame = simulator.Frame;
                    if (output.ServerMessages != null) {
                        foreach (var msg in output.ServerMessages) {
                            var item = Deserializer.Deserialize(msg.Type, msg.Message);
                            simulator.ApplyGameUpdate(item);
                        }
                    }
                    simulator.AdvanceAutomaticProgress();
                }

                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        // TODO: what does this do exactly?
                        simulator.ApplyEntityRegistryUpdateLate(entity);
                    }
                }
                if (output.Chefs != null) {
                    foreach (var entry in output.Chefs) {
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

            // For both Running and AwaitingResume, the *next* frame will need inputs, so compute
            // inputs.
            if (State == RealGameState.Running || State == RealGameState.AwaitingResume) {
                // TODO: this also computes whether current actions have ended. If we pause right
                // when an action is done, we would not be marking the current action as done
                // until we resume again. Is that an issue?
                inputs.Input = simulator.ComputeInputForNextFrame();
                inputs.NextFrame = simulator.Frame + 1;
            } else {
                inputs.NextFrame = simulator.Frame;
            }

            if (State == RealGameState.Warping) {
                var entityIdToPathWhenWarping = new Dictionary<int, EntityPath>();
                if (State == RealGameState.Warping) {
                    foreach (var entry in output.Items) {
                        if (entry.Value.__isset.entityPathReference) {
                            var path =  entry.Value.EntityPathReference.FromThrift();
                            entityIdToPathWhenWarping[entry.Key] = path;
                        }
                    }
                }
                if (output.ServerMessages != null) {
                    foreach (var msg in output.ServerMessages) {
                        var item = Deserializer.Deserialize(msg.Type, msg.Message);
                        simulator.ApplyGameUpdateWhenWarping(item, entityIdToPathWhenWarping);
                    }
                }
                simulator.SetFrameAfterWarping(output.FrameNumber);
            }

            // If we advanced logical frame earlier, notify the UI of the new frame. We don't do this
            // earlier because we may have called ComputeInputForNextFrame in the middle and that
            // could affect start and end times of actions that affects UI layout.
            if (State == RealGameState.Running || State == RealGameState.AwaitingPause) {
                var temp = OnFrameUpdate;
                if (temp != null) {
                    temp();
                }
            }
            
            // Finally, handle user requests to pause, resume, or warp.
            if (currentRequest != null) {
                switch (currentRequest.Kind) {
                    case RealGameStateRequestKind.Pause:
                        if (State != RealGameState.AwaitingPause) {
                            Console.WriteLine($"Unexpected state when handling pause; should be AwaitingPause; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        if (output.NextFramePaused) {
                            State = RealGameState.Paused;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        } else {
                            Console.WriteLine($"Unexpected game state when pausing; game is not paused but should be");
                            State = RealGameState.Error;
                        }
                        break;
                    case RealGameStateRequestKind.Resume:
                        if (State != RealGameState.AwaitingResume) {
                            Console.WriteLine($"Unexpected state when handling resume; should be AwaitingResume; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        if (!output.NextFramePaused) {
                            State = RealGameState.Running;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        } else {
                            Console.WriteLine($"Unexpected game state when resuming; game is paused but should not be");
                            State = RealGameState.Error;
                        }
                        break;
                    case RealGameStateRequestKind.Warp:
                        Console.WriteLine($"Current state: {State}; frame is {output.FrameNumber}");
                        if (State != RealGameState.Warping) {
                            Console.WriteLine($"Unexpected state when handling warp; should be Warping; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        if (output.FrameNumber == currentRequest.FrameToWarpTo) {
                            State = RealGameState.Paused;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        } else {
                            Console.WriteLine($"Unexpected game state when warping; game frame {output.FrameNumber} is not equal to the requested frame {currentRequest.FrameToWarpTo}");
                            State = RealGameState.Error;
                        }
                        break;
                }
            } else if (!didGameJustStart) {
                RealGameStateRequest request;
                if (requests.TryDequeue(out request)) {
                    currentRequest = request;
                    switch (currentRequest.Kind) {
                        case RealGameStateRequestKind.Pause:
                            if (State == RealGameState.Running) {
                                inputs.RequestPause = true;
                                State = RealGameState.AwaitingPause;
                            } else {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
                            break;
                        case RealGameStateRequestKind.Resume:
                            if (State == RealGameState.Paused) {
                                Console.WriteLine($"Requesting resume from frame {simulator.Frame}");
                                inputs.RequestResume = true;
                                State = RealGameState.AwaitingResume;
                            } else {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
                            break;
                        case RealGameStateRequestKind.Warp:
                            if (State == RealGameState.Paused) {
                                inputs.Warp = WarpCalculator.CalculateWarp(simulator.entityIdToRecord, setup.entityRecords, simulator.Frame, request.FrameToWarpTo);
                                State = RealGameState.Warping;
                            } else {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
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

        public Task RequestWarp(int frame) {
            var request = RealGameStateRequest.Warp(frame);
            requests.Enqueue(request);
            return request.Completed.Task;
        }
    }
}