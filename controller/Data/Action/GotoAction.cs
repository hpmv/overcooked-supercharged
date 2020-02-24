using System;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class GotoAction : GameAction {
        public LocationToken DesiredPos { get; set; }
        public float AllowedError { get; set; } = 0.2f;
        public bool AllowDash { get; set; } = true;

        private const float DASH_TIME = 0.3f;
        private const float DASH_COOLDOWN = 0.4f;
        private const float RUN_SPEED = 6;
        private const float DASH_SPEED = 18;
        private const float USE_DASH_IF_REMAIN = 0.75f;

        public override GameActionOutput Step(GameActionInput input) {
            var time = DateTime.Now;
            try {
                var chefPos = Chef.position[input.Frame].XZ();
                var desired = DesiredPos.GetLocation(input);
                // Console.WriteLine($"Stepping for chef {Chef} and frame {input.Frame} to {desired.Length}");
                if (desired.Any(DesiredPos => (chefPos - DesiredPos).Length() < AllowedError)) {
                    return new GameActionOutput { Done = true };
                }
                var path = input.Map.FindPath(chefPos, desired.ToList());
                if (path.Count < 2) {
                    Console.WriteLine($"Failed to find path from {chefPos} to {string.Join(',', desired)}");
                    return new GameActionOutput();
                }
                var direction = (path[1] - path[0]) / (path[1] - path[0]).Length();
                var dash = false;
                var chefState = Chef.chefState[input.Frame];
                if (DASH_TIME - chefState.dashTimer < DASH_COOLDOWN) {
                    var dashDirection = chefState.forward * MathUtils.SinusoidalSCurve((float)chefState.dashTimer / DASH_TIME) * DASH_SPEED;
                    // TODO: what to do here exactly?
                } else {
                    double remainDistance = 0;
                    for (int i = 1; i < path.Count; i++) {
                        remainDistance += (path[i - 1] - path[i]).Length();
                    }
                    if (AllowDash && remainDistance > USE_DASH_IF_REMAIN * DASH_SPEED * DASH_TIME * 0.5) {
                        if (Vector2.Dot(Vector2.Normalize(chefState.forward), direction) > 0.8) {
                            dash = true;
                        }
                    }
                }

                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = new Vector2(direction.X, -direction.Y),
                        dash = dash
                    }
                };
            } finally {
                // Console.WriteLine($"Took {(DateTime.Now - time).TotalMilliseconds} ms");
            }
        }

        public override string Describe() {
            return $"Goto {DesiredPos}";
        }
    }
}