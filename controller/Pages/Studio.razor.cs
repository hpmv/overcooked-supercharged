using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using Hpmv;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Google.Protobuf;

namespace controller.Pages {
    partial class Studio : IDisposable {

        private ElementReference scheduleBackgroundRef;
        private DotNetObjectReference<Studio> thisRef;
        private NodeSelector nodeSelector = new NodeSelector();

        private void UseLevel(GameSetup level) {
            this.level = level;
            TimelineLayout.Sequences = level.sequences;
            EditorState.Records = level.entityRecords;
            EditorState.Sequences = level.sequences;
            EditorState.SelectedChef = null;
            EditorState.SelectedActionIndex = -1;
            EditorState.SelectedFrame = 0;
            EditorState.Geometry = level.geometry;
            EditorState.MapByChef = level.mapByChef;
            TimelineLayout.DoLayout();
        }

        protected override void OnInitialized() {
            RefreshAvailableSaveFiles();
        }

        protected override void OnAfterRender(bool firstRender) {
            if (firstRender) {
                thisRef = DotNetObjectReference.Create(this);
                JSRuntime.InvokeVoidAsync("setUpScheduleBackgroundClick", scheduleBackgroundRef, thisRef);
            }
        }


        public void Dispose() {
            if (realGameConnector != null) {
                StopRealSimulation();
            }
        }

        private void StopRealSimulation() {
            realGameConnector.Stop();
            realGameConnector = null;
            EditorState.SelectedFrame = level.LastEmpiricalFrame;
            TimelineLayout.DoLayout();
            StateHasChanged();
        }

        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;
        private RealGameConnector realGameConnector;
        private GameSetup level = new GameSetup();
        private bool ShowRecordDebug { get; set; }

        [JSInvokable]
        public async void HandleScheduleBackgroundClick(double x, double y) {
            var frame = Math.Min(level.LastEmpiricalFrame, TimelineLayout.FrameFromOffset(y));
            if (realGameConnector != null) {
                if (realGameConnector.State == RealGameState.Paused) {
                    await realGameConnector.RequestWarp(frame);
                } else {
                    // Don't honor frame navigation if we're in the middle of a simulation.
                    return;
                }
            }
            EditorState.SelectedFrame = frame;
            EditorState.SelectedChef = null;
            EditorState.SelectedActionIndex = 0;
            StateHasChanged();
        }

        public async Task HandleSelectAction(GameEntityRecord chef, int index, int frame) {
            if (nodeSelector.IsSelecting) {
                nodeSelector.Select(level.sequences.Actions[level.sequences.ChefIndexByChef[chef]][index]);
            } else {
                var targetFrame = Math.Min(level.LastEmpiricalFrame, frame);
                if (realGameConnector != null) {
                    if (realGameConnector.State == RealGameState.Paused) {
                        await realGameConnector.RequestWarp(targetFrame);
                    } else {
                        // Don't honor frame navigation if we're in the middle of a simulation.
                        return;
                    }
                }
                EditorState.SelectedChef = chef;
                EditorState.SelectedActionIndex = index;
                EditorState.SelectedFrame = targetFrame;
            }
        }

        public async Task HandleDeleteAction() {
            if (!CanEdit) {
                return;
            }
            var frame = EditorState.ResimulationFrame();
            level.sequences.DeleteAction(EditorState.SelectedChef, EditorState.SelectedActionIndex);
            await SimulateInResponseToEditorChange(frame);
        }

        public bool CanEdit {
            get {
                return realGameConnector != null && realGameConnector.State == RealGameState.Paused;
            }
        }

        public async Task OnDependenciesChanged() {
            var frame = EditorState.ResimulationFrame();
            await SimulateInResponseToEditorChange(frame);
        }

        // private async void Repaint() {
        //     this._context = await this._canvasReference.CreateCanvas2DAsync();
        //     await _context.BeginBatchAsync();
        //     await _context.SetStrokeStyleAsync("#666666");
        //     await _context.BeginPathAsync();
        //     for (int i = 0; i < levelShape.Length; i++) {
        //         if (i == 0) {
        //             var point1 = Render(levelShape[i].ToXZVector3());
        //             await _context.MoveToAsync(point1.X, point1.Y);
        //         }
        //         int j = (i + 1) % levelShape.Length;
        //         var point = Render(levelShape[j].ToXZVector3());
        //         await _context.LineToAsync(point.X, point.Y);
        //     }
        //     await _context.StrokeAsync();
        //     await _context.EndBatchAsync();

        // }

        public EditorState EditorState = new EditorState();
        private InputHistory inputHistory = new InputHistory();

        public TimelineLayout TimelineLayout = new TimelineLayout();


