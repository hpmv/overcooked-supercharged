using System.Numerics;

namespace Hpmv {
    public class ControllerInput {
        public int chef = 0;
        public Vector2 axes;
        public ButtonOutput primary;
        public ButtonOutput secondary;
        public ButtonOutput dash;
        public double GameSpeed = 0;
    }
}