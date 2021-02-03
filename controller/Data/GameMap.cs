using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ClipperLib;
using Dijkstra.NET.Graph;
using Dijkstra.NET.ShortestPath;

namespace Hpmv {
    public class GameMapGeometry {
        public readonly Vector2 topLeft;
        public readonly Vector2 size;
        public GameMapGeometry(Vector2 topLeft, Vector2 size) {
            this.topLeft = topLeft;
            this.size = size;
        }

        public Vector2 CoordsToGridPos(Vector2 coord) {
            return new Vector2((coord.X - topLeft.X) / 1.2f, (coord.Y - topLeft.Y) / -1.2f);
        }

        public Vector2 GridPosToCoords(Vector2 gridPos) {
            return new Vector2(1.2f * gridPos.X + topLeft.X, -1.2f * gridPos.Y + topLeft.Y);
        }
        
        public Vector2 GridPos(int x, int y) {
            return new Vector2(1.2f * x, -1.2f * y) + topLeft;
        }

        public (int x, int y) CoordsToGridPosRounded(Vector2 coord) {
            return ((int)Math.Round((coord.X - topLeft.X) / 1.2), (int)Math.Round((coord.Y - topLeft.Y) / -1.2));
        }

        public Save.GameMapGeometry ToProto() {
            return new Save.GameMapGeometry {
                TopLeft = topLeft.ToProto(),
            };
        }
    }

    public static class GameMapGeometryFromProto {
        public static GameMapGeometry FromProto(this Save.GameMapGeometry proto) {
            return new GameMapGeometry(proto.TopLeft.FromProto(), Vector2.Zero);
        }
    }

    public class GameMap {
        public Vector2[][] polygons;
        public GameMapGeometry geometry;
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


        public GameMap(Vector2[][] polygons, GameMapGeometry geometry) {
            this.polygons = polygons;
            this.geometry = geometry;

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

            int numX = (int)Math.Ceiling(geometry.size.X * SCALE / RESOLUTION);
            int numY = (int)Math.Ceiling(geometry.size.Y * SCALE / RESOLUTION);

            precomputedConnectivity = new bool[numX, numY, boundaryPoints.Count];
            topLeftDiscrete = Discretize(geometry.topLeft);
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


        private static int DistanceApprox(IntPoint a, IntPoint b) {
            long xdiff = a.X - b.X;
            long ydiff = a.Y - b.Y;
            return (int)Math.Sqrt(xdiff * xdiff + ydiff * ydiff);
        }

        public List<Vector2> FindPath(Vector2 start, List<Vector2> ends) {
            if (ends.Count == 0) {
                return new List<Vector2>();
            }
            // debugConnectivity.Clear();

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
                    // debugConnectivity.Add((vertices[i], vertices[j]));
                }
            }
            for (int k = 0; k < boundaryPoints.Count; k++) {
                if (startD.Item1 < precomputedConnectivity.GetLength(0) &&
                        startD.Item2 < precomputedConnectivity.GetLength(1) &&
                        startD.Item1 >= 0 && startD.Item2 >= 0 && precomputedConnectivity[startD.Item1, startD.Item2, k]) {
                    var dist = DistanceApprox(startDis, boundaryPoints[k]);
                    graph.Connect(startIndex, boundaryIndices[k], dist, 0);
                    graph.Connect(boundaryIndices[k], startIndex, dist, 0);
                    // debugConnectivity.Add((vertices[boundaryPoints.Count], vertices[k]));
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
                        // debugConnectivity.Add((vertices[boundaryPoints.Count + 1 + i], vertices[k]));
                    }
                }
                if (LineIsInsidePolygon(startDis, endDis[i])) {
                    var dist = DistanceApprox(startDis, endDis[i]);
                    graph.Connect(startIndex, endIndices[i], dist, 0);
                    graph.Connect(endIndices[i], startIndex, dist, 0);
                    // debugConnectivity.Add((vertices[boundaryPoints.Count], vertices[boundaryPoints.Count + 1 + i]));
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

        public bool IsInsideMap(Vector2 v) {
            return PathFinding.IsInside(Discretize(v), LevelPolygons);
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

        public Save.GameMap ToProto() {
            var result = new Save.GameMap();
            result.Polygons.AddRange(polygons.Select(polygon => {
                var proto = new Save.Polygon();
                proto.Points.AddRange(polygon.Select(p => p.ToProto()));
                return proto;
            }));
            result.BoundaryPoints.AddRange(boundaryPoints.Select(p => p.ToProto()));
            result.PointsMesh.AddRange(pointsMesh.Select(mesh => {
                var list = new Save.IntList();
                list.Values.AddRange(mesh);
                return list;
            }));
            foreach (var val in precomputedConnectivity) {
                result.PrecomputedConnectivity.Add(val);
            }
            result.PrecomputedConnectivityDimensions.Add(precomputedConnectivity.GetLength(0));
            result.PrecomputedConnectivityDimensions.Add(precomputedConnectivity.GetLength(1));
            result.PrecomputedConnectivityDimensions.Add(precomputedConnectivity.GetLength(2));
            return result;
        }

        public GameMap(Save.GameMap proto, GameMapGeometry geometry) {
            this.geometry = geometry;
            polygons = proto.Polygons.Select(polygon => polygon.Points.Select(p => p.FromProto()).ToArray()).ToArray();
            boundaryPoints = proto.BoundaryPoints.Select(p => p.FromProto()).ToList();
            pointsMesh = proto.PointsMesh.Select(list => list.Values.ToList()).ToList();
            var dim = proto.PrecomputedConnectivityDimensions;
            precomputedConnectivity = new bool[dim[0], dim[1], dim[2]];
            int ptr = 0;
            for (int i = 0; i < dim[0]; i++) {
                for (int j = 0; j < dim[1]; j++) {
                    for (int k = 0; k < dim[2]; k++) {
                        precomputedConnectivity[i, j, k] = proto.PrecomputedConnectivity[ptr];
                        ptr++;
                    }
                }
            }
            LevelPolygons =
                polygons.Select(polygon => polygon.Select(Discretize).ToList()).ToList();
            topLeftDiscrete = Discretize(geometry.topLeft);
        }
    }

    public static class GameMapFromProto {
        public static GameMap FromProto(this Save.GameMap proto, GameMapGeometry geometry) {
            return new GameMap(proto, geometry);
        }
    }
}