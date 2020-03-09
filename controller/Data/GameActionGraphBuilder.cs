using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GameActionGraphBuilder {
        private readonly GameActionSequences Sequences;
        private readonly GameMap Map;
        public GameEntityRecord Chef;
        public List<int> Deps;

        public GameActionGraphBuilder(GameActionSequences sequences, GameMap map, GameEntityRecord chef, List<int> deps) {
            Sequences = sequences;
            Map = map;
            Chef = chef;
            Deps = deps;
        }

        public GameActionGraphBuilder Placeholder() {
            return new GameActionGraphBuilder(Sequences, Map, null, new List<int>());
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

        public GameActionGraphBuilder WithChef(GameEntityRecord chef) {
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

        public GameActionGraphBuilder Pickup(GameEntityRecord entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityReference(entity), IsPickup = true });
        }

        public GameActionGraphBuilder Pickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityReference(Sequences.NodeById[dep.Deps[0]].Action.ActionId), IsPickup = true });
        }

        public GameActionGraphBuilder PrepareToPickup(GameEntityRecord entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityReference(entity), Prepare = true });
        }

        public GameActionGraphBuilder PrepareToPickup(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityReference(Sequences.NodeById[dep.Deps[0]].Action.ActionId), Prepare = true });
        }
        public GameActionGraphBuilder PutOnto(GameEntityRecord entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityReference(entity) });
        }
        public GameActionGraphBuilder PutOnto(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't put onto multiple deps");
            }
            return AddAction(new InteractAction { Subject = new SpawnedEntityReference(Sequences.NodeById[dep.Deps[0]].Action.ActionId) });
        }

        public GameActionGraphBuilder GetFromCrate(GameEntityRecord entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityReference(entity), ExpectSpawn = true, IsPickup = true });
        }

        public GameActionGraphBuilder DoWork(GameEntityRecord entity) {
            return AddAction(new InteractAction { Subject = new LiteralEntityReference(entity), Primary = false });
        }

        public GameActionGraphBuilder ThrowTowards(GameEntityRecord entity, Vector2 bias = default) {
            return AddAction(new ThrowAction { Location = new EntityLocationToken(new LiteralEntityReference(entity)), Bias = bias });
        }

        public GameActionGraphBuilder ThrowTowards(int x, int y, Vector2 bias = default) {
            return AddAction(new ThrowAction { Location = new GridPosLocationToken(x, y), Bias = bias });
        }

        public GameActionGraphBuilder WaitForSpawn(GameEntityRecord entity) {
            return AddAction(new WaitForSpawnAction { Spawner = new LiteralEntityReference(entity) });
        }

        public GameActionGraphBuilder WaitForSpawn(GameActionGraphBuilder dep) {
            if (dep.Deps.Count != 1) {
                throw new ArgumentException("Can't pickup from multiple deps");
            }
            return AddAction(new WaitForSpawnAction { Spawner = new SpawnedEntityReference(Sequences.NodeById[dep.Deps[0]].Action.ActionId) });
        }

        public GameActionGraphBuilder WaitForDirtyPlate(GameEntityRecord dirtyPlateSpawner, int count) {
            return AddAction(new WaitForDirtyPlateAction { DirtyPlateSpawner = dirtyPlateSpawner, Count = count });
        }

        public GameActionGraphBuilder WaitForCleanPlate(GameEntityRecord dryingPart) {
            return AddAction(new WaitForCleanPlateAction { DryingPart = dryingPart });
        }

        public GameActionGraphBuilder WaitForCooked(GameEntityRecord entity) {
            return AddAction(new WaitForProgressAction { Entity = new LiteralEntityReference(entity), Progress = 10.0 });
        }

        public GameActionGraphBuilder WaitForMixed(GameEntityRecord entity) {
            return AddAction(new WaitForProgressAction { Entity = new LiteralEntityReference(entity), Progress = 12.0 });
        }

        public GameActionGraphBuilder Sleep(int frames) {
            return AddAction(new WaitAction { NumFrames = frames });
        }

        public GameActionGraphBuilder Catch(Vector2 direction) {
            return AddAction(new CatchAction { FacingDirection = direction });
        }

        private GameActionGraphBuilder AddAction(GameAction action) {
            action.Chef = Chef;
            // string diagInfo = "";
            // var trace = new StackTrace(true);
            // foreach (var frame in trace.GetFrames()) {
            //     if (frame != null) {
            //         // Console.WriteLine(frame!);
            //         if (frame!.GetFileName()?.Contains(".razor.cs") ?? false) {
            //             diagInfo += $"script: {frame.GetFileLineNumber()}:{frame.GetFileColumnNumber()}";
            //         }
            //     }
            // }
            // action.DiagInfo = diagInfo;
            var id = Sequences.AddAction(Chef, action, Deps);
            action.ActionId = id;
            return new GameActionGraphBuilder(Sequences, Map, Chef, new List<int> { id });
        }
    }
}