using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using ClipperLib;
using Hpmv;

namespace controller.Pages {
    partial class PathFindingTest {
        private Canvas2DContext _context;
        private BECanvasComponent _canvasReference;

        public (double x, double y)[] levelShape = {
            (17.8, -7),
            (17.8, -11),
            (29.56, -11),
            (29.8, -11.24),
            (29.8, -12.76),
            (29.56, -13),
            (17.8, -13),
            (17.8, -17),
            (30.2, -17),
            (30.2, -15.4),
            (18.44, -15.4),
            (18.2, -15.16),
            (18.2, -13.64),
            (18.44, -13.4),
            (30.2, -13.4),
            (30.2, -10.6),
            (18.44, -10.6),
            (18.2, -10.36),
            (18.2, -8.84),
            (18.44, -8.6),
            (30.2, -8.6),
            (30.2, -7),
            (29.8, -7),
            (29.8, -7.5),
            (27.8, -7.5),
            (27.8, -7)
        };

        private GameMap map;

        private Vector2 Render(Vector2 point) {
            return new Vector2(30 * (-17 + point.X), 30 * -(6 + point.Y));
        }

        protected override async Task OnAfterRenderAsync(bool firstRender) {
            this._context = await this._canvasReference.CreateCanvas2DAsync();

            var geometry = new GameMapGeometry(new Vector2(16.8f, -6f), new Vector2(1.2f * 12, 1.2f * 10));
            map = new GameMap(new Vector2[][] {
                    levelShape.Select(v => new Vector2((float) v.x, (float) v.y)).ToArray()
                }, geometry);

            var path = map.FindPath(new Vector2(20.113993f, -7.0000005f), map.GetInteractionPointsForBlockEntity(new Vector2(30.2f, -11.301f)));

            await _context.BeginBatchAsync();
            await _context.SetStrokeStyleAsync("1px solid gray");
            foreach (var obstacle in map.polygons) {
                await _context.BeginPathAsync();
                for (int i = 0; i < obstacle.Length; i++) {
                    var point = Render(obstacle[i]);
                    if (i == 0) {
                        await _context.MoveToAsync(point.X, point.Y);
                    }
                    int j = (i + 1) % obstacle.Length;
                    point = Render(obstacle[j]);
                    await _context.LineToAsync(point.X, point.Y);
                }
                await _context.StrokeAsync();
            }
            await _context.SetStrokeStyleAsync("2px solid blue");
            await _context.BeginPathAsync();
            for (int i = 0; i < path.Count; i++) {
                var point = Render(path[i]);
                if (i == 0) {
                    await _context.MoveToAsync(point.X, point.Y);
                } else {
                    await _context.LineToAsync(point.X, point.Y);
                }
            }
            await _context.StrokeAsync();

            // foreach (var pair in map.debugConnectivity) {
            //     await _context.BeginPathAsync();
            //     await _context.MoveToAsync(Render(pair.Item1).X, Render(pair.Item1).Y);
            //     await _context.LineToAsync(Render(pair.Item2).X, Render(pair.Item2).Y);
            //     await _context.StrokeAsync();
            // }

            await _context.EndBatchAsync();
        }
    }
}