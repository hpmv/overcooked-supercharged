using System;
using System.Numerics;

namespace Hpmv {
    public class InteractAction : GameAction {
        public IEntityReference Subject { get; set; }
        public bool Primary { get; set; } = true;
        public bool Prepare { get; set; } = false;
        public bool IsPickup { get; set; } = false;
        public bool ExpectSpawn { get; set; } = false;

        public override string Describe() {
            if (IsPickup) {
                if (ExpectSpawn) {
                    return $"Get from {Subject}";
                }
                return $"Pickup {Subject}";
            } else if (Primary) {
                if (Prepare) {
                    return "Prepare to interact with {Subject}";
                }
                return $"Put {Subject}";
            } else {
                return $"Use {Subject}";
            }
        }

        public GameEntityRecord GetInteractableEntity(GameActionInput input) {
            var entity = Subject.GetEntityRecord(input);
            // Console.WriteLine($"[{input.Frame}] Entity for #{ActionId} is {entity}");
            while (entity != null) {
                var data = entity.data[input.Frame];
                if (data.attachmentParent != null) {
                    entity = data.attachmentParent;
                    continue;
                }
                // Console.WriteLine($"[{input.Frame}] Attachment root for #{ActionId} is {entity}");
                return entity;
            }
            return entity;
        }

        private DesiredControllerInput CalculateInputRegardlessOfHighlighting(GameEntityRecord subjectEntity, GameActionInput input) {
            GotoAction gotoAction = new GotoAction {
                Chef = Chef,
                DesiredPos = new InteractionPointsLocationToken(new LiteralEntityReference(subjectEntity))
            };

            var output = gotoAction.Step(input);
            var dir = Vector2.Normalize(subjectEntity.position[input.Frame].XZ() - Chef.position[input.Frame].XZ());
            if (output.Done) {
                return new DesiredControllerInput {
                    axes = new Vector2(dir.X, -dir.Y)
                };
            }
            return output.ControllerInput.Value;
        }

        public override GameActionOutput Step(GameActionInput input) {
            var subjectEntity = GetInteractableEntity(input);
            if (subjectEntity == null) {
                return new GameActionOutput();
            }
            // Console.WriteLine($"[{input.Frame}] interactable entity for {Subject} is {subjectEntity.displayName}");
            var chefState = Chef.chefState[input.Frame];
            var chefForward = Chef.rotation[input.Frame].ToForwardVector();

            var gotoInput = CalculateInputRegardlessOfHighlighting(subjectEntity, input);
            var (predictedPosition, predictedVelocity, predictedForward) =
                OfflineCalculations.PredictChefPositionAfterInput(
                    chefState,
                    Chef.position[input.Frame],
                    Chef.velocity[input.Frame],
                    chefForward,
                    input.MapByChef[Chef.path.ids[0]],
                    input.ControllerState.axes);
            // var predictedPosition2 = OfflineEmulator.CalculateNewChefPositionAfterMovement(predictedPosition, predictedVelocity, input.Map);
            var predictedHighlightingOnNextFrame =
                OfflineCalculations.CalculateHighlightedObjects(predictedPosition, predictedForward, input.Geometry, input.Entities.GenAllEntities());
            // var predictedHighlightingOnNextNextFrame =
            //     OfflineEmulator.CalculateHighlightedObjects(predictedPosition2, predictedForward, input.Map, input.Entities.GenAllEntities());
            // var prepareToInteractHighlightings = OfflineEmulator.CalculateHighlightedObjects(Chef.position.Last().XZ(), chefState.forward, input.Map, input.Entities.GenAllEntities());

            var gridPosOfChef = input.Geometry.CoordsToGridPosRounded(Chef.position.Last().XZ());
            var gridPosOfEntity = input.Geometry.CoordsToGridPosRounded(subjectEntity.position.Last().XZ());
            var gridDirection = input.Geometry.GridPos(gridPosOfEntity.x, gridPosOfEntity.y) - input.Geometry.GridPos(gridPosOfChef.x, gridPosOfChef.y);
            var directionCheck = Vector2.Dot(Vector2.Normalize(gridDirection), chefForward) > 0.05;

            if (Primary) {
                if (input.ControllerState.PrimaryButtonDown) {
                    if (ExpectSpawn) {
                        foreach (var child in subjectEntity.spawned) {
                            // Console.WriteLine($"{child}: {child.existed[input.Frame]}, {child.spawnOwner[input.Frame]}");
                            if (child.existed[input.Frame] && child.spawnOwner[input.Frame] == -1) {
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

                // if ((Prepare || chefState.highlightedForPickup == subjectEntity) && (predictedHighlightingOnNextFrame.highlightedForPickup == subjectEntity) && predictedHighlightingOnNextNextFrame.highlightedForPickup == subjectEntity) {
                if (((chefState.highlightedForPickup == subjectEntity) || (predictedHighlightingOnNextFrame.highlightedForPickup == subjectEntity && Prepare)) && directionCheck) {
                    if (Prepare) {
                        return new GameActionOutput {
                            ControllerInput = gotoInput,
                            Done = true
                        };
                    }
                    // TODO: We can't store state for streak here, so we'd have to predict the highlight in the next frame
                    // with some humanizer information. For now just be inaccurate.
                    if (IsPickup ? input.ControllerState.RequestButtonDownForPickup() : input.ControllerState.RequestButtonDown()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                primaryDown = true,
                                primaryDownIsForPickup = IsPickup,
                                axes = gotoInput.axes,
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

                if (directionCheck && chefState.highlightedForUse == subjectEntity) {
                    if (input.ControllerState.RequestButtonDown()) {
                        return new GameActionOutput {
                            ControllerInput = new DesiredControllerInput {
                                secondaryDown = true,
                                axes = gotoInput.axes,
                            }
                        };
                    }
                    return default;
                }

            }
            return new GameActionOutput { ControllerInput = gotoInput };
        }

        public Save.InteractAction ToProto() {
            return new Save.InteractAction {
                Subject = Subject.ToProto(),
                Primary = Primary,
                Prepare = Prepare,
                IsPickup = IsPickup,
                ExpectSpawn = ExpectSpawn,
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
            };
        }
    }
}