using System;
using System.Collections.Generic;
using ClipperLib;
using Dijkstra.NET.Graph;
using Dijkstra.NET.ShortestPath;

namespace Hpmv {

    class PathFinding {
        private static int DistanceApprox(IntPoint a, IntPoint b) {
            long xdiff = a.X - b.X;
            long ydiff = a.Y - b.Y;
            return (int) Math.Sqrt(xdiff * xdiff + ydiff * ydiff);
        }

        public static List<IntPoint> ShortestPathAroundObstacles(List<List<IntPoint>> obstacles, IntPoint src, IntPoint dest) {
            List<int> belongingObstacle = new List<int>();
            List<HashSet<int>> alreadyAdded = new List<HashSet<int>>();
            var vertices = new List<IntPoint>();
            for (int i = 0; i < obstacles.Count; i++) {
                foreach (var point in obstacles[i]) {
                    belongingObstacle.Add(i);
                    vertices.Add(point);
                    alreadyAdded.Add(new HashSet<int>());
                }
            }
            vertices.Add(src);
            vertices.Add(dest);
            belongingObstacle.Add(-1);
            belongingObstacle.Add(-2);
            alreadyAdded.Add(new HashSet<int>());
            alreadyAdded.Add(new HashSet<int>());

            var graph = new Graph<int, int>();
            for (int i = 0; i < vertices.Count; i++) {
                graph.AddNode(i);
            }

            var clipper = new Clipper();
            var sol = new PolyTree();
            for (int i = 0; i < vertices.Count; i++) {
                for (int j = i + 1; j < vertices.Count; j++) {
                    // if (belongingObstacle[i] == belongingObstacle[j]) continue;
                    var pA = vertices[i];
                    var pB = vertices[j];
                    bool ok = false; {
                        clipper.Clear();
                        sol.Clear();
                        clipper.AddPath(new List<IntPoint> { pA, pB }, PolyType.ptSubject, false);
                        clipper.AddPaths(obstacles, PolyType.ptClip, true);
                        clipper.Execute(ClipType.ctDifference, sol, PolyFillType.pftEvenOdd);
                        ok = ok || sol.Total == 0;
                    } {
                        clipper.Clear();
                        sol.Clear();
                        clipper.AddPath(new List<IntPoint> { pB, pA }, PolyType.ptSubject, false);
                        clipper.AddPaths(obstacles, PolyType.ptClip, true);
                        clipper.Execute(ClipType.ctDifference, sol, PolyFillType.pftEvenOdd);
                        ok = ok || sol.Total == 0;
                    }
                    if (ok) {
                        graph.Connect((uint) i, (uint) j, DistanceApprox(vertices[i], vertices[j]), 0);
                        graph.Connect((uint) j, (uint) i, DistanceApprox(vertices[i], vertices[j]), 0);
                        alreadyAdded[i].Add(j);
                        alreadyAdded[j].Add(i);
                        // Console.WriteLine($"Connecting {vertices[i]} to {vertices[j]} with dist {DistanceApprox(vertices[i], vertices[j])}");
                    }
                }
            }

            foreach (var obstacle in obstacles) {
                for (int i = 0; i < obstacle.Count; i++) {
                    int j = (i + 1) % obstacle.Count;
                    if (!alreadyAdded[i].Contains(j)) {
                        graph.Connect((uint) i, (uint) j, DistanceApprox(vertices[i], vertices[j]), 0);
                        graph.Connect((uint) j, (uint) i, DistanceApprox(vertices[i], vertices[j]), 0);
                        // Console.WriteLine($"Connecting {vertices[i]} to {vertices[j]} with dist {DistanceApprox(vertices[i], vertices[j])}");
                    }
                }
            }

            var result = graph.Dijkstra((uint) (vertices.Count - 2), (uint) (vertices.Count - 1));
            var pointResult = new List<IntPoint>();
            foreach (uint u in result.GetPath()) {
                pointResult.Add(vertices[(int) u]);
            }
            return pointResult;
        }
    }
}