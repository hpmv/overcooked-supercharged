using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ChartJs.Blazor.ChartJS.ScatterChart;
using ChartJs.Blazor.Charts;
using ChartJs.Blazor.Util;
using Hpmv;
using Team17.Online.Multiplayer.Messaging;

namespace controller.Pages {
    partial class CaptureMap : IDisposable {
        private ScatterConfig _config;
        private ChartJsScatterChart _scatter;

        private List<Point> points = new List<Point>();

        private const double scale = 30;

        private Connector connector;
        private CaptureMapHandler handler;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        protected override void OnInitialized() {
            connector = new Connector(handler = new CaptureMapHandler(this));
            DoConnect();
            RefreshEverySec();

            _config = new ScatterConfig {
                Options = new ScatterOptions {
                    Responsive = true,
                },
            };
            _config.Data.Datasets.Add(new ScatterDataset {
                Data = new List<ChartJs.Blazor.ChartJS.Common.Point>(),
                BackgroundColor = ColorUtil.RandomColorString(),
                BorderWidth = 0,
                BorderColor = "#666666",
                PointRadius = 2,
                Fill = true
            });
        }

        private void PrintPositions() {
            foreach (var (_, line) in handler.posCache.OrderBy(p => p.Key)) {
                Console.WriteLine(line);
            }
        }

        public void Dispose() {
            cancellationTokenSource.Cancel();
        }

        private async void DoConnect() {
            await connector.Connect(cancellationTokenSource.Token);
        }

        private async void RefreshEverySec() {
            while (!cancellationTokenSource.IsCancellationRequested) {
                await Task.Delay(1000);
                await InvokeAsync(() => {
                    var scatterData = _config.Data.Datasets[0];
                    foreach (var point in points.Skip(scatterData.Data.Count)) {
                        scatterData.Data.Add(new ChartJs.Blazor.ChartJS.Common.Point(point.X, point.Z));
                    }

                    StateHasChanged();
                });
            }
        }

        class CaptureMapHandler : Interceptor.IAsync {
            public CaptureMapHandler(CaptureMap parent) {
                this.parent = parent;
            }

            CaptureMap parent;
            public Dictionary<int, string> posCache = new Dictionary<int, string>();

            public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
                // TODO: fix.
                if (output.Items != null) {
                    foreach (var pos in output.Items) {
                        var line = $"entityRecords.CapturedInitialPositions[{pos.Key}] = new Vector3({(float)pos.Value.Pos.X}f, {(float)pos.Value.Pos.Y}f, {(float)pos.Value.Pos.Z}f);";
                        Console.WriteLine(line);
                        posCache[pos.Key] = line;
                        // parent.points.Add(pos.Value.Pos);
                    }
                }
                if (output.EntityRegistry != null) {
                    foreach (var reg in output.EntityRegistry) {
                        var line = $"entityRecords.CapturedInitialPositions[{reg.EntityId}] = new Vector3({(float)reg.Pos.X}f, {(float)reg.Pos.Y}f, {(float)reg.Pos.Z}f);";
                        Console.WriteLine(line);
                        posCache[reg.EntityId] = line;
                        // parent.points.Add(pos.Value.Pos);
                    }
                }
                if (output.ServerMessages != null) {

                    foreach (var msg in output.ServerMessages) {
                        var item = Deserializer.Deserialize(msg.Type, msg.Message);

                        if (item is DestroyEntityMessage dem) {
                            Console.WriteLine($"Destroying {dem.m_Header.m_uEntityID}");
                            posCache.Remove((int)dem.m_Header.m_uEntityID);
                        } else if (item is DestroyEntitiesMessage dems) {
                            foreach (var i in dems.m_ids) {
                                Console.WriteLine($"Destroying {i}");
                                posCache.Remove((int)i);
                            }
                        }
                    }
                }
                return new InputData();
            }
        }
    }

}