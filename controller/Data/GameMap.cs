using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClipperLib;

namespace Hpmv {
    public class GameMap {
        private Vector2[][] polygons;
        private Vector2 topLeft;

        private const int SCALE = 1000;

        private List<List<IntPoint>> LevelPolygons;

        private IntPoint Discretize(Vector2 p) => new IntPoint((int) (p.X * SCALE), (int) (p.Y * SCALE));
        private Vector2 Undiscretize(IntPoint point) => new Vector2((float) point.X / SCALE, (float) point.Y / SCALE);

        public GameMap(Vector2[][] polygons, Vector2 topLeft) {
            this.polygons = polygons;
            this.topLeft = topLeft;

            LevelPolygons =
                polygons.Select(polygon => polygon.Select(Discretize).ToList()).ToList();
        }

        public Vector2 GridPos(int x, int y) {
            return new Vector2(1.2f * x, -1.2f * y) + topLeft;
        }

        public List<Vector2> FindPath(Vector2 start, List<Vector2> ends) {
            start = EnsureInsideMap(start);
            var startD = Discretize(start);
            var endD = ends.Select(Discretize).ToList();
            var path = PathFinding.ShortestPathAroundObstacles(LevelPolygons, startD, endD);
            if (path.Count >= 2) {
                // TODO: fix inaccurate cases.
                return path.Select(p => Undiscretize(p)).ToList();
            }
            return new List<Vector2>();
        }

        public Vector2 EnsureInsideMap(Vector2 initial) {
            var offsets = new Vector2[] { Vector2.Zero, new Vector2(0.005f, 0.005f), new Vector2(0.005f, -0.005f), new Vector2(-0.005f, 0.005f), new Vector2(-0.005f, -0.005f) };
            foreach (var offset in offsets) {
                if (PathFinding.IsInside(Discretize(initial + offset), LevelPolygons)) {
                    return initial + offset;
                }
            }
            return initial;
        }

        private static double Distance(IntPoint a, IntPoint b) {
            long xdiff = a.X - b.X;
            long ydiff = a.Y - b.Y;
            return Math.Sqrt(xdiff * xdiff + ydiff * ydiff);
        }

        public Vector2 FindNeighboringPointWithinMap(Vector2 point, Vector2 towards) {
            var pointD = Discretize(point);
            var towardsD = Discretize(towards);
            var clipper = new Clipper();
            clipper.AddPath(new List<IntPoint> { pointD, towardsD }, PolyType.ptSubject, false);
            clipper.AddPaths(LevelPolygons, PolyType.ptClip, true);
            var tree = new PolyTree();
            clipper.Execute(ClipType.ctIntersection, tree);
            IntPoint closest = towardsD;

            foreach (var path in tree.Childs) {
                foreach (var cand in path.m_polygon) {
                    if (Distance(closest, pointD) > Distance(cand, pointD)) {
                        closest = cand;
                    }
                }
            }
            return Undiscretize(closest);
        }

        private const float CHEF_RADIUS = 0.4f;
        private const float ITEM_RADIUS = 0.601f;
        private const float TOLERANCE = 0.1f;
        private Vector2[] offsets = new [] {
            new Vector2(-ITEM_RADIUS - CHEF_RADIUS, -ITEM_RADIUS + TOLERANCE),
            new Vector2(-ITEM_RADIUS - CHEF_RADIUS, 0),
            new Vector2(-ITEM_RADIUS - CHEF_RADIUS, ITEM_RADIUS - TOLERANCE),

            new Vector2(ITEM_RADIUS + CHEF_RADIUS, -ITEM_RADIUS + TOLERANCE),
            new Vector2(ITEM_RADIUS + CHEF_RADIUS, 0),
            new Vector2(ITEM_RADIUS + CHEF_RADIUS, ITEM_RADIUS - TOLERANCE),

            new Vector2(-ITEM_RADIUS + TOLERANCE, -ITEM_RADIUS - CHEF_RADIUS),
            new Vector2(0, -ITEM_RADIUS - CHEF_RADIUS),
            new Vector2(ITEM_RADIUS - TOLERANCE, -ITEM_RADIUS - CHEF_RADIUS),

            new Vector2(-ITEM_RADIUS + TOLERANCE, ITEM_RADIUS + CHEF_RADIUS),
            new Vector2(0, ITEM_RADIUS + CHEF_RADIUS),
            new Vector2(ITEM_RADIUS - TOLERANCE, ITEM_RADIUS + CHEF_RADIUS),
        };

        public List<Vector2> GetInteractionPointsForBlockEntity(Vector2 center) {
            return offsets.Select(v => center + v).Where(v => PathFinding.IsInside(Discretize(v), LevelPolygons)).ToList();
        }
    }
}