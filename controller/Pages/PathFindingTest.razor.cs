using System.Collections.Generic;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using ClipperLib;
using Hpmv;

namespace controller.Pages {
    partial class PathFindingTest {
        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;

        private List<List<IntPoint>> obstacles;

        protected override async Task OnAfterRenderAsync(bool firstRender) {
            this._context = await this._canvasReference.CreateCanvas2DAsync();
            obstacles = new List<List<IntPoint>> {
                new List<IntPoint> {
                new IntPoint(0, 100),
                new IntPoint(300, 100),
                new IntPoint(300, 200),
                new IntPoint(0, 200),
                new IntPoint(0, 360),
                new IntPoint(300, 360),
                new IntPoint(300, 460),
                new IntPoint(0, 460),
                new IntPoint(0, 550),
                new IntPoint(400, 550),
                new IntPoint(400, 330),
                new IntPoint(100, 330),
                new IntPoint(100, 230),
                new IntPoint(400, 230),
                new IntPoint(400, 0),
                new IntPoint(0, 0)
                },
            };
            var start = new IntPoint(100, 50);
            var end = new IntPoint(200, 530);
            var path = PathFinding.ShortestPathAroundObstacles(obstacles, start, end);

            await _context.BeginBatchAsync();
            await _context.SetStrokeStyleAsync("1px solid gray");
            foreach (var obstacle in obstacles) {
                await _context.BeginPathAsync();
                for (int i = 0; i < obstacle.Count; i++) {
                    if (i == 0) {
                        await _context.MoveToAsync(obstacle[i].X, obstacle[i].Y);
                    }
                    int j = (i + 1) % obstacle.Count;
                    await _context.LineToAsync(obstacle[j].X, obstacle[j].Y);
                }
                await _context.StrokeAsync();
            }
            await _context.SetStrokeStyleAsync("2px solid blue");
            await _context.BeginPathAsync();
            for (int i = 0; i < path.Count; i++) {
                if (i == 0) {
                    await _context.MoveToAsync(path[i].X, path[i].Y);
                } else {
                    await _context.LineToAsync(path[i].X, path[i].Y);
                }
            }
            await _context.StrokeAsync();
            await _context.EndBatchAsync();
        }
    }
}