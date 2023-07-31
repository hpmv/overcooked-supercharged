using System.Linq;

namespace Hpmv {

    public class PickupClosestAction : GameAction
    {
        public string PrefabName { get; set; }

        public override string Describe()
        {
            return $"Pickup closest {PrefabName} from floor";
        }

        public override GameActionOutput Step(GameActionInput input)
        {
            if (input.ControllerState.primaryButtonDown) {
                if (input.ControllerState.RequestButtonUp()) {
                    return new GameActionOutput {
                        ControllerInput = new DesiredControllerInput {
                            primaryUp = true,
                        },
                        Done = true,
                    };
                } else {
                    return new GameActionOutput {};
                }
            }

            var chefState = Chef.chefState[input.Frame];
            if (chefState.highlightedForPickup is GameEntityRecord highlighted) {
                if (highlighted.prefab.Name == PrefabName) {
                    if (input.ControllerState.RequestButtonDownForPickup()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                primaryDown = true,
                                primaryDownIsForPickup = true,
                            },
                        };
                    } else {
                        return new GameActionOutput {};
                    }
                }
            }

            var eligibleEntities = input.Entities.GenAllEntities().Where(e => {
                var data = e.data[input.Frame];
                return e.prefab.Name == PrefabName && e.existed[input.Frame] && data.attachmentParent == null && !data.throwableItem.IsFlying;
            });
            var closestEntity = eligibleEntities.OrderBy(e => (e.position[input.Frame] - Chef.position[input.Frame]).LengthSquared()).FirstOrDefault();
            if (closestEntity == null) {
                return new GameActionOutput {
                    Done = true
                };
            }

            var gotoAction = new GotoAction {
                Chef = Chef,
                AllowDash = false,
                DesiredPos = new LiteralLocationToken(closestEntity.position[input.Frame].XZ()),
            };
            return gotoAction.Step(input);
        }

        public new Save.PickupClosestAction ToProto()
        {
            return new Save.PickupClosestAction {
                PrefabName = PrefabName,
            };
        }
    }

    public static class PickupClosestActionFromProto
    {
        public static PickupClosestAction FromProto(this Save.PickupClosestAction action, LoadContext context)
        {
            return new PickupClosestAction {
                PrefabName = action.PrefabName,
            };
        }
    }
}