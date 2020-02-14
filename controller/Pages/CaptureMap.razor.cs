using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChartJs.Blazor.ChartJS.ScatterChart;
using ChartJs.Blazor.Charts;
using ChartJs.Blazor.Util;
using Hpmv;

namespace controller.Pages {
    partial class CaptureMap : IDisposable {
        private ScatterConfig _config;
        private ChartJsScatterChart _scatter;

        private List<Point> points = new List<Point>();

        private const double scale = 30;

        private Connector connector;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        protected override void OnInitialized() {
            connector = new Connector(new CaptureMapHandler(this));
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

                    Console.WriteLine($"refreshing plot with {points.Count} points");
                    StateHasChanged();
                });
            }
        }

        class CaptureMapHandler : Interceptor.IAsync {
            public CaptureMapHandler(CaptureMap parent) {
                this.parent = parent;
            }

            CaptureMap parent;

            public async Task<InputData> getNextAsync(OutputData output, CancellationToken cancellationToken = default) {
                // TODO: fix.
                await parent.InvokeAsync(() => {
                    foreach (var pos in output.CharPos) {
                        // parent.points.Add(pos.Value.Pos);
                    }
                });
                return new InputData();
            }
        }
    }

}