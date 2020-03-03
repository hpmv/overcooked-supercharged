using System;
using System.Numerics;

namespace Hpmv {
    public class InteractAction : GameAction, ISpawnClaimingAction {
        public IEntityReference Subject { get; set; }
        public bool Primary { get; set; } = true;
        public bool Prepare { get; set; } = false;
        public bool IsPickup { get; set; } = false;
        public bool ExpectSpawn { get; set; } = false;
        public string SpawnLabel { get; set; } = "";

        public IEntityReference GetSpawner() {
            return Subject;
        }

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

        public GameEntityRecord GetInteractableEntity(GameActionInput input) {
            var entity = Subject.GetEntityRecord(input);
            // Console.WriteLine($"[{input.Frame}] Entity for #{ActionId} is {entity}");
            while (true) {
                var data = entity.data[input.Frame];
                if (data.attachmentParent != null) {
                    entity = data.attachmentParent;
                    continue;
                }
                // Console.WriteLine($"[{input.Frame}] Attachment root for #{ActionId} is {entity}");
                return entity;
            }
        }

        public override GameActionOutput Step(GameActionInput input) {
            var subjectEntity = GetInteractableEntity(input);
            // Console.WriteLine($"[{input.Frame}] interactable entity for {Subject} is {subjectEntity.displayName}");
            var chefState = Chef.chefState[input.Frame];

            if (Primary) {
                if (input.ControllerState.PrimaryButtonDown) {
                    if (ExpectSpawn) {
                        foreach (var child in subjectEntity.spawned) {
                            // Console.WriteLine($"{child}: {child.existed[input.Frame]}, {child.spawnOwner[input.Frame]}");
                            if (child.existed[input.Frame] && child.spawnOwner[input.Frame] == null) {
                                if (input.ControllerState.RequestButtonUp()) {
                                    return new GameActionOutput {
                                        ControllerInput = new DesiredControllerInput {
                                            primaryUp = true,
                                        },
                                        SpawningClaim = child,
                                        Done = true
                                    };
                                }
                            }
                        }
                    } else {
                        if (input.ControllerState.RequestButtonUp()) {
                            return new GameActionOutput {
                                ControllerInput = new DesiredControllerInput {
                                    primaryUp = true,
                                },
                                Done = true
                            };
                        }
                    }
                    return default;
                }

                if (chefState.highlightedForPickup == subjectEntity || chefState.highlightedForPlacement == subjectEntity) {
                    if (Prepare) {
                        return new GameActionOutput {
                            Done = true
                        };
                    }
                    // TODO: We can't store state for streak here, so we'd have to predict the highlight in the next frame
                    // with some humanizer information. For now just be inaccurate.
                    if (IsPickup ? input.ControllerState.RequestButtonDownForPickup() : input.ControllerState.RequestButtonDown()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                primaryDown = true,
                                primaryDownIsForPickup = IsPickup
                            }
                        };
                    }
                    return default;
                }


            } else {
                if (input.ControllerState.SecondaryButtonDown) {
                    if (input.ControllerState.RequestButtonUp()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                secondaryUp = true,
                            },
                            Done = true
                        };
                    }
                    return default;
                }

                if (chefState.highlightedForUse == subjectEntity) {
                    if (Prepare) {
                        return new GameActionOutput {
                            Done = true
                        };
                    }
                    if (input.ControllerState.RequestButtonDown()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                secondaryDown = true,
                            }
                        };
                    }
                    return default;
                }

            }

            GotoAction gotoAction = new GotoAction {
                Chef = Chef,
                DesiredPos = new InteractionPointsLocationToken(new LiteralEntityReference(subjectEntity))
            };

            var output = gotoAction.Step(input);
            var dir = Vector2.Normalize(subjectEntity.position[input.Frame].XZ() - Chef.position[input.Frame].XZ());
            if (output.Done) {
                return new GameActionOutput {
                    ControllerInput = new DesiredControllerInput {
                        axes = new Vector2(dir.X, -dir.Y)
                    }
                };
            }
            return output;
        }

        public Save.InteractAction ToProto() {
            return new Save.InteractAction {
                Subject = Subject.ToProto(),
                Primary = Primary,
                Prepare = Prepare,
                IsPickup = IsPickup,
                ExpectSpawn = ExpectSpawn,
                SpawnLabel = SpawnLabel
            };
        }
    }

    public static class InteractActionFromProto {
        public static InteractAction FromProto(this Save.InteractAction action, LoadContext context) {
            return new InteractAction {
                Subject = action.Subject.FromProto(context),
                Primary = action.Primary,
                Prepare = action.Prepare,
                IsPickup = action.IsPickup,
                ExpectSpawn = action.ExpectSpawn,
                SpawnLabel = action.SpawnLabel
            };
        }
    }
}