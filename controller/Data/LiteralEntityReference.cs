namespace Hpmv {
    public class LiteralEntityReference : IEntityReference {
        public GameEntityRecord Record;

        public LiteralEntityReference(GameEntityRecord record) {
            Record = record;
        }

        public GameEntityRecord GetEntityRecord(GameActionInput input) {
            return Record;
        }

        public PrefabRecord GetPrefabRecord() {
            return Record.prefab;
        }

        public override string ToString() {
            return Record.displayName;
        }
    }
}