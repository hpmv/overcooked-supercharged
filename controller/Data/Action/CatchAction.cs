using System.Numerics;

namespace Hpmv {
    class CatchAction : GameAction {
        public Vector2 FacingDirection { get; set; }
        public string Label { get; set; } = "?";

        public override string Describe() {
            return $"Catch {Label}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            if (Chef.data[input.Frame].carriedItem != null) {
                return new GameActionOutput { Done = true };
            }
            var chefState = Chef.chefState[input.Frame];
            if (Vector2.Dot(Vector2.Normalize(chefState.forward), FacingDirection) < 0.85) {
                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = new Vector2(FacingDirection.X, -FacingDirection.Y)
                    }
                };
            }
            return default;
        }
    }
}