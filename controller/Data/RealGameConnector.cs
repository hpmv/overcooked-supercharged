using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hpmv {
    class RealGameConnector : Interceptor.IAsync {
        private FramerateController FramerateController = new FramerateController { Delay = TimeSpan.FromSeconds(1) / 60 };
        private GameSetup setup;
        public RealGameSimulator simulator = new RealGameSimulator();
        public bool started = false;
        public bool waitingForStart = false;

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
            simulator.Map = setup.map;
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
            waitingForStart = true;
            started = false;
        }

        public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
            bool restarted = false;
            var time = DateTime.Now;

            if (started || waitingForStart) {
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
                        if (gsm.m_State == GameState.InLevel && waitingForStart) {
                            started = true;
                            waitingForStart = false;
                        }
                    } else {
                        if (started || waitingForStart) {
                            simulator.ApplyGameUpdate(item);
                        }
                    }
                }
            }
            if (started || waitingForStart) {
                if (output.EntityRegistry != null) {
                    foreach (var entity in output.EntityRegistry) {
                        simulator.ApplyEntityRegistryUpdateLate(entity);
                    }
                }
                if (output.CharPos != null) {
                    foreach (var entry in output.CharPos) {
                        simulator.ApplyChefUpdate(entry.Key, entry.Value);
                    }
                }
                if (output.Items != null) {
                    foreach (var entry in output.Items) {
                        simulator.ApplyPositionUpdate(entry.Key, entry.Value);
                    }
                }
            }
            if (started) {
                simulator.AdvanceFrameAfterReceivingGameMessages();
            }

            InputData inputs;
            if (started) {
                setup.LastEmpiricalFrame = setup.LastSimulatedFrame = simulator.Frame;
                inputs = simulator.Step();
            } else {
                inputs = new InputData();
            }

            var temp = OnFrameUpdate;
            if (temp != null) {
                temp();
            }

            if (restarted) inputs.ResetOrderSeed = 12347;
            //FramerateController.WaitTillNextFrame();
            return inputs;
        }
    }
}