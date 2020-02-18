using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public class ThrowAction : GameAction {
        public LocationToken Location { get; set; }
        public Vector2 Bias { get; set; }
        public double AngleAllowance { get; set; } = 0.995;

        public override string Describe() {
            return $"Throw towards {Location}";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
            state.ChefId = Chef;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            state.Error = "";
            while (true) {
                var location = Location.GetLocation(context) [0] + Bias;

                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Error = "No such chef";
                    yield return null;
                    continue;
                }

                var stableFramesLeft = 3;
                var genPress = humanizer.PressButton(() => {
                    var chef = context.Entities.chefs[Chef];
                    var chefData = context.Entities.entities[Chef];
                    if (Vector2.Dot(Vector2.Normalize(chef.forward), Vector2.Normalize(location - chefData.pos.XZ())) >= AngleAllowance) {
                        if (--stableFramesLeft == 0) {
                            return true;
                        }
                    }
                    return false;
                });

                foreach (var press in genPress) {
                    var chefData = context.Entities.entities[Chef];
                    var desiredDir = Vector2.Normalize(location - chefData.pos.XZ());
                    if (!press.isDown) {
                        desiredDir = Vector2.Zero;
                    }
                    yield return new ControllerInput {
                        chef = Chef,
                            secondary = press,
                            axes = humanizer.HumanizeAxes(new Vector2(desiredDir.X, -desiredDir.Y))
                    };
                }
                yield break;
            }
        }
    }
}