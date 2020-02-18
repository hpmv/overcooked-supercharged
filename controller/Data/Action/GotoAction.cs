using System;
using System.Collections.Generic;
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

        public override void InitializeState(GameActionState state) {
            state.Action = this;
            state.ChefId = Chef;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            var desired = DesiredPos.GetLocation(context);
            while (true) {
                state.Error = "";
                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Error = "No such chef";
                    yield return null;
                    continue;
                }
                var chef = context.Entities.chefs[Chef];
                var chefPos = context.Entities.entities[Chef].pos.XZ();
                if (desired.Any(DesiredPos => (chefPos - DesiredPos).Length() < AllowedError)) {
                    yield break;
                }
                var path = context.Map.FindPath(chefPos, desired.ToList());
                if (path.Count < 2) {
                    Console.WriteLine($"Failed to find path from {chefPos} to {DesiredPos}");
                    state.Error = "Failed to find path";
                    yield return null;
                    continue;
                }
                var direction = (path[1] - path[0]) / (path[1] - path[0]).Length();
                var dash = false;
                if (DASH_TIME - chef.dashTimer < DASH_COOLDOWN) {
                    var dashDirection = chef.forward * MathUtils.SinusoidalSCurve((float) chef.dashTimer / DASH_TIME) * DASH_SPEED;
                    // TODO: what to do here exactly?
                } else {
                    double remainDistance = 0;
                    for (int i = 1; i < path.Count; i++) {
                        remainDistance += (path[i - 1] - path[i]).Length();
                    }
                    if (AllowDash && remainDistance > USE_DASH_IF_REMAIN * DASH_SPEED * DASH_TIME * 0.5) {
                        if (Vector2.Dot(Vector2.Normalize(chef.forward), direction) > 0.8) {
                            dash = true;
                        }
                    }
                }

                yield return new ControllerInput {
                    chef = Chef,
                        axes = humanizer.HumanizeAxes(new Vector2(direction.X, -direction.Y)),
                        dash = new ButtonOutput { justPressed = dash }
                };
            }
        }

        public override string Describe() {
            return $"Goto {DesiredPos}";
        }
    }
}