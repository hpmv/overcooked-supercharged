using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public class ThrowAction : GameAction {
        public int Chef { get; set; }
        public LocationToken Location { get; set; }
        public Vector2 Bias { get; set; }
        public double AngleAllowance { get; set; } = 0.995;

        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.ChefId = Chef;
            state.Description = $"Chef {Chef} throw towards {Location}" + (Bias == Vector2.Zero ? "" : $" with bias {Bias}");
        }

        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            while (true) {
                var location = Location.GetLocation(context).XZ() + Bias;
                state.Description = $"Chef {Chef} throw towards {location}";

                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Description += " (no such chef)";
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