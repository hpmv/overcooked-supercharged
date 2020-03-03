namespace Hpmv {
    public class GameSetup {
        public GameMap map;
        public GameEntityRecords entityRecords = new GameEntityRecords();
        public GameActionSequences sequences = new GameActionSequences();
        public InputHistory inputHistory = new InputHistory();

        public void RegisterChef(GameEntityRecord chef) {
            sequences.AddChef(chef);
            inputHistory.FrameInputs[chef] = new Versioned<ActualControllerInput>(default);
        }
    }
}
