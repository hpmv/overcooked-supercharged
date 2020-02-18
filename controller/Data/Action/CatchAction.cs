using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    class CatchAction : GameAction {
        public Vector2 FacingDirection { get; set; }
        public string Label { get; set; } = "?";

        public override string Describe() {
            return $"Catch {Label}";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            while (true) {
                state.Error = "";
                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Error = "No such chef";
                    yield return null;
                    continue;
                }

                {
                    var chefData = context.Entities.entities[Chef].data;
                    if (chefData.ContainsKey(EntityType.ChefCarry)) {
                        var carryData = chefData[EntityType.ChefCarry];
                        if (carryData is ChefCarryMessage msg) {
                            if (msg.m_carriableItem != 0) {
                                context.EntityIdReturnValueForAction[state.ActionId] = (int) msg.m_carriableItem;
                                yield break;
                            }
                        }
                    }
                }

                {
                    var chefData = context.Entities.chefs[Chef];
                    if (Vector2.Dot(Vector2.Normalize(chefData.forward), FacingDirection) < 0.85) {
                        yield return new ControllerInput {
                            chef = Chef,
                                axes = humanizer.HumanizeAxes(new Vector2(FacingDirection.X, -FacingDirection.Y))
                        };
                        continue;
                    }
                    yield return null;
                }
            }
        }
    }
}