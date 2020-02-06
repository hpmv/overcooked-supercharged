using UnityEngine;

namespace Hpmv {
    public class HpmvIdentifiable : MonoBehaviour {
        public int id = -1;

        private static int nextId = 0;

        public void Start() {
            id = nextId++;
        }
    }
}