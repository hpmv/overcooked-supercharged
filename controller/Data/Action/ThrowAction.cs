using System.Numerics;

namespace Hpmv {
    public class ThrowAction : GameAction {
        public LocationToken Location { get; set; }
        public Vector2 Bias { get; set; }
        public double AngleAllowance { get; set; } = 0.999;

        public override string Describe() {
            return $"Throw towards {Location}";
        }

        public override GameActionOutput Step(GameActionInput input) {
            var ctrl = input.ControllerState;
            var chefFwd = Chef.chefState[input.Frame].forward;
            var chefPos = Chef.position[input.Frame].XZ();
            var location = Location.GetLocation(input)[0] + Bias;
            if (ctrl.SecondaryButtonDown) {
                if (Vector2.Dot(Vector2.Normalize(chefFwd), Vector2.Normalize(location - chefPos)) >= AngleAllowance) {
                    if (ctrl.RequestButtonUp()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                secondaryUp = true
                            },
                            Done = true
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
            if (ctrl.RequestButtonDown()) {
                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        secondaryDown = true
                    }
                };
            }
            return default;
        }

        public Save.ThrowAction ToProto() {
            return new Save.ThrowAction {
                Location = Location.ToProto(),
                Bias = Bias.ToProto(),
                AngleAllowance = AngleAllowance
            };
        }
    }

    public static class ThrowActionFromProto {
        public static ThrowAction FromProto(this Save.ThrowAction action, LoadContext context) {
            return new ThrowAction {
                Location = action.Location.FromProto(context),
                Bias = action.Bias.FromProto(),
                AngleAllowance = action.AngleAllowance
            };
        }
    }
}