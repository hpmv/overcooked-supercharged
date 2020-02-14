using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GameActionGraphBuilder {
        private readonly GameActionGraph Graph;
        private readonly GameMap Map;
        public int Chef;
        public List<int> Deps;

        private const int UNKNOWN_CHEF = -1;
        private const int CHEF_1 = 97;
        private const int CHEF_2 = 103;

        public GameActionGraphBuilder(GameActionGraph graph, GameMap map, int chef, List<int> deps) {
            Graph = graph;
            Map = map;
            Chef = chef;
            Deps = deps;
        }

        public GameActionGraphBuilder WaitFor(params GameActionGraphBuilder[] other) {
            var newDeps = new List<int>(Deps);
            foreach (var b in other) {
                newDeps.AddRange(b.Deps);
            }
            return new GameActionGraphBuilder(Graph, Map, Chef, newDeps);
        }

        public GameActionGraphBuilder Chef1() {
            return new GameActionGraphBuilder(Graph, Map, CHEF_1, Deps);
        }
        public GameActionGraphBuilder Chef2() {
            return new GameActionGraphBuilder(Graph, Map, CHEF_2, Deps);
        }

        public GameActionGraphBuilder GotoPoint(float x, float y) {
            return AddAction(new GotoAction { Chef = Chef, DesiredPosList = new List<Vector2> { new Vector2(x, y) } });
        }

        public GameActionGraphBuilder Goto(int x, int y) {
            return AddAction(new GotoAction { Chef = Chef, DesiredPosList = new List<Vector2> { Map.GridPos(x, y) } });
        }

        public GameActionGraphBuilder Pickup(int entity) {
            return AddAction(new InteractAction { Chef = Chef, Subject = new LiteralEntityToken(entity) });
        }

        public GameActionGraphBuilder Pickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Chef = Chef, Subject = new SpawnedEntityToken(dep.Deps[0]) });
        }

        public GameActionGraphBuilder PrepareToPickup(int entity) {
            return AddAction(new InteractAction { Chef = Chef, Subject = new LiteralEntityToken(entity), Prepare = true });
        }

        public GameActionGraphBuilder PrepareToPickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Chef = Chef, Subject = new SpawnedEntityToken(dep.Deps[0]), Prepare = true });
        }
        public GameActionGraphBuilder PutOnto(int entity) {
            return AddAction(new InteractAction { Chef = Chef, Subject = new LiteralEntityToken(entity) });
        }

        public GameActionGraphBuilder GetFromCrate(int entity) {
            return AddAction(new InteractAction { Chef = Chef, Subject = new LiteralEntityToken(entity), ExpectSpawn = true });
        }

        public GameActionGraphBuilder DoWork(int entity) {
            return AddAction(new InteractAction { Chef = Chef, Subject = new LiteralEntityToken(entity), Primary = false });
        }

        public GameActionGraphBuilder ThrowTowards(int entity, Vector2 bias = default) {
            return AddAction(new ThrowAction { Chef = Chef, Location = new EntityLocationToken(new LiteralEntityToken(entity)), Bias = bias });
        }

        public GameActionGraphBuilder ThrowTowards(int x, int y, Vector2 bias = default) {
            return AddAction(new ThrowAction { Chef = Chef, Location = new LiteralLocationToken(Map.GridPos(x, y).ToXZVector3()), Bias = bias });
        }

        public GameActionGraphBuilder WaitForSpawn(int entity) {
            return AddAction(new WaitForSpawnAction { SourceEntity = new LiteralEntityToken(entity) });
        }

        public GameActionGraphBuilder WaitForSpawn(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new WaitForSpawnAction { SourceEntity = new SpawnedEntityToken(dep.Deps[0]) });
        }

        public GameActionGraphBuilder WaitForDirtyPlate() {
            return AddAction(new WaitForPlateAction { Dirty = true });
        }

        public GameActionGraphBuilder WaitForCleanPlate() {
            return AddAction(new WaitForPlateAction { Dirty = false });
        }

        public GameActionGraphBuilder WaitForCooked(int entity) {
            return AddAction(new WaitForCookedAction { Entity = new LiteralEntityToken(entity) });
        }

        public GameActionGraphBuilder WaitForMixed(int entity) {
            return AddAction(new WaitForMixedAction { Entity = new LiteralEntityToken(entity) });
        }

        public GameActionGraphBuilder SetGameSpeed(double speed) {
            return AddAction(new GameScaleAction { Scale = speed });
        }

        private GameActionGraphBuilder AddAction(GameAction action) {
            var id = Graph.AddAction(action, Deps.ToArray());
            return new GameActionGraphBuilder(Graph, Map, Chef, new List<int> { id });
        }
    }
}