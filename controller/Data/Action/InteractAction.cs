using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    class InteractAction : GameAction {
        public EntityToken Subject { get; set; }
        public bool Primary { get; set; } = true;
        public bool Prepare { get; set; } = false;
        public bool IsPickup { get; set; } = false;
        public bool ExpectSpawn { get; set; } = false;
        public string SpawnLabel { get; set; } = "";

        public override string Describe() {
            if (IsPickup) {
                if (ExpectSpawn) {
                    return $"Get {SpawnLabel} from {Subject}";
                }
                return $"Pickup {Subject}";
            } else if (Primary) {
                return $"Put {Subject}";
            } else {
                return $"Use {Subject}";
            }
        }

        public int GetInteractableEntityId(GameActionContext context) {
            var id = Subject.GetEntityId(context);
            while (true) {
                if (context.Entities.entities.ContainsKey(id)) {
                    var data = context.Entities.entities[id].data;
                    if (data.ContainsKey(EntityType.PhysicalAttach)) {
                        var attach = data[EntityType.PhysicalAttach] as PhysicalAttachMessage;
                        if (attach.m_parent != -1) {
                            id = attach.m_parent;
                            continue;
                        }
                    }
                }
                return id;
            }
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
            state.ChefId = Chef;
        }

        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            GotoAction gotoAction = new GotoAction { Chef = Chef };
            GameActionState innerState = new GameActionState();
            gotoAction.InitializeState(innerState);
            var enumerator = gotoAction.Perform(innerState, context);
            int streak = 0;
            while (true) {
                var subjectEntity = GetInteractableEntityId(context);
                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Error = "No such chef";
                    yield return null;
                    continue;
                }
                if (!context.Entities.entities.ContainsKey(subjectEntity)) {
                    state.Error = "No such subject";
                    yield return null;
                    continue;
                }

                var chef = context.Entities.entities[Chef].pos.XZ();
                var subject = context.Entities.entities[subjectEntity].pos.XZ();
                gotoAction.DesiredPos = new InteractionPointsLocationToken(new LiteralEntityToken(subjectEntity));

                if (Primary) {
                    if (context.Entities.chefs[Chef].highlightedForPlacement == subjectEntity ||
                        context.Entities.chefs[Chef].highlightedForPickup == subjectEntity) {
                        if (Prepare) {
                            yield break;
                        }
                        streak++;
                        if (streak == 2) {
                            var genPress = IsPickup ? humanizer.PressButtonForPickup() : humanizer.PressButton(() => true);
                            foreach (var output in genPress) {
                                yield return new ControllerInput {
                                    chef = Chef,
                                        primary = output
                                };
                            }

                            if (ExpectSpawn) {
                                state.Error = "Waiting for spawn";
                                while (true) {
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
                                    yield return null;
                                }
                            }
                            yield break;
                        }
                    } else {
                        streak = 0;
                    }
                } else {
                    if (context.Entities.chefs[Chef].highlightedForUse == subjectEntity) {
                        if (Prepare) {
                            yield break;
                        }
                        streak++;
                        if (streak == 2) {
                            foreach (var output in humanizer.PressButton(() => true)) {
                            yield return new ControllerInput {
                            chef = Chef,
                            secondary = output
                                };
                            }
                            yield break;
                        }
                    } else {
                        streak = 0;
                    }
                }
                if (enumerator == null || !enumerator.MoveNext()) {
                    enumerator = null;
                    var dir = (subject - chef) / (subject - chef).Length();
                    state.Error = "Finetuning position";
                    yield return new ControllerInput {
                        chef = Chef,
                            axes = humanizer.HumanizeAxes(new Vector2(dir.X, -dir.Y))
                    };
                } else {
                    state.Error = innerState.Error;
                    yield return enumerator.Current;
                }
            }

        }
    }
}