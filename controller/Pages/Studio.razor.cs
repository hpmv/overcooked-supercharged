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

namespace controller.Pages
{
    partial class Studio : IDisposable
    {

        private ElementReference scheduleBackgroundRef;
        private DotNetObjectReference<Studio> thisRef;
        private NodeSelector nodeSelector;
        private MatIconButton LoadButton;
        private MatIconButton InitializeButton;
        private MatIconButton PatchPrefabButton;
        private MatIconButton CompareButton;
        private BaseMatMenu LoadMenu;
        private BaseMatMenu InitializeMenu;
        private BaseMatMenu PatchPrefabMenu;
        private BaseMatMenu CompareMenu;
        private bool SaveDialogIsOpen;
        private RealGameConnector realGameConnector;
        private GameSetup level = new();
        private bool ShowInputDebug;
        private bool ShowRecordDebug { get; set; }
        private bool ShowMessageStats;

        private bool pauseTimelineLayout;
        private bool PauseTimelineLayout
        {
            get
            {
                return pauseTimelineLayout;
            }
            set
            {
                pauseTimelineLayout = value;
                if (!pauseTimelineLayout)
                {
                    TimelineLayout.DoLayout(level.LastEmpiricalFrame);
                }
            }
        }

        private void UseLevel(GameSetup level)
        {
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

        protected override void OnInitialized()
        {
            RefreshAvailableSaveFiles();
            nodeSelector = new NodeSelector(() =>
            {
                StateHasChanged();
            });
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (firstRender)
            {
                thisRef = DotNetObjectReference.Create(this);
                _ = JSRuntime.InvokeVoidAsync("setUpScheduleBackgroundClick", scheduleBackgroundRef, thisRef);
            }
        }


        public void Dispose()
        {
            if (realGameConnector != null)
            {
                StopRealSimulation();
            }
        }

        private void StopRealSimulation()
        {
            if (realGameConnector == null)
            {
                return;
            }
            realGameConnector.Stop();
            realGameConnector = null;
            EditorState.SelectedFrame = level.LastEmpiricalFrame;
            TimelineLayout.DoLayout(level.LastEmpiricalFrame);
            StateHasChanged();
        }

        [JSInvokable]
        public void HandleScheduleBackgroundClick(double x, double y)
        {
            var frame = Math.Min(level.LastEmpiricalFrame, TimelineLayout.FrameFromOffset(y));
            NavigateToFrame(frame, true);
        }

        private void NavigateToFrame(int frame, bool clearSelection)
        {
            if (realGameConnector != null)
            {
                if (realGameConnector.State == RealGameState.Paused && !realGameConnector.RequestPending)
                {
                    realGameConnector.RequestWarp(frame);
                }
                else
                {
                    // Don't honor frame navigation if we're in the middle of a simulation.
                    return;
                }
            }
            EditorState.SelectedFrame = frame;
            if (clearSelection)
            {
                EditorState.SelectedActionIndex = null;
            }
            level.pathDebug.Clear();
            StateHasChanged();
        }

        public void HandleSelectAction(int chef, int index, int frame)
        {
            if (nodeSelector.IsSelecting)
            {
                nodeSelector.Select(level.sequences.Actions[chef][index]);
            }
            else
            {
                if (!CanEdit)
                {
                    return;
                }
                var targetFrame = Math.Min(level.LastEmpiricalFrame, frame);
                EditorState.SelectedActionIndex = (chef, index);
                NavigateToFrame(targetFrame, false);
            }
        }

        public void HandleDeleteAction()
        {
            if (!CanEdit || EditorState.SelectedActionIndex == null)
            {
                return;
            }
            var frame = EditorState.ResimulationFrame(level.LastEmpiricalFrame);
            level.sequences.DeleteAction(EditorState.SelectedActionIndex.Value);
            SimulateInResponseToEditorChange(frame);
        }

        public bool CanEdit
        {
            get
            {
                return realGameConnector == null || (realGameConnector.State == RealGameState.Paused && !realGameConnector.RequestPending);
            }
        }

        public void OnDependenciesChanged()
        {
            var frame = EditorState.ResimulationFrame(level.LastEmpiricalFrame);
            SimulateInResponseToEditorChange(frame);
        }

        public EditorState EditorState = new EditorState();
        private InputHistory inputHistory = new InputHistory();

        public TimelineLayout TimelineLayout = new TimelineLayout();


        private void StartRealSimulation()
        {
            if (realGameConnector != null)
            {
                StopRealSimulation();
            }
            EditorState.SelectedFrame = 0;
            level.pathDebug.Clear();
            realGameConnector = new RealGameConnector(level);
            realGameConnector.OnFrameUpdate += () =>
            {
                InvokeAsync(() =>
                {
                    if (!realGameConnector.RequestPending)
                    {
                        EditorState.SelectedFrame = level.LastEmpiricalFrame;
                    }
                    if (!PauseTimelineLayout)
                    {
                        TimelineLayout.DoLayout(level.LastEmpiricalFrame);
                    }
                    if (realGameConnector.State == RealGameState.Running)
                    {
                        if (level.entityRecords.InvalidStateReason[level.LastEmpiricalFrame] != "")
                        {
                            realGameConnector.RequestPause();
                        }
                        else if (EditorState.SelectedActionIndex != null)
                        {
                            var (chef, index) = EditorState.SelectedActionIndex.Value;
                            var actions = level.sequences.Actions[chef];
                            int frameToStop;
                            if (actions.Count == 0)
                            {
                                frameToStop = 0;
                            }
                            else if (index >= actions.Count)
                            {
                                frameToStop = actions.Last().Predictions.EndFrame ?? -1;
                            }
                            else
                            {
                                frameToStop = actions[index].Predictions.StartFrame ?? -1;
                            }
                            if (frameToStop != -1 && level.LastEmpiricalFrame >= frameToStop)
                            {
                                EditorState.SelectedFrame = frameToStop;
                                realGameConnector.RequestPauseAndWarp(EditorState.SelectedFrame);
                            }
                        }
                    }
                    if (!PauseTimelineLayout)
                    {
                        StateHasChanged();
                    }
                });
            };
            realGameConnector.OnStateChanged += () =>
            {
                if (!PauseTimelineLayout)
                {
                    StateHasChanged();
                }
            };
            realGameConnector.OnConnectionTerminated += () =>
            {
                InvokeAsync(() =>
                {
                    StopRealSimulation();
                    StateHasChanged();
                });
            };
            realGameConnector.Start();
        }

        private void PauseRealSimulation()
        {
            if (realGameConnector == null || realGameConnector.RequestPending)
            {
                return;
            }
            realGameConnector.RequestPause();
            EditorState.SelectedActionIndex = null;
        }

        private void ResumeRealSimulation()
        {
            if (realGameConnector == null || realGameConnector.RequestPending)
            {
                return;
            }
            realGameConnector.RequestResume();
            EditorState.SelectedActionIndex = null;
        }

        private bool IsSimulationRunning()
        {
            return realGameConnector != null && realGameConnector.State == RealGameState.Running;
        }

        private bool IsSimulationPaused()
        {
            return realGameConnector != null && realGameConnector.State == RealGameState.Paused;
        }

        private static string RealGameStateToIcon(RealGameState state)
        {
            return state switch
            {
                RealGameState.NotInLevel => "pending",
                RealGameState.AwaitingStart => "hourglass_empty",
                RealGameState.Running => "play_arrow",
                RealGameState.AwaitingPause => "hourglass_bottom",
                RealGameState.Paused => "hourglass_full",
                RealGameState.AwaitingResume or RealGameState.AwaitingPhysicsPhaseShiftAlignment => "hourglass_top",
                RealGameState.Warping => "restore",
                RealGameState.Error => "error_outline",
                RealGameState.ReestablishingConnection => "settings_ethernet",
                _ => "",
            };
        }

        private static string RealGameStateToColor(RealGameState state)
        {
            return state switch
            {
                RealGameState.NotInLevel => "gray",
                RealGameState.AwaitingStart => "gray",
                RealGameState.Running => "green",
                RealGameState.AwaitingPause => "darkyellow",
                RealGameState.Paused => "green",
                RealGameState.AwaitingResume => "darkyellow",
                RealGameState.Warping => "gray",
                RealGameState.Error => "red",
                RealGameState.ReestablishingConnection => "gray",
                _ => "",
            };
        }

        private void SimulateInResponseToEditorChange(int frame)
        {
            realGameConnector?.RequestWarpAndResume(frame);
        }

        private readonly List<string> availableSaveFiles = new List<string>();
        private void RefreshAvailableSaveFiles()
        {
            availableSaveFiles.Clear();
            var files = Directory.GetFiles("Saves");
            foreach (var file in files)
            {
                if (file.EndsWith(".proto"))
                {
                    availableSaveFiles.Add(file);
                }
            }
        }

        private string SaveFileName { get; set; }

        private readonly Dictionary<string, Type> levelInitializers = new Dictionary<string, Type>
        {
            ["carnival31-four"] = typeof(Carnival31FourLevel),
            ["carnival34-four"] = typeof(Carnival34FourLevel),
            ["carnival32-four"] = typeof(Carnival32FourLevel),
        };

        private void LoadLevel(string selectedLevel)
        {
            var data = File.ReadAllBytes(selectedLevel);
            var save = new Hpmv.Save.GameSetup();
            save.MergeFrom(data);
            UseLevel(save.FromProto());
        }

        private void SaveLevel()
        {
            var filePath = "Saves/" + SaveFileName + ".proto";
            var data = level.ToProto();
            File.WriteAllBytes(filePath, data.ToByteArray());
        }

        private void InitializeNewLevel(string selectedLevel)
        {
            var initializer = levelInitializers[selectedLevel];
            UseLevel(Activator.CreateInstance(initializer) as GameSetup);
        }

        private void CompareLevel(string otherSave)
        {
            var data = File.ReadAllBytes(otherSave);
            var save = new Hpmv.Save.GameSetup();
            save.MergeFrom(data);
            var level = save.FromProto();

            level.CompareWith(this.level);
        }

        private void PatchPrefab(string selectedLevel)
        {
            var initializer = levelInitializers[selectedLevel];
            var stock = Activator.CreateInstance(initializer) as GameSetup;
            var dict = new Dictionary<string, PrefabRecord>();

            var prefabs = stock.entityRecords.Prefabs;
            foreach (var prefab in prefabs)
            {
                if (dict.ContainsKey(prefab.Name))
                {
                    Console.WriteLine("Error: duplicate prefab name: {0}, patching aborted.", prefab.Name);
                    return;
                }
                dict[prefab.Name] = prefab;
            }

            foreach (var entity in level.entityRecords.GenAllEntities())
            {
                var prefab = entity.prefab;
                if (!dict.ContainsKey(prefab.Name))
                {
                    Console.WriteLine("Error: prefab {0} not found in stock, patching aborted.", prefab.Name);
                    return;
                }
            }

            level.entityRecords.Prefabs.Clear();
            level.entityRecords.Prefabs.AddRange(stock.entityRecords.Prefabs);
            level.entityRecords.PrefabToIndex.Clear();
            for (int i = 0; i < level.entityRecords.Prefabs.Count; i++)
            {
                level.entityRecords.PrefabToIndex[level.entityRecords.Prefabs[i]] = i;
            }

            foreach (var entity in level.entityRecords.GenAllEntities())
            {
                var prefab = entity.prefab;
                entity.prefab = dict[prefab.Name];
            }
        }
    }
}
