using System.Numerics;

namespace Hpmv {
    public class CatchAction : GameAction {
        public Vector2 FacingPosition { get; set; }

        public override string Describe() {
            return $"Catch towards {FacingPosition}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            if (Chef.data[input.Frame].attachment != null) {
                return new GameActionOutput { Done = true };
            }
            var chefForward = Chef.rotation[input.Frame].ToForwardVector();
            var chefPosition = Chef.position[input.Frame].XZ();
            var facingDirection = Vector2.Normalize(FacingPosition - chefPosition);
            if (Vector2.Dot(chefForward, facingDirection) < 0.85) {
                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = new Vector2(facingDirection.X, -facingDirection.Y)
                    }
                };
            }
            return default;
        }

        public new Save.CatchAction ToProto() {
            return new Save.CatchAction {
                FacingPosition = FacingPosition.ToProto(),
            };
        }
    }

    public static class CatchActionFromProto {
        public static CatchAction FromProto(this Save.CatchAction action) {
            return new CatchAction {
                FacingPosition = action.FacingPosition.FromProto(),
            };
        }
    }
}