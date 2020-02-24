using System.Linq;

namespace Hpmv {
    public class EntityPath {
        public int id;
        public EntityPath parent;

        public override string ToString() {
            if (parent == null) {
                return id.ToString();
            }
            return $"{parent}.{id}";
        }

        public static int[] ParseEntityPath(string s) => s.Split('.').Select(p => int.Parse(p)).ToArray();
    }
}