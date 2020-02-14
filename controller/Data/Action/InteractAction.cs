using System.Collections.Generic;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    class InteractAction : GameAction {
        public int Chef { get; set; }
        public EntityToken Subject { get; set; }
        public bool Primary { get; set; } = true;
        public bool Prepare { get; set; } = false;
        public bool ExpectSpawn { get; set; } = false;

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

        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.ChefId = Chef;
            state.Description = Primary ? $"Chef {Chef} Interact with {Subject}" : $"Work on {Subject}";
        }

        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var humanizer = context.ChefHumanizers[Chef];
            GotoAction gotoAction = new GotoAction { Chef = Chef };
            GameActionState innerState = new GameActionState();
            gotoAction.InitializeState(innerState);
            var enumerator = gotoAction.Perform(innerState, context);
            while (true) {
                var subjectEntity = GetInteractableEntityId(context);
                state.Description = Primary ? $"Chef {Chef} Interact with {subjectEntity}" : $"Work on {subjectEntity}";
                if (!context.Entities.entities.ContainsKey(Chef) || !context.Entities.chefs.ContainsKey(Chef)) {
                    state.Description += " (no such chef)";
                    yield return null;
                    continue;
                }
                if (!context.Entities.entities.ContainsKey(subjectEntity)) {
                    state.Description += " (no such subject)";
                    yield return null;
                    continue;
                }

                var chef = context.Entities.entities[Chef].pos.XZ();
                var subject = context.Entities.entities[subjectEntity].pos.XZ();
                var targets = context.Map.GetInteractionPointsForBlockEntity(subject);
                gotoAction.DesiredPosList = targets;

                if (Primary) {
                    if (context.Entities.chefs[Chef].highlightedForPlacement == subjectEntity) {
                        if (Prepare) {
                            yield break;
                        }
                        state.Description += " Picking/placing";
                        foreach (var output in humanizer.PressButton(() => true)) {
                            yield return new ControllerInput {
                            chef = Chef,
                            primary = output
                            };
                        }

                        if (ExpectSpawn) {
                            state.Description += " Waiting for spawn";
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
                    if (context.Entities.chefs[Chef].highlightedForUse == subjectEntity) {
                        if (Prepare) {
                            yield break;
                        }
                        state.Description += " interacting";
                        foreach (var output in humanizer.PressButton(() => true)) {
                            yield return new ControllerInput {
                            chef = Chef,
                            secondary = output
                            };
                        }
                        yield break;
                    }
                }
                if (enumerator == null || !enumerator.MoveNext()) {
                    enumerator = null;
                    var dir = (subject - chef) / (subject - chef).Length();
                    state.Description += " Finetuning position";
                    yield return new ControllerInput {
                        chef = Chef,
                            axes = humanizer.HumanizeAxes(new Vector2(dir.X, -dir.Y))
                    };
                } else {
                    state.Description += " " + innerState.Description;
                    yield return enumerator.Current;
                }
            }

        }
    }
}