using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GameActionGraphBuilder {
        private readonly GameActionSequences Sequences;
        private readonly GameMap Map;
        public int Chef = -1;
        public List<int> Deps;

        public GameActionGraphBuilder(GameActionSequences sequences, GameMap map, int chef, List<int> deps) {
            Sequences = sequences;
            Map = map;
            Chef = chef;
            Deps = deps;
        }

        public GameActionGraphBuilder Placeholder() {
            return new GameActionGraphBuilder(Sequences, Map, -1, new List<int>());
        }

        public GameActionGraphBuilder Aka(GameActionGraphBuilder other) {
            other.Chef = Chef;
            other.Deps = Deps;
            return this;
        }

        public GameActionGraphBuilder WaitFor(params GameActionGraphBuilder[] other) {
            var newDeps = new List<int>(Deps);
            foreach (var b in other) {
                newDeps.AddRange(b.Deps);
            }
            return new GameActionGraphBuilder(Sequences, Map, Chef, newDeps);
        }

        public GameActionGraphBuilder WithChef(int chef) {
            return new GameActionGraphBuilder(Sequences, Map, chef, Deps);
        }

        public GameActionGraphBuilder Goto(Vector2 v) {
            return AddAction(new GotoAction { DesiredPos = new LiteralLocationToken(v) });
        }

        public GameActionGraphBuilder Goto(int x, int y) {
            return AddAction(new GotoAction { DesiredPos = new GridPosLocationToken(x, y) });
        }

        public GameActionGraphBuilder GotoNoDash(Vector2 v) {
            return AddAction(new GotoAction { DesiredPos = new LiteralLocationToken(v), AllowDash = false });
        }

        public GameActionGraphBuilder GotoNoDash(int x, int y) {
            return AddAction(new GotoAction { DesiredPos = new GridPosLocationToken(x, y), AllowDash = false });
        }

        public GameActionGraphBuilder Pickup(int entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityToken(entity), IsPickup = true });
        }

        public GameActionGraphBuilder Pickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityToken(dep.Deps[0]), IsPickup = true });
        }

        public GameActionGraphBuilder PrepareToPickup(int entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityToken(entity), Prepare = true });
        }

        public GameActionGraphBuilder PrepareToPickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityToken(dep.Deps[0]), Prepare = true });
        }
        public GameActionGraphBuilder PutOnto(int entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityToken(entity) });
        }
        public GameActionGraphBuilder PutOnto(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't put onto multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityToken(dep.Deps[0]) });
        }

        public GameActionGraphBuilder GetFromCrate(int entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityToken(entity), ExpectSpawn = true, IsPickup = true });
        }

        public GameActionGraphBuilder DoWork(int entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityToken(entity), Primary = false });
        }

        public GameActionGraphBuilder ThrowTowards(int entity, Vector2 bias = default) {
            return AddAction(new ThrowAction { Location = new EntityLocationToken(new LiteralEntityToken(entity)), Bias = bias });
        }

        public GameActionGraphBuilder ThrowTowards(int x, int y, Vector2 bias = default) {
            return AddAction(new ThrowAction { Location = new GridPosLocationToken(x, y), Bias = bias });
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

        public GameActionGraphBuilder Sleep(int frames) {
            return AddAction(new WaitAction { NumFrames = frames });
        }

        public GameActionGraphBuilder SetFps(int fps) {
            return AddAction(new SetFpsAction { Fps = fps });
        }

        public GameActionGraphBuilder Catch(Vector2 direction) {
            return AddAction(new CatchAction { FacingDirection = direction });
        }

        private GameActionGraphBuilder AddAction(GameAction action) {
            action.Chef = Chef;
            string diagInfo = "";
            var trace = new StackTrace(true);
            foreach (var frame in trace.GetFrames()) {
                if (frame != null) {
                    // Console.WriteLine(frame!);
                    if (frame!.GetFileName()?.Contains(".razor.cs") ?? false) {
                        diagInfo += $"script: {frame.GetFileLineNumber()}:{frame.GetFileColumnNumber()}";
                    }
                }
            }
            action.DiagInfo = diagInfo;
            var id = Sequences.AddAction(Chef, action, Deps);
            return new GameActionGraphBuilder(Sequences, Map, Chef, new List<int> { id });
        }
    }
}