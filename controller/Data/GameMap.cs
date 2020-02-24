using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClipperLib;
using Dijkstra.NET.Graph;
using Dijkstra.NET.ShortestPath;

namespace Hpmv {
    public class GameMap {
        public Vector2[][] polygons;
        private Vector2 topLeft;
        private IntPoint topLeftDiscrete;

        private const int SCALE = 1000;
        private const int RESOLUTION = 100;

        private List<List<IntPoint>> LevelPolygons;

        private IntPoint Discretize(Vector2 p) => new IntPoint((int)(p.X * SCALE), (int)(p.Y * SCALE));
        private Vector2 Undiscretize(IntPoint point) => new Vector2((float)point.X / SCALE, (float)point.Y / SCALE);
        private (int, int) ClosestGridPoint(IntPoint p) {
            var x = (p.X - topLeftDiscrete.X + RESOLUTION / 2) / RESOLUTION;
            var y = (-(p.Y - topLeftDiscrete.Y) + RESOLUTION / 2) / RESOLUTION;
            return ((int)x, (int)y);
        }

        public Vector2 CoordsToGridPos(Vector2 coord) {
            return new Vector2((coord.X - topLeft.X) / 1.2f, (coord.Y - topLeft.Y) / -1.2f);
        }

        private bool[,,] precomputedConnectivity;
        private List<IntPoint> boundaryPoints = new List<IntPoint>();
        private List<List<int>> pointsMesh = new List<List<int>>();

        public List<(Vector2, Vector2)> debugConnectivity = new List<(Vector2, Vector2)>();

        private bool LineIsInsidePolygon(IntPoint pA, IntPoint pB) {

            var clipper = new Clipper();
            var sol = new PolyTree();
            bool ok = false;
            {
                clipper.Clear();
                sol.Clear();
                clipper.AddPath(new List<IntPoint> { pA, pB }, PolyType.ptSubject, false);
                clipper.AddPaths(LevelPolygons, PolyType.ptClip, true);
                clipper.Execute(ClipType.ctDifference, sol, PolyFillType.pftEvenOdd);
                ok = ok || sol.Total == 0;
            }
            {
                clipper.Clear();
                sol.Clear();
                clipper.AddPath(new List<IntPoint> { pB, pA }, PolyType.ptSubject, false);
                clipper.AddPaths(LevelPolygons, PolyType.ptClip, true);
                clipper.Execute(ClipType.ctDifference, sol, PolyFillType.pftEvenOdd);
                ok = ok || sol.Total == 0;
            }
            return ok;
        }

        public GameMap(Vector2[][] polygons, Vector2 topLeft, Vector2 size) {
            this.polygons = polygons;
            this.topLeft = topLeft;

            LevelPolygons =
                polygons.Select(polygon => polygon.Select(Discretize).ToList()).ToList();


            foreach (var polygon in LevelPolygons) {
                foreach (var point in polygon) {
                    boundaryPoints.Add(point);
                    pointsMesh.Add(new List<int>());

                }
            }

            bool[,] alreadyAdded = new bool[boundaryPoints.Count, boundaryPoints.Count];
            for (int i = 0; i < boundaryPoints.Count; i++) {
                for (int j = 0; j < i; j++) {
                    // if (belongingObstacle[i] == belongingObstacle[j]) continue;
                    var pA = boundaryPoints[i];
                    var pB = boundaryPoints[j];
                    if (LineIsInsidePolygon(pA, pB)) {
                        pointsMesh[i].Add(j);
                        pointsMesh[j].Add(i);
                        alreadyAdded[i, j] = true;
                        alreadyAdded[j, i] = true;
                        // Console.WriteLine($"Connecting {vertices[i]} to {vertices[j]} with dist {DistanceApprox(vertices[i], vertices[j])}");
                    }
                }
            }

            int ii = 0;
            foreach (var obstacle in LevelPolygons) {
                for (int i = 0; i < obstacle.Count; i++) {
                    int j = (i + 1) % obstacle.Count;
                    if (!alreadyAdded[ii + i, ii + j]) {
                        pointsMesh[ii + i].Add(ii + j);
                        pointsMesh[ii + j].Add(ii + i);
                    }
                }
                ii += obstacle.Count;
            }

            int numX = (int)Math.Ceiling(size.X * SCALE / RESOLUTION);
            int numY = (int)Math.Ceiling(size.Y * SCALE / RESOLUTION);

            precomputedConnectivity = new bool[numX, numY, boundaryPoints.Count];
            topLeftDiscrete = Discretize(topLeft);
            for (int i = 0; i < numX; i++) {
                for (int j = 0; j < numY; j++) {
                    var point = new IntPoint(topLeftDiscrete.X + i * RESOLUTION, topLeftDiscrete.Y - j * RESOLUTION);
                    for (int k = 0; k < boundaryPoints.Count; k++) {
                        var boundary = boundaryPoints[k];
                        if (LineIsInsidePolygon(point, boundary)) {
                            precomputedConnectivity[i, j, k] = true;
                        }
                    }
                }
            }
        }

