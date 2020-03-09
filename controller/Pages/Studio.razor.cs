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
            EditorState.Map = level.map;
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
        public void HandleScheduleBackgroundClick(double x, double y) {
            EditorState.SelectedFrame = Math.Min(level.LastSimulatedFrame, TimelineLayout.FrameFromOffset(y));
            EditorState.SelectedChef = null;
            EditorState.SelectedActionIndex = 0;
            StateHasChanged();
        }

        public void HandleSelectAction(GameEntityRecord chef, int index, int frame) {
            if (nodeSelector.IsSelecting) {
                nodeSelector.Select(level.sequences.Actions[level.sequences.ChefIndexByChef[chef]][index]);
            } else {
                EditorState.SelectedChef = chef;
                EditorState.SelectedActionIndex = index;
                EditorState.SelectedFrame = Math.Min(level.LastSimulatedFrame, frame);
            }
        }

        public void HandleDeleteAction() {
            var frame = EditorState.ResimulationFrame();
            level.sequences.DeleteAction(EditorState.SelectedChef, EditorState.SelectedActionIndex);
            SimulateInResponseToEditorChange(frame + 1);
        }

        public void OnDependenciesChanged() {
            var frame = EditorState.ResimulationFrame();
            SimulateInResponseToEditorChange(frame + 1);
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
                // InvokeAsync(() => {
                //     EditorState.SelectedFrame = level.LastEmpiricalFrame;
                //     TimelineLayout.DoLayout();
                //     StateHasChanged();
                // });
            };
            realGameConnector.Start();
        }

        private void DoOfflineSimulation() {
            var offlineSimulator = new OfflineEmulator(level);
            offlineSimulator.SimulateEntireSchedule(EditorState.SelectedFrame);
            EditorState.SelectedFrame = level.LastSimulatedFrame;
            TimelineLayout.DoLayout();
        }

        private void SimulateInResponseToEditorChange(int frame) {
            var offlineSimulator = new OfflineEmulator(level);
            var actions = level.sequences.Actions[level.sequences.ChefIndexByChef[EditorState.SelectedChef]];
            offlineSimulator.SimulateEntireSchedule(frame);
            TimelineLayout.DoLayout();

            if (EditorState.SelectedActionIndex >= actions.Count) {
                EditorState.SelectedFrame = (actions.Last().Predictions.EndFrame ?? -1) + 1;
            } else {
                EditorState.SelectedFrame = actions[EditorState.SelectedActionIndex].Predictions.StartFrame ?? 0;
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

        private string SelectedLevelToLoad { get; set; }
        private string SelectedLevelToInitialize { get; set; }
        private string SaveFileName { get; set; }

        private Dictionary<string, Type> levelInitializers = new Dictionary<string, Type> {
            ["carnival31-four"] = typeof(Carnival31FourLevel)
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
    }
}
