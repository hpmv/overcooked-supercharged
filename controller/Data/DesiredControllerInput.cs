using System.Numerics;

namespace Hpmv {
    public struct DesiredControllerInput {
        public Vector2 axes;
        public bool primaryDown;
        public bool primaryDownIsForPickup;
        public bool primaryUp;
        public bool secondaryDown;
        public bool secondaryUp;
        public bool dash;
    }
}