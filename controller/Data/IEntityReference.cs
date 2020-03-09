namespace Hpmv {
    public interface IEntityReference {
        GameEntityRecord GetEntityRecord(GameActionInput input);
        // PrefabRecord GetPrefabRecord();
        Save.EntityReference ToProto();
    }

    public static class IEntityReferenceFromProto {
        public static IEntityReference FromProto(this Save.EntityReference r, LoadContext context) {
            if (r.KindCase == Save.EntityReference.KindOneofCase.Literal) {
                return new LiteralEntityReference(r.Literal.FromProtoRef(context));
            } else {
                return new SpawnedEntityReference(r.Spawner);
            }
        }
    }
}