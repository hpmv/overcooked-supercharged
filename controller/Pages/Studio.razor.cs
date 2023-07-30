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
        private NodeSelector nodeSelector;
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
        private bool ShowMessageStats;

        private void UseLevel(GameSetup level) {
            this.level = level;
            TimelineLayout.Sequences = level.sequences;
            TimelineLayout.Records = level.entityRecords;
            EditorState.Records = level.entityRecords;
            EditorState.Sequences = level.sequences;
            EditorState.SelectedActionIndex = null;
            EditorState.SelectedFrame = 0;
            EditorState.Geometry = level.geometry;
            EditorState.MapByChef = level.mapByChef;
            TimelineLayout.DoLayout(level.LastEmpiricalFrame);
        }

        protected override void OnInitialized() {
            RefreshAvailableSaveFiles();
            nodeSelector = new NodeSelector(() => {
                StateHasChanged();
            });
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
            TimelineLayout.DoLayout(level.LastEmpiricalFrame);
            StateHasChanged();
        }

        [JSInvokable]
        public void HandleScheduleBackgroundClick(double x, double y) {
            var frame = Math.Min(level.LastEmpiricalFrame, TimelineLayout.FrameFromOffset(y));
            NavigateToFrame(frame, true);
        }

        private void NavigateToFrame(int frame, bool clearSelection) {
            if (realGameConnector != null) {
                if (realGameConnector.State == RealGameState.Paused && !realGameConnector.RequestPending) {
                    realGameConnector.RequestWarp(frame);
                } else {
                    // Don't honor frame navigation if we're in the middle of a simulation.
                    return;
                }
            }
            EditorState.SelectedFrame = frame;
            if (clearSelection) {
                EditorState.SelectedActionIndex = null;
            }
            level.pathDebug.Clear();
            StateHasChanged();
        }

        public void HandleSelectAction(int chef, int index, int frame) {
            if (nodeSelector.IsSelecting) {
                nodeSelector.Select(level.sequences.Actions[chef][index]);
            } else {
                if (!CanEdit) {
                    return;
                }
                var targetFrame = Math.Min(level.LastEmpiricalFrame, frame);
                EditorState.SelectedActionIndex = (chef, index);
                NavigateToFrame(targetFrame, false);
            }
        }

        public void HandleDeleteAction() {
            if (!CanEdit || EditorState.SelectedActionIndex == null) {
                return;
            }
            var frame = EditorState.ResimulationFrame(level.LastEmpiricalFrame);
            level.sequences.DeleteAction(EditorState.SelectedActionIndex.Value);
            SimulateInResponseToEditorChange(frame);
        }

        public bool CanEdit {
            get {
                return realGameConnector == null || (realGameConnector.State == RealGameState.Paused && !realGameConnector.RequestPending);
            }
        }

        public void OnDependenciesChanged() {
            var frame = EditorState.ResimulationFrame(level.LastEmpiricalFrame);
            SimulateInResponseToEditorChange(frame);
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
                InvokeAsync(() => {
                    if (!realGameConnector.RequestPending) {
                        EditorState.SelectedFrame = level.LastEmpiricalFrame;
                    }
                    TimelineLayout.DoLayout(level.LastEmpiricalFrame);
                    if (realGameConnector.State == RealGameState.Running) {
                        if (level.entityRecords.InvalidStateReason[level.LastEmpiricalFrame] != null) {
                            realGameConnector.RequestPause();
                        } else if (EditorState.SelectedActionIndex != null) {
                            var (chef, index) = EditorState.SelectedActionIndex.Value;
                            var actions = level.sequences.Actions[chef];
                            int frameToStop;
                            if (actions.Count == 0) {
                                frameToStop = 0;
                            } else if (index >= actions.Count) {
                                frameToStop = actions.Last().Predictions.EndFrame ?? -1;
                            } else {
                                frameToStop = actions[index].Predictions.StartFrame ?? -1;
                            }
                            if (frameToStop != -1 && level.LastEmpiricalFrame >= frameToStop) {
                                EditorState.SelectedFrame = frameToStop;
                                realGameConnector.RequestPauseAndWarp(EditorState.SelectedFrame);
                            }
                        }
                    }
                    StateHasChanged();
                });
            };
            realGameConnector.OnStateChanged += () => {
                StateHasChanged();
            };
            realGameConnector.OnConnectionTerminated += () => {
                InvokeAsync(() => {
                    StopRealSimulation();
                    StateHasChanged();
                });
            };
            realGameConnector.Start();
        }

        private void PauseRealSimulation() {
            if (realGameConnector == null || realGameConnector.RequestPending) {
                return;
            }
            realGameConnector.RequestPause();
            EditorState.SelectedActionIndex = null;
        }

        private void ResumeRealSimulation() {
            if (realGameConnector == null || realGameConnector.RequestPending) {
                return;
            }
            realGameConnector.RequestResume();
            EditorState.SelectedActionIndex = null;
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

        private void SimulateInResponseToEditorChange(int frame) {
            if (realGameConnector != null) {
                realGameConnector.RequestWarpAndResume(frame);
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
            ["carnival32-four"] = typeof(Carnival32FourLevel),
        };

        private void LoadLevel(string selectedLevel) {
            var data = File.ReadAllBytes(selectedLevel);
            var save = new Hpmv.Save.GameSetup();
            save.MergeFrom(data);
            UseLevel(save.FromProto());
        }

        private void SaveLevel() {
            var filePath = "Saves/" + SaveFileName + ".proto";
            var data = level.ToProto();
            File.WriteAllBytes(filePath, data.ToByteArray());
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
