namespace Hpmv {
    public class GameSetup {
        public GameMap map;
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
            result.Map = map.ToProto();
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
            return new GameSetup {
                entityRecords = loadContext.Records,
                map = proto.Map.FromProto(),
                sequences = proto.Sequences.FromProto(loadContext),
                inputHistory = proto.InputHistory.FromProto(loadContext),
                LastEmpiricalFrame = proto.LastEmpiricalFrame,
                LastSimulatedFrame = proto.LastSimulatedFrame
            };
        }
    }
}
