using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using ClipperLib;
using Hpmv;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using Team17.Online.Multiplayer.Messaging;

namespace controller.Pages {
    partial class Carnival31_four : IDisposable {

        private ElementReference scheduleBackgroundRef;
        private DotNetObjectReference<Carnival31_four> thisRef;
        private NodeSelector nodeSelector = new NodeSelector();

        protected override void OnInitialized() {
            TimelineLayout.Sequences = level.sequences;
            EditorState.Records = level.entityRecords;
            EditorState.Sequences = level.sequences;
        }

        protected override void OnAfterRender(bool firstRender) {
            if (firstRender) {
                thisRef = DotNetObjectReference.Create(this);
                JSRuntime.InvokeVoidAsync("setUpScheduleBackgroundClick", scheduleBackgroundRef, thisRef);
                TimelineLayout.DoLayout();
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
        }

        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;
        private RealGameConnector realGameConnector;
        private GameSetup level = new Carnival31FourLevel();

        [JSInvokable]
        public void HandleScheduleBackgroundClick(double x, double y) {
            EditorState.SelectedFrame = Math.Min(EditorState.LastSimulatedFrame, TimelineLayout.FrameFromOffset(y));
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
                EditorState.SelectedFrame = Math.Min(EditorState.LastSimulatedFrame, frame);
            }
        }

        public void HandleDeleteAction() {
            level.sequences.DeleteAction(EditorState.SelectedChef, EditorState.SelectedActionIndex);
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
            EditorState.LastSimulatedFrame = 0;
            EditorState.SelectedFrame = 0;
            realGameConnector = new RealGameConnector(level);
            realGameConnector.OnFrameUpdate += () => {
                InvokeAsync(() => {
                    EditorState.LastSimulatedFrame = realGameConnector.simulator.Frame;
                    EditorState.SelectedFrame = realGameConnector.simulator.Frame;
                    TimelineLayout.DoLayout();
                    StateHasChanged();
                });
            };
            realGameConnector.Start();
        }

        private void DoOfflineSimulation() {
            var offlineSimulator = new OfflineEmulator(level);
            offlineSimulator.SimulateEntireSchedule(EditorState.SelectedFrame);
            EditorState.SelectedFrame = offlineSimulator.Frame;
            EditorState.LastSimulatedFrame = EditorState.SelectedFrame;
            TimelineLayout.DoLayout();
        }

        private void SimulateInResponseToEditorChange() {
            var offlineSimulator = new OfflineEmulator(level);
            offlineSimulator.SimulateEntireSchedule(EditorState.SelectedFrame);
            EditorState.LastSimulatedFrame = offlineSimulator.Frame;
            TimelineLayout.DoLayout();

            var actions = level.sequences.Actions[level.sequences.ChefIndexByChef[EditorState.SelectedChef]];
            if (EditorState.SelectedActionIndex >= actions.Count) {
                EditorState.SelectedFrame = (actions.Last().Predictions.EndFrame ?? -1) + 1;
            } else {
                EditorState.SelectedFrame = actions[EditorState.SelectedActionIndex].Predictions.StartFrame ?? 0;
            }
        }

    }
}

