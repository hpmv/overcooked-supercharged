using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public struct GameActionOutput {
        public bool Done { get; set; }
        public DesiredControllerInput? ControllerInput { get; set; }
        public GameEntityRecord SpawningClaim { get; set; }
        public List<Vector2> PathDebug { get; set; }
    }
}