        public Vector2 GridPos(int x, int y) {
            return new Vector2(1.2f * x, -1.2f * y) + topLeft;
        }

        private static int DistanceApprox(IntPoint a, IntPoint b) {
            long xdiff = a.X - b.X;
            long ydiff = a.Y - b.Y;
            return (int)Math.Sqrt(xdiff * xdiff + ydiff * ydiff);
        }

        public List<Vector2> FindPath(Vector2 start, List<Vector2> ends) {
            debugConnectivity.Clear();

            var startDis = Discretize(start);
            var endDis = ends.Select(Discretize).ToList();
            var startD = ClosestGridPoint(startDis);
            var endD = endDis.Select(ClosestGridPoint).ToList();

            var vertices = new List<Vector2>();


            var graph = new Graph<int, int>();
            var boundaryIndices = new List<uint>();
            for (int i = 0; i < boundaryPoints.Count; i++) {
                boundaryIndices.Add(graph.AddNode(i));
                vertices.Add(Undiscretize(boundaryPoints[i]));
            }
            var startIndex = graph.AddNode(boundaryPoints.Count);
            vertices.Add(start);
            var endIndices = new List<uint>();
            for (int i = 0; i < ends.Count; i++) {
                endIndices.Add(graph.AddNode(boundaryPoints.Count + 1 + i));
                vertices.Add(ends[i]);
            }

            for (int i = 0; i < boundaryPoints.Count; i++) {
                foreach (int j in pointsMesh[i]) {
                    var dist = DistanceApprox(boundaryPoints[i], boundaryPoints[j]);
                    graph.Connect(boundaryIndices[i], boundaryIndices[j], dist, 0);
                    graph.Connect(boundaryIndices[j], boundaryIndices[i], dist, 0);
                    debugConnectivity.Add((vertices[i], vertices[j]));
                }
            }
            for (int k = 0; k < boundaryPoints.Count; k++) {
                if (startD.Item1 < precomputedConnectivity.GetLength(0) &&
                        startD.Item2 < precomputedConnectivity.GetLength(1) &&
                        startD.Item1 >= 0 && startD.Item2 >= 0 && precomputedConnectivity[startD.Item1, startD.Item2, k]) {
                    var dist = DistanceApprox(startDis, boundaryPoints[k]);
                    graph.Connect(startIndex, boundaryIndices[k], dist, 0);
                    graph.Connect(boundaryIndices[k], startIndex, dist, 0);
                    debugConnectivity.Add((vertices[boundaryPoints.Count], vertices[k]));
                }
            }
            for (int i = 0; i < ends.Count; i++) {
                var end = endD[i];
                for (int k = 0; k < boundaryPoints.Count; k++) {
                    if (end.Item1 < precomputedConnectivity.GetLength(0) &&
                        end.Item2 < precomputedConnectivity.GetLength(1) &&
                        end.Item1 >= 0 && end.Item2 >= 0 && precomputedConnectivity[end.Item1, end.Item2, k]) {
                        var dist = DistanceApprox(endDis[i], boundaryPoints[k]);
                        graph.Connect(endIndices[i], boundaryIndices[k], dist, 0);
                        graph.Connect(boundaryIndices[k], endIndices[i], dist, 0);
                        debugConnectivity.Add((vertices[boundaryPoints.Count + 1 + i], vertices[k]));
                    }
                }
                if (LineIsInsidePolygon(startDis, endDis[i])) {
                    var dist = DistanceApprox(startDis, endDis[i]);
                    graph.Connect(startIndex, endIndices[i], dist, 0);
                    graph.Connect(endIndices[i], startIndex, dist, 0);
                    debugConnectivity.Add((vertices[boundaryPoints.Count], vertices[boundaryPoints.Count + 1 + i]));
                }
            }


            var result = endIndices.Select(endIndex => {
                return graph.Dijkstra(startIndex, endIndex);
            }).OrderBy(r => {
                if (!r.IsFounded) {
                    return double.MaxValue;
                }
                return r.Distance;
            }).First();
            var pointResult = new List<Vector2>();
            foreach (uint u in result.GetPath()) {
                pointResult.Add(vertices[graph[u].Item]);
            }
            return pointResult;
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
        private Vector2[] offsets = new[] {
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
            var results = offsets.Select(v => center + v).Where(v => PathFinding.IsInside(Discretize(v), LevelPolygons)).ToList();
            if (results.Count == 0) {
                Console.WriteLine($"Trying to find interaction points around unreachable entity at {center}");
            }
            return results;
        }
    }
}