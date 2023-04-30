using System.Collections.Generic;

namespace Hpmv {
    public class GameSetup {
        public GameMapGeometry geometry;
        public Dictionary<int, GameMap> mapByChef = new Dictionary<int, GameMap>();
        public GameEntityRecords entityRecords = new GameEntityRecords();
        public GameActionSequences sequences = new GameActionSequences();
        public InputHistory inputHistory = new InputHistory();
        public int LastEmpiricalFrame { get; set; }
        public int LastSimulatedFrame { get; set; }

        public void RegisterChef(GameEntityRecord chef) {
            sequences.AddChef(chef);
            inputHistory.FrameInputs[chef] = new Versioned<ActualControllerInput>(default);
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
            result.LastSimulatedFrame = LastSimulatedFrame;
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
                LastSimulatedFrame = proto.LastSimulatedFrame
            };
        }
    }
}
