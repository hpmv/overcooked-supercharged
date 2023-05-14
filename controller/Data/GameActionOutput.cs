namespace Hpmv {
    public struct GameActionOutput {
        public bool Done { get; set; }
        public DesiredControllerInput? ControllerInput { get; set; }
        public GameEntityRecord SpawningClaim { get; set; }
    }
}