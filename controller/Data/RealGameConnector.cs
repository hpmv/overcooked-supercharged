using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hpmv {
    class RealGameConnector : Interceptor.IAsync {
        private GameMap map;
        private GameEntityRecords records;
        private FramerateController FramerateController = new FramerateController { Delay = TimeSpan.FromSeconds(1) / 60 };
        public GameActionSequences sequences;
        private InputHistory inputHistory;
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
            sequences.FillSaneFutureTimings(simulator.Frame);
        }

        public RealGameConnector(GameSetup level) {
            this.map = level.map;
            this.records = level.entityRecords;
            this.sequences = level.sequences;
            this.inputHistory = level.inputHistory;

            simulator.Graph = sequences.ToGraph();
            simulator.Map = map;
            simulator.Timings = sequences;
            simulator.Records = records;
            simulator.InputHistory = inputHistory;

            FakeEntityRegistry.entityToTypes.Clear(); // sigh.
        }

        private void RestartLevel() {
            simulator.Reset();
            records.CleanRecordsFromFrame(0);
            sequences.CleanTimingsFromFrame(0);
            inputHistory.CleanHistoryFromFrame(0);
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
                inputs = simulator.Step();
            } else {
                inputs = new InputData();
            }

            var temp = OnFrameUpdate;
            if (temp != null) {
                temp();
            }

            if (restarted) inputs.ResetOrderSeed = 12347;
            FramerateController.WaitTillNextFrame();
            return inputs;
        }
    }
}