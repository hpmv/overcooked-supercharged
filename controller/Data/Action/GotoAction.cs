using System;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GotoAction : GameAction {
        public LocationToken DesiredPos { get; set; }
        public float AllowedError { get; set; } = 0.05f;
        public bool DisallowDash { get; set; } = false;
        public bool DisallowOvershoot { get; set; } = false;

        private const float DASH_TIME = 0.3f;
        private const float DASH_COOLDOWN = 0.4f;
        private const float RUN_SPEED = 6;
        private const float DASH_SPEED = 18;
        private const float USE_DASH_IF_REMAIN = 0.75f;
        private const float DASH_INTEGRAL = 0.5f;  // integral of 1/2 (1 - cos (pi x)) where x is from 0 to 1

        public override GameActionOutput Step(GameActionInput input) {
            var time = DateTime.Now;
            try {
                var chefPos = Chef.position[input.Frame].XZ();
                var chefForward = Chef.rotation[input.Frame].ToForwardVector();
                var chefState = Chef.chefState[input.Frame];
                var desired = DesiredPos.GetLocation(input, Chef);
                // Console.WriteLine($"Stepping for chef {Chef} and frame {input.Frame} to {desired.Length}");
                if (desired.Any(DesiredPos => {
                        if (Vector2.Dot(Vector2.Normalize(DesiredPos - chefPos), chefForward) > 0.5) {
                            return (chefPos - DesiredPos).Length() < AllowedError;
                        } else {
                            return (chefPos - DesiredPos).Length() < AllowedError * 4;
                        }
                    })) {
                    return new GameActionOutput { Done = true };
                }
                var path = input.MapByChef[Chef.path.ids[0]].FindPath(chefPos, desired.ToList());
                Vector2 direction;
                if (path.Count < 2) {
                    // Console.WriteLine($"Failed to find path from {chefPos} to {string.Join(',', desired)}");
                    var approxTarget = desired.Aggregate((a, b) => a + b) / desired.Length;
                    direction = Vector2.Normalize(approxTarget - chefPos);
                } else {
                    direction = (path[1] - path[0]) / (path[1] - path[0]).Length();
                }

                if (Chef.chefState[input.Frame].movementInputSuppressed) {
                    if (Vector2.Dot(Chef.chefState[input.Frame].lastMoveInputDirection.XZ(), direction) > Math.Cos(15 * Math.PI / 180)) {
                        direction = Vector2.TransformNormal(Chef.chefState[input.Frame].lastMoveInputDirection.XZ(), Matrix3x2.CreateRotation(16 * (float)Math.PI / 180));
                    }
                }

                var dash = false;
                double remainDistance = 0;
                for (int i = 1; i < path.Count; i++) {
                    remainDistance += (path[i - 1] - path[i]).Length();
                }
                var shouldRelyOnDashOnly = false;
                if (chefState.dashTimer > 0) {
                    var dashT = (float)chefState.dashTimer / DASH_TIME;
                    var remainingDashDistance = 0.5f * (dashT - Math.Sin(dashT * Math.PI) / Math.PI) * DASH_SPEED * DASH_TIME;
                    if (path.Count > 0 && remainingDashDistance > remainDistance && Vector2.Dot(chefForward, path[0]) > 0.99) {
                        shouldRelyOnDashOnly = true;
                    }
                } else if (DASH_TIME - chefState.dashTimer >= DASH_COOLDOWN) {
                    var useDashIfRemain = DisallowOvershoot ? 1 : USE_DASH_IF_REMAIN;
                    if (!DisallowDash && remainDistance > useDashIfRemain * DASH_SPEED * DASH_TIME * DASH_INTEGRAL) {
                        if (Vector2.Dot(chefForward, direction) > 0.9999) {
                            dash = true;
                        }
                    }
                }

                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = shouldRelyOnDashOnly ? Vector2.Zero : new Vector2(direction.X, -direction.Y),
                        dash = dash,
                    },
                    PathDebug = path
                };
            } finally {
                // Console.WriteLine($"Took {(DateTime.Now - time).TotalMilliseconds} ms");
            }
        }

        public override string Describe() {
            return $"Goto {DesiredPos}";
        }

        public new Save.GotoAction ToProto() {
            return new Save.GotoAction {
                Location = DesiredPos.ToProto(),
                AllowedError = AllowedError,
                DisallowDash = DisallowDash,
                DisallowOvershoot = DisallowOvershoot
            };
        }
    }

    public static class GotoActionFromProto {
        public static GotoAction FromProto(this Save.GotoAction action, LoadContext context) {
            return new GotoAction {
                DesiredPos = action.Location.FromProto(context),
                AllowedError = 0.05f,  // TODO: temporary override
                DisallowDash = action.DisallowDash,
                DisallowOvershoot = action.DisallowOvershoot
            };
        }
    }
}