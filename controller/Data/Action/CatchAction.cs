using System.Numerics;

namespace Hpmv {
    public class CatchAction : GameAction {
        public Vector2 FacingDirection { get; set; }
        public string Label { get; set; } = "?";

        public override string Describe() {
            return $"Catch {Label}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            if (Chef.data[input.Frame].attachment != null) {
                return new GameActionOutput { Done = true };
            }
            var chefForward = Chef.rotation[input.Frame].ToForwardVector();
            if (Vector2.Dot(chefForward, FacingDirection) < 0.85) {
                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = new Vector2(FacingDirection.X, -FacingDirection.Y)
                    }
                };
            }
            return default;
        }

        public Save.CatchAction ToProto() {
            return new Save.CatchAction {
                FacingDirection = FacingDirection.ToProto(),
                Label = Label
            };
        }
    }

    public static class CatchActionFromProto {
        public static CatchAction FromProto(this Save.CatchAction action) {
            return new CatchAction {
                FacingDirection = action.FacingDirection.FromProto(),
                Label = action.Label
            };
        }
    }
}