using System.Numerics;

namespace Hpmv {
    public class DropAction : GameAction {
        public LocationToken Location { get; set; }
        public double AngleAllowance { get; set; } = 0.999;

        public override string Describe() {
            return $"Drop towards {Location}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var ctrl = input.ControllerState;
            if (ctrl.PrimaryButtonDown) {
                if (ctrl.RequestButtonUp()) {
                    return new GameActionOutput {
                        ControllerInput = new DesiredControllerInput {
                            primaryUp = true
                        },
                        Done = true,
                    };
                }
                return default;
            }
            var chefFwd = Chef.rotation[input.Frame].ToForwardVector();
            var chefPos = Chef.position[input.Frame].XZ();
            var location = Location.GetLocation(input, Chef)[0];
            if (Vector2.Dot(chefFwd, Vector2.Normalize(location - chefPos)) >= AngleAllowance) {
                if (ctrl.RequestButtonDown()) {
                    return new GameActionOutput {
                        ControllerInput = new DesiredControllerInput {
                            primaryDown = true
                        },
                    };
                }
                return default;
            }
            var desiredDir = Vector2.Normalize(location - chefPos);
            return new GameActionOutput {
                ControllerInput = new DesiredControllerInput {
                    axes = new Vector2(desiredDir.X, -desiredDir.Y)
                }
            };
        }

        public new Save.DropAction ToProto() {
            return new Save.DropAction {
                Location = Location.ToProto(),
                AngleAllowance = AngleAllowance
            };
        }
    }

    public static class DropActionFromProto {
        public static DropAction FromProto(this Save.DropAction action, LoadContext context) {
            return new DropAction {
                Location = action.Location.FromProto(context),
                AngleAllowance = action.AngleAllowance
            };
        }
    }
}