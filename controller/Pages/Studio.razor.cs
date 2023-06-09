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
using MatBlazor;

namespace controller.Pages {
    partial class Studio : IDisposable {

        private ElementReference scheduleBackgroundRef;
        private DotNetObjectReference<Studio> thisRef;
        private NodeSelector nodeSelector = new NodeSelector();
        private MatIconButton LoadButton;
        private MatIconButton InitializeButton;
        private MatIconButton PatchPrefabButton;
        private BaseMatMenu LoadMenu;
        private BaseMatMenu InitializeMenu;
        private BaseMatMenu PatchPrefabMenu;
        private bool SaveDialogIsOpen;
        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;
        private RealGameConnector realGameConnector;
        private GameSetup level = new GameSetup();
        private bool ShowInputDebug;
        private bool ShowRecordDebug { get; set; }

        private void UseLevel(GameSetup level) {
            this.level = level;
            TimelineLayout.Sequences = level.sequences;
            TimelineLayout.Records = level.entityRecords;
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
            if (realGameConnector == null) {
                return;
            }
            realGameConnector.Stop();
            realGameConnector = null;
            EditorState.SelectedFrame = level.LastEmpiricalFrame;
            TimelineLayout.DoLayout();
            StateHasChanged();
        }

        [JSInvokable]
        public async void HandleScheduleBackgroundClick(double x, double y) {
            var frame = Math.Min(level.LastEmpiricalFrame, TimelineLayout.FrameFromOffset(y));
            await NavigateToFrame(frame);
        }

        private async Task NavigateToFrame(int frame) {
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
            level.pathDebug.Clear();
            StateHasChanged();
        }

        public async Task HandleSelectAction(GameEntityRecord chef, int index, int frame) {
            if (nodeSelector.IsSelecting) {
                nodeSelector.Select(level.sequences.Actions[level.sequences.ChefIndexByChef[chef]][index]);
            } else {
                var targetFrame = Math.Min(level.LastEmpiricalFrame, frame);
                await NavigateToFrame(targetFrame);
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
                return realGameConnector == null || realGameConnector.State == RealGameState.Paused;
            }
        }

        public async Task OnDependenciesChanged() {
            var frame = EditorState.ResimulationFrame();
            await SimulateInResponseToEditorChange(frame);
        }

        public EditorState EditorState = new EditorState();
        private InputHistory inputHistory = new InputHistory();

        public TimelineLayout TimelineLayout = new TimelineLayout();


        private void StartRealSimulation() {
            if (realGameConnector != null) {
                StopRealSimulation();
            }
            EditorState.SelectedFrame = 0;
            level.pathDebug.Clear();
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
            realGameConnector.OnConnectionTerminated += () => {
                InvokeAsync(() => {
                    StopRealSimulation();
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

        private string RealGameStateToIcon(RealGameState state) {
            switch (state) {
                case RealGameState.NotInLevel:
                    return "pending";
                case RealGameState.AwaitingStart:
                    return "hourglass_empty";
                case RealGameState.Running:
                    return "play_arrow";
                case RealGameState.AwaitingPause:
                    return "hourglass_bottom";
                case RealGameState.Paused:
                    return "hourglass_full";
                case RealGameState.AwaitingResume:
                    return "hourglass_top";
                case RealGameState.Warping:
                    return "restore";
                case RealGameState.Error:
                    return "error_outline";
                case RealGameState.ReestablishingConnection:
                    return "settings_ethernet";
            }
            return "";
        }

        private string RealGameStateToColor(RealGameState state) {
            switch (state) {
                case RealGameState.NotInLevel:
                    return "gray";
                case RealGameState.AwaitingStart:
                    return "gray";
                case RealGameState.Running:
                    return "green";
                case RealGameState.AwaitingPause:
                    return "darkyellow";
                case RealGameState.Paused:
                    return "green";
                case RealGameState.AwaitingResume:
                    return "darkyellow";
                case RealGameState.Warping:
                    return "gray";
                case RealGameState.Error:
                    return "red";
                case RealGameState.ReestablishingConnection:
                    return "gray";
            }
            return "";
        }

        private async Task SimulateInResponseToEditorChange(int frame) {
            if (realGameConnector != null) {
                await realGameConnector.RequestWarp(frame);
                realGameConnector.simulator.ClearHistoryBeforeSimulation();
                await realGameConnector.RequestResume();
            }
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

        private string SaveFileName { get; set; }

        private Dictionary<string, Type> levelInitializers = new Dictionary<string, Type> {
            ["carnival31-four"] = typeof(Carnival31FourLevel),
            ["campfire14-two"] = typeof(Campture14TwoLevel),
            ["horde13-two"] = typeof(Horde13TwoLevel),
            ["carnival34-four"] = typeof(Carnival34FourLevel),
        };

        private async Task LoadLevel(string selectedLevel) {
            var data = await File.ReadAllBytesAsync(selectedLevel);
            var save = new Hpmv.Save.GameSetup();
            save.MergeFrom(data);
            UseLevel(save.FromProto());
        }

        private async Task SaveLevel() {
            var filePath = "Saves/" + SaveFileName + ".proto";
            var data = level.ToProto();
            await File.WriteAllBytesAsync(filePath, data.ToByteArray());
        }

        private void InitializeNewLevel(string selectedLevel) {
            var initializer = levelInitializers[selectedLevel];
            UseLevel(Activator.CreateInstance(initializer) as GameSetup);
        }

        private void PatchPrefab(string selectedLevel) {
            var initializer = levelInitializers[selectedLevel];
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
    }
}
