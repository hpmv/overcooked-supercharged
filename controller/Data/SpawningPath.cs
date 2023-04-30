
using System.Collections.Generic;

namespace Hpmv
{
    public class SpawningPath
    {
        public int InitialFixedEntityId { get; set; }
        public List<int> SpawnableIds { get; set; } = new List<int>();

        public Hpmv.Save.SpawningPath ToProto()
        {
            var proto = new Hpmv.Save.SpawningPath();
            proto.InitialFixedEntityId = InitialFixedEntityId;
            proto.SpawnableIds.AddRange(SpawnableIds);
            return proto;
        }

        public SpawningPath Concat(int spawnableId) {
            var result = new SpawningPath {
                InitialFixedEntityId = InitialFixedEntityId,
            };
            result.SpawnableIds.AddRange(SpawnableIds);
            result.SpawnableIds.Add(spawnableId);
            return result;
        }
    }

    public static class SpawningPathFromProto {
        public static SpawningPath FromProto(this Hpmv.Save.SpawningPath proto) {
            var result = new SpawningPath {
                InitialFixedEntityId = proto.InitialFixedEntityId,
            };
            result.SpawnableIds.AddRange(proto.SpawnableIds);
            return result;
        }
    }
}
