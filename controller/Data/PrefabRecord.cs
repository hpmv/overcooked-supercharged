using System.Collections.Generic;

namespace Hpmv {
    public class PrefabRecord {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public readonly List<PrefabRecord> Spawns = new List<PrefabRecord>();
        public bool CanUse { get; set; }
        public bool IsCrate { get; set; }
        public double MaxProgress { get; set; }

        public PrefabRecord(string name, string className) {
            Name = name;
            ClassName = className;
        }

    }
}