        private void StartRealSimulation() {
            if (realGameConnector != null) {
                StopRealSimulation();
            }
            EditorState.SelectedFrame = 0;
            realGameConnector = new RealGameConnector(level);
            realGameConnector.OnFrameUpdate += () => {
                InvokeAsync(async () => {
                    EditorState.SelectedFrame = level.LastEmpiricalFrame;
                    TimelineLayout.DoLayout();
                    if (realGameConnector.State == RealGameState.Running) {
                        if (realGameConnector.simulator.inProgress.Count == 0 || EditorState.SelectedFrame >= realGameConnector.simulator.LastMadeProgressFrame + 600) {
                            if (EditorState.SelectedActionIndex != -1) {
                                await realGameConnector.RequestPause();
                                if (EditorState.SelectedActionIndex != -1) {
                                    var actions = level.sequences.Actions[level.sequences.ChefIndexByChef[EditorState.SelectedChef]];
                                    if (actions.Count == 0) {
                                        EditorState.SelectedFrame = 0;
                                    } else if (EditorState.SelectedActionIndex >= actions.Count) {
                                        EditorState.SelectedFrame = (actions.Last().Predictions.EndFrame ?? 0);
                                    } else {
                                        EditorState.SelectedFrame = actions[EditorState.SelectedActionIndex].Predictions.StartFrame ?? 0;
                                    }
                                    await realGameConnector.RequestWarp(EditorState.SelectedFrame);
                                }
                            }
                        }
                    }
                    StateHasChanged();
                });
            };
            realGameConnector.Start();
        }

        private async Task PauseRealSimulation() {
            if (realGameConnector == null) {
                return;
            }
            await realGameConnector.RequestPause();
            EditorState.SelectedActionIndex = -1;
            EditorState.SelectedChef = null;
        }

        private async Task ResumeRealSimulation() {
            if (realGameConnector == null) {
                return;
            }
            realGameConnector.simulator.ClearHistoryBeforeSimulation();
            await realGameConnector.RequestResume();
            EditorState.SelectedActionIndex = -1;
            EditorState.SelectedChef = null;
        }

        private bool IsSimulationRunning() {
            return realGameConnector != null && realGameConnector.State == RealGameState.Running;
        }

        private bool IsSimulationPaused() {
            return realGameConnector != null && realGameConnector.State == RealGameState.Paused;
        }

        private string GetRealGameState() {
            if (realGameConnector == null) {
                return "Not connected";
            }
            return realGameConnector.State.ToString();
        }

        private async Task SimulateInResponseToEditorChange(int frame) {
            if (realGameConnector != null) {
                await realGameConnector.RequestWarp(frame);
                realGameConnector.simulator.ClearHistoryBeforeSimulation();
                await realGameConnector.RequestResume();
            }
        }

        private void OnFrameChanged(int frame) {
            EditorState.SelectedFrame = frame;
            StateHasChanged();
        }


        private List<string> availableSaveFiles = new List<string>();
        private void RefreshAvailableSaveFiles() {
            availableSaveFiles.Clear();
            var files = Directory.GetFiles("Saves");
            foreach (var file in files) {
                if (file.EndsWith(".proto")) {
                    availableSaveFiles.Add(file);
                }
            }
        }

        private string SelectedLevelToLoad { get; set; }
        private string SelectedLevelToInitialize { get; set; }
        private string SaveFileName { get; set; }

        private Dictionary<string, Type> levelInitializers = new Dictionary<string, Type> {
            ["carnival31-four"] = typeof(Carnival31FourLevel),
            ["campfire14-two"] = typeof(Campture14TwoLevel),
            ["horde13-two"] = typeof(Horde13TwoLevel),
            ["carnival34-four"] = typeof(Carnival34FourLevel),
        };

        private async Task LoadLevel() {
            var data = await File.ReadAllBytesAsync(SelectedLevelToLoad);
            var save = new Hpmv.Save.GameSetup();
            save.MergeFrom(data);
            UseLevel(save.FromProto());
        }

        private async Task SaveLevel() {
            var filePath = "Saves/" + SaveFileName + ".proto";
            var data = level.ToProto();
            await File.WriteAllBytesAsync(filePath, data.ToByteArray());
        }

        private void InitializeNewLevel() {
            var initializer = levelInitializers[SelectedLevelToInitialize];
            UseLevel(Activator.CreateInstance(initializer) as GameSetup);
        }

        private void PatchPrefab() {
            var initializer = levelInitializers[SelectedLevelToInitialize];
            var stock = Activator.CreateInstance(initializer) as GameSetup;
            var dict = new Dictionary<string, PrefabRecord>();

            Queue<PrefabRecord> queue = new Queue<PrefabRecord>();
            foreach (var record in stock.entityRecords.GenAllEntities()) {
                queue.Enqueue(record.prefab);
            }

            while (queue.Count > 0) {
                var rec = queue.Dequeue();
                foreach (var spawn in rec.Spawns) {
                    queue.Enqueue(spawn);
                }
                dict[rec.ClassName] = rec;
                Console.WriteLine($"{rec.ClassName}");
            }


            foreach (var entity in level.entityRecords.GenAllEntities()) {
                var prefab = entity.prefab;
                if (dict.ContainsKey(prefab.ClassName)) {
                    var val = dict[prefab.ClassName];
                    entity.prefab = val;
                }
            }
        }

        private Hpmv.Save.Analysis Analysis { get; set; }
        private void Analyze() {
            var analysis = new Hpmv.Save.Analysis();
            var analyzers = new IAnalyzer[] {
                new MixerAnalyzer()
            };
            foreach (var analyzer in analyzers) {
                foreach (var row in analyzer.Analyze(EditorState.Records, level.LastEmpiricalFrame)) {
                    analysis.Rows.Add(row);
                }
            }
            Analysis = analysis;
        }
    }
}
