using System.Numerics;

namespace Hpmv {
    public struct ActualControllerInput {
        public Vector2 axes;
        public ButtonOutput primary;
        public ButtonOutput secondary;
        public ButtonOutput dash;
    }
}