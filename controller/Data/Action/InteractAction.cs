using System.Numerics;

namespace Hpmv {
    class InteractAction : GameAction, ISpawnClaimingAction {
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
            while (true) {
                var data = entity.data[input.Frame];
                if (data.attachmentParent != null) {
                    entity = data.attachmentParent;
                    continue;
                }
                return entity;
            }
        }

        public override GameActionOutput Step(GameActionInput input) {
            var subjectEntity = GetInteractableEntity(input);
            var chefState = Chef.chefState[input.Frame];

            if (Primary) {
                if (input.ControllerState.PrimaryButtonDown) {
                    if (ExpectSpawn) {
                        foreach (var child in subjectEntity.spawned) {
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
                DesiredPos = new InteractionPointsLocationToken(Subject)
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
    }
}