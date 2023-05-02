using System;
using System.Linq;

namespace Hpmv {
    public class EntityPath {
        public int[] ids;

        public override string ToString() {
            return string.Join(".", ids);
        }

        public static int[] ParseEntityPath(string s) => s.Split('.').Select(p => int.Parse(p)).ToArray();

        public Save.EntityPath ToProto() {
            var result = new Save.EntityPath();
            result.Path.AddRange(ids);
            return result;
        }

        internal EntityPathReference ToThrift()
        {
            return new EntityPathReference { Ids = ids.ToList() };
        }
    }

    public static class EntityPathFromProto {
        public static EntityPath FromProto(this Save.EntityPath path) {
            return new EntityPath { ids = path.Path.ToArray() };
        }
    }
}