namespace Hpmv {
    public interface IEntityReference {
        GameEntityRecord GetEntityRecord(GameActionInput input);
        PrefabRecord GetPrefabRecord();
    }
}