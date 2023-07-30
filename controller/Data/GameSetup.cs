using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public class GameSetup {
        public GameMapGeometry geometry;
        public Dictionary<int, GameMap> mapByChef = new Dictionary<int, GameMap>();
        public GameEntityRecords entityRecords = new GameEntityRecords();
        public GameActionSequences sequences = new GameActionSequences();
        public InputHistory inputHistory = new InputHistory();
        public Dictionary<int, List<Vector2>> pathDebug = new Dictionary<int, List<Vector2>>();
        public int LastEmpiricalFrame { get; set; }
        public bool PreventInvalidState { get; set; } = false;

        public void RegisterChef(GameEntityRecord chef) {
            sequences.AddChef(chef);
            inputHistory.FrameInputs[chef] = new Versioned<ActualControllerInput>(default);
        }

        public bool IsValid() {
            return geometry != null;
        }

        public Save.GameSetup ToProto() {
            var result = new Save.GameSetup();
            foreach (var kv in mapByChef) {
                result.MapByChef.Add(kv.Key, kv.Value.ToProto());
            }
            result.Geometry = geometry.ToProto();
            result.Records = entityRecords.ToProto();
            result.Sequences = sequences.ToProto();
            result.InputHistory = inputHistory.ToProto();
            result.LastEmpiricalFrame = LastEmpiricalFrame;
            return result;
        }
    }

    public static class GameSetupFromProto {
        public static GameSetup FromProto(this Save.GameSetup proto) {
            var loadContext = new LoadContext();
            loadContext.Load(proto.Records);
            Dictionary<int, GameMap> mapByChef = new Dictionary<int, GameMap>();
            GameMapGeometry geometry;
            if (proto.Map != null) {
                // compatibility with old format
                geometry = new GameMapGeometry(proto.Map.TopLeft.FromProto(), new System.Numerics.Vector2());
                var map = proto.Map.FromProto(geometry);
                foreach (var chef in proto.Sequences.Chefs) {
                    mapByChef[chef.Chef.Path[0]] = map;
                }
            } else {
                geometry = proto.Geometry.FromProto();
                foreach (var map in proto.MapByChef) {
                    mapByChef[map.Key] = map.Value.FromProto(proto.Geometry.FromProto());
                }
            }
            return new GameSetup {
                entityRecords = loadContext.Records,
                geometry = geometry,
                mapByChef = mapByChef,
                sequences = proto.Sequences.FromProto(loadContext),
                inputHistory = proto.InputHistory.FromProto(loadContext),
                LastEmpiricalFrame = proto.LastEmpiricalFrame,
            };
        }
    }
}
