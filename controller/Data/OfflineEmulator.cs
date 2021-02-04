using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class OfflineEmulator {
        public GameActionGraph Graph;
        int frame = 0;
        private GameSetup setup;
        public int Frame { get { return frame; } }

        public List<(int actionId, int actionFrame)> inProgress = new List<(int actionId, int actionFrame)>();
        Dictionary<int, int> depsRemaining = new Dictionary<int, int>();
        public HashSet<int> CompletedActions = new HashSet<int>();

        public OfflineEmulator(GameSetup level) {
            this.setup = level;
        }

        private void ResetToFrame(int frame) {
            Graph = setup.sequences.ToGraph();
            FakeEntityRegistry.entityToTypes.Clear();
            this.frame = frame;
            depsRemaining.Clear();
            inProgress.Clear();

            setup.entityRecords.CleanRecordsFromFrame(frame + 1);
            setup.sequences.CleanTimingsFromFrame(frame);
            setup.inputHistory.CleanHistoryFromFrame(frame);
            setup.LastSimulatedFrame = Math.Max(0, frame - 1);  // hand wavy...
            setup.LastEmpiricalFrame = Math.Min(setup.LastEmpiricalFrame, setup.LastSimulatedFrame);

            foreach (var (i, action) in Graph.actions) {
                depsRemaining[i] = Graph.deps[i].Count;
            }
            foreach (var (i, action) in Graph.actions) {
                var timing = setup.sequences.NodeById[i];
                if (timing.Predictions.EndFrame < frame) {
                    CompletedActions.Add(i);
                    foreach (var fwd in Graph.fwds[i]) {
                        depsRemaining[fwd]--;
                    }
                }
            }

            foreach (var (i, action) in Graph.actions) {
                if (depsRemaining[i] == 0 && !CompletedActions.Contains(i)) {
                    inProgress.Add((i, 0));
                    var predictions = setup.sequences.NodeById[i].Predictions;
                    if (predictions.StartFrame == null || predictions.StartFrame >= frame) {
                        predictions.StartFrame = frame;
                    }
                }
            }
            Console.WriteLine($"In progress: {string.Join(',', inProgress.Select(i => i.actionId))}");
        }

        public void SimulateEntireSchedule(int startFrame) {
            Console.WriteLine($"Offline simulating from frame {startFrame}");
            ResetToFrame(startFrame);

            int lastActionFrame = frame;
            while (frame < lastActionFrame + 600) {
                bool madeProgress;
                var inputs = Step(out madeProgress);
                if (madeProgress) {
                    lastActionFrame = frame;
                }
                foreach (var (chef, input) in inputs) {
                    setup.inputHistory.FrameInputs[chef].ChangeTo(input, Frame);
                }
                ApplyInputData(inputs);
            }

            setup.sequences.FillSaneFutureTimings(frame);
        }

        public Dictionary<GameEntityRecord, ActualControllerInput> Step(out bool madeProgress) {
            madeProgress = false;
            var names = new List<string>();
            var newInProgress = new List<(int actionId, int actionFrame)>();
            var newControllers = new Dictionary<GameEntityRecord, DesiredControllerInput>();
            foreach (var (actionId, actionFrame) in inProgress) {
                var action = Graph.actions[actionId];

                var gameActionInput = new GameActionInput {
                    ControllerState = setup.entityRecords.Chefs[action.Chef].Last(),
                    Entities = setup.entityRecords,
                    Frame = frame,
                    FrameWithinAction = actionFrame,
                    Geometry = setup.geometry,
                    MapByChef = setup.mapByChef
                };

                var output = action.Step(gameActionInput);
                if (output.SpawningClaim != null) {
                    // Console.WriteLine($"Action {actionId} claimed {output.SpawningClaim}");
                    output.SpawningClaim.spawnOwner.ChangeTo(action.ActionId, frame);
                }
                newControllers[action.Chef] = output.ControllerInput;
                if (output.Done) {
                    setup.sequences.NodeById[actionId].Predictions.EndFrame = frame;
                    foreach (var fwd in Graph.fwds[actionId]) {
                        if (--depsRemaining[fwd] == 0) {
                            madeProgress = true;
                            newInProgress.Add((fwd, 0));
                            setup.sequences.NodeById[fwd].Predictions.StartFrame = frame + 1;
                        }
                    }
                } else {
                    newInProgress.Add((actionId, actionFrame + 1));
                }
            }
            inProgress = newInProgress;

            var inputData = new Dictionary<GameEntityRecord, ActualControllerInput>();

            foreach (var entry in setup.entityRecords.Chefs) {
                var desiredInput = newControllers.GetValueOrDefault(entry.Key);
                var (newState, actualInput) = entry.Value.Last().ApplyInputAndAdvanceFrame(desiredInput);
                entry.Value.ChangeTo(newState, frame);
                inputData[entry.Key] = actualInput;
            }
            return inputData;
        }

        public void ApplyInputData(Dictionary<GameEntityRecord, ActualControllerInput> input) {
            setup.LastSimulatedFrame = frame;
            frame++;
            var allEntities = setup.entityRecords.GenAllEntities().Where(e => e.existed.Last()).ToList();
            foreach (var (chef, inputData) in input) {
                UpdateNearbyObjects(chef, allEntities);
                UpdateCarry(chef, inputData, allEntities);
                UpdateInteract(chef, allEntities, inputData);
                UpdateThrow(chef, inputData);
                UpdateAim(chef, inputData);
                UpdateMovement(chef, inputData);
            }
            UpdateAttachmentPositions(allEntities);
            UpdateCatches(allEntities);
            UpdateProgresses(allEntities);
            UpdateItemPhysics(allEntities);
        }

        public static (Vector2 pos, Vector2 velocity, Vector2 fwd) PredictChefPositionAfterInput(ChefState chefState, Vector2 position, Vector2 velocity, GameMap map, ActualControllerInput input) {
            var newPosition = CalculateNewChefPositionAfterMovement(position, velocity, map);
            var (newVelocity, forward) = CalculateNewChefVelocityAndForward(chefState, input);
            return (newPosition, newVelocity, forward);
        }

        public static ChefState CalculateHighlightedObjects(Vector2 position, Vector2 forward, GameMapGeometry geometry, IEnumerable<GameEntityRecord> entities) {
            // Find objects:
            //   - Grid objects:
            //       - Get discrete grid pos of chef, then look at four neighbors, score by
            //         how much the chef is facing the object - objects more than 135 degrees
            //         away are considered the same.
            //
            //   - Non-grid objects:
            //       - Scan around as a sphere, then check intersection with a forward arc.
            //
            // Finally, filter for objects that are interactable / placable, and then pick
            // the closest one by position.

            // TODO. We'll only do grid selection for now. Much easier.
            var coord = geometry.CoordsToGridPosRounded(position);
            GameEntityRecord best = null;
            double bestDot = -1;
            foreach (var entity in entities) {
                if (entity.chefState != null) {
                    continue;
                }
                if (entity.prefab.Name == "Dirty Plate") {
                    // This is a bug - in real simulation, somehow I couldn't get dirty plates to destroy themselves.
                    continue;
                }
                if (entity.path.ids.Length > 1) {
                    // Spawned items generally don't occupy a grid position.
                    continue;
                }
                foreach (var occupiedGridOffset in entity.prefab.OccupiedGridPoints) {
                    var entityCoord = geometry.CoordsToGridPosRounded(entity.position.Last().XZ() + occupiedGridOffset);
                    if (Math.Abs(entityCoord.x - coord.x) + Math.Abs(entityCoord.y - coord.y) == 1) {
                        if (entity.data.Last().attachmentParent == null) {
                            var gridOffsetDirection = new Vector2(1.0f * (entityCoord.x - coord.x), -1.0f * (entityCoord.y - coord.y));
                            var dot = Vector2.Dot(gridOffsetDirection, forward);
                            if (best == null || dot > bestDot) {
                                best = entity;
                                bestDot = dot;
                            }
                        }
                    }
                }
            }

            var chefState = new ChefState();
            chefState.highlightedForPickup = best;
            chefState.highlightedForPlacement = best;
            chefState.highlightedForUse = null;
            if (best != null) {
                if (best.prefab.IsBoard) {
                    if (best.data.Last().attachment is GameEntityRecord attachment && attachment.prefab.IsChoppable && attachment.prefab.MaxProgress > 0) {
                        chefState.highlightedForUse = best;
                    }
                } else if (best.prefab.IsWashingStation) {
                    if (best.data.Last().numPlates > 0) {
                        chefState.highlightedForUse = best;
                    }
                }
            }
            return chefState;
        }

        private void UpdateNearbyObjects(GameEntityRecord chef, List<GameEntityRecord> entities) {
            var newChefState = CalculateHighlightedObjects(chef.position.Last().XZ(), chef.chefState.Last().forward, setup.geometry, entities);
            chef.chefState.AppendWith(frame, state => {
                state.highlightedForPickup = newChefState.highlightedForPickup;
                state.highlightedForPlacement = newChefState.highlightedForPlacement;
                state.highlightedForUse = newChefState.highlightedForUse;
                return state;
            });
        }


        private static void Swap<T>(ref T lhs, ref T rhs) {
            T temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        private void Detach(GameEntityRecord child, int frame) {
            var parent = child.data.Last().attachmentParent;
            if (parent != null) {
                child.data.AppendWith(frame, d => {
                    d.attachmentParent = null;
                    return d;
                });
                parent.data.AppendWith(frame, d => {
                    d.attachment = null;
                    return d;
                });
            }
        }

        private void Attach(GameEntityRecord child, GameEntityRecord parent, int frame) {
            Detach(child, frame);
            child.data.AppendWith(frame, d => {
                d.attachmentParent = parent;
                return d;
            });
            parent.data.AppendWith(frame, d => {
                d.attachment = child;
                return d;
            });
        }

        private void UpdateCarry(GameEntityRecord chef, ActualControllerInput input, List<GameEntityRecord> allEntities) {
            if (input.primary.justPressed) {
                #region Holding an item, placing another item
                {
                    if (chef.data.Last().attachment is GameEntityRecord from
                        && chef.chefState.Last().highlightedForPickup is GameEntityRecord placer) {
                        if (placer.prefab.Name == "Serve" && from.prefab.Name == "Plate") {
                            placer.data.AppendWith(frame, d => {
                                d.plateRespawnTimers = d.plateRespawnTimers == null ? new List<TimeSpan>() :
                                     new List<TimeSpan>(d.plateRespawnTimers);
                                d.plateRespawnTimers.Add(TimeSpan.FromSeconds(7));
                                return d;
                            });
                            from.existed.ChangeTo(false, frame);
                            Detach(from, frame);
                            return;
                        }
                        if ((placer.prefab.Name == "Sink" || placer.prefab.Name == "Clean Plate Spawner")
                           && from.prefab.Name == "Dirty Plate Stack") {
                            placer = allEntities.Where(s => s.prefab.Name == "Sink").First();
                            placer.data.AppendWith(frame, d => {
                                d.numPlates += from.spawned.Count(s => s.existed.Last());
                                return d;
                            });
                            foreach (var spawn in from.spawned) {
                                if (spawn.existed.Last()) {
                                    spawn.existed.ChangeTo(false, frame);
                                }
                            }
                            from.existed.ChangeTo(false, frame);
                            Detach(from, frame);
                            return;
                        }
                        var to = placer.data.Last().attachment;
                        if (to == null && placer.prefab.IsAttachStation) {
                            Attach(from, placer, frame);
                        } else if (from.prefab.IsIngredient && to.prefab.CanContainIngredients) {
                            // Adding ingredient
                            to.data.AppendWith(frame, (data) => {
                                data.contents = data.contents == null ? new List<int>() : new List<int>(data.contents);
                                data.contents.Add(from.prefab.IngredientId);
                                return data;
                            });
                            if (to.prefab.MaxProgress != 0) {
                                to.progress.AppendWith(frame, (progress) => Math.Min(to.prefab.MaxProgress, progress) / 2);
                            }
                            Detach(from, frame);
                            from.existed.ChangeTo(false, frame);
                        } else if (from.prefab.CanContainIngredients && to.prefab.IsIngredient) {
                            // Putting container onto ingredient.
                            from.data.AppendWith(frame, (data) => {
                                data.contents = data.contents == null ? new List<int>() : new List<int>(data.contents);
                                data.contents.Add(to.prefab.IngredientId);
                                return data;
                            });
                            if (from.prefab.MaxProgress != 0) {
                                from.progress.AppendWith(frame, (progress) => Math.Min(from.prefab.MaxProgress, progress) / 2);
                            }
                            Detach(to, frame);
                            Attach(from, placer, frame);
                            to.existed.ChangeTo(false, frame);
                        } else if (from.prefab.CanContainIngredients && to.prefab.CanContainIngredients) {
                            // Ingredient transfer.
                            var fromData = from.data.Last();
                            fromData.contents = fromData.contents == null ? new List<int>() : new List<int>(fromData.contents);
                            var toData = to.data.Last();
                            toData.contents = toData.contents == null ? new List<int>() : new List<int>(toData.contents);
                            var fromProgress = from.progress.Last();
                            var toProgress = to.progress.Last();
                            if ((fromData.contents.Count == 0 || toData.contents.Count == 0) && to.prefab.CookingStage == from.prefab.CookingStage) {
                                Swap(ref fromData.contents, ref toData.contents);
                                Swap(ref fromProgress, ref toProgress);
                            } else if (toData.contents.Count == 0 && to.prefab.CookingStage > from.prefab.CookingStage) {
                                Swap(ref fromData.contents, ref toData.contents);
                                fromProgress = 0;
                                toProgress = 0;
                            } else if (fromData.contents.Count == 0 && from.prefab.CookingStage > to.prefab.CookingStage) {
                                Swap(ref fromData.contents, ref toData.contents);
                                fromProgress = 0;
                                toProgress = 0;
                            } else if (to.prefab.CookingStage == from.prefab.CookingStage) {
                                toData.contents.AddRange(fromData.contents);
                                fromData.contents.Clear();
                                toProgress = (Math.Min(toProgress, to.prefab.MaxProgress) + Math.Min(from.progress.Last(), to.prefab.MaxProgress)) / 2;
                                fromProgress = 0;
                            }
                            from.data.ChangeTo(fromData, frame);
                            to.data.ChangeTo(toData, frame);
                            from.progress.ChangeTo(fromProgress, frame);
                            to.progress.ChangeTo(toProgress, frame);
                        } else if (from.prefab.Name == "Dirty Plate Stack" && to.prefab.Name == "Dirty Plate Stack") {
                            // TODO.
                        }

                        return;
                    }
                }
                #endregion

                #region Not holding an item, placing an item
                {
                    if (chef.chefState.Last().highlightedForPickup is GameEntityRecord placer) {
                        if (placer.prefab.Name == "Sink" || placer.prefab.Name == "Clean Plate Spawner") {
                            placer = allEntities.Where(s => s.prefab.Name == "Clean Plate Spawner").First();
                            if (placer.data.Last().attachment is GameEntityRecord plateStack) {
                                var plate = plateStack.spawned.Where(s => s.existed.Last() && s.data.Last().attachmentParent == plateStack)
                                    .LastOrDefault();
                                if (plate != null) {
                                    Attach(plate, chef, frame);
                                    if (!plateStack.spawned.Any(s => s.existed.Last() && s.data.Last().attachmentParent == plateStack)) {
                                        Detach(plateStack, frame);
                                        plateStack.existed.ChangeTo(false, frame);
                                    }
                                }
                            }
                            return;
                        }

                        if (placer.data.Last().attachment is GameEntityRecord attachment) {
                            Attach(attachment, chef, frame);
                        } else if (placer.prefab.IsCrate) {
                            GameEntityRecord child = null;
                            if (placer.nextSpawnId.Last() < placer.spawned.Count) {
                                child = placer.spawned[placer.nextSpawnId.Last()];
                            }
                            if (child == null) {
                                var spawnedPrefab = placer.prefab.Spawns[0];
                                child = new GameEntityRecord {
                                    path = new EntityPath {
                                        ids = placer.path.ids.Append(placer.spawned.Count).ToArray(),
                                    },
                                    prefab = spawnedPrefab,
                                    spawner = placer,
                                    existed = new Versioned<bool>(false),
                                    position = new Versioned<Vector3>(Vector3.Zero),
                                    displayName = spawnedPrefab.Name,
                                    className = spawnedPrefab.ClassName
                                };
                                placer.spawned.Add(child);
                            }
                            child.existed.ChangeTo(true, frame);
                            child.position.ChangeTo(placer.position.Last(), frame);
                            placer.nextSpawnId.ChangeTo(placer.nextSpawnId.Last() + 1, frame);
                            Attach(child, chef, frame);
                        } else if (placer.data.Last().attachmentParent is null) {
                            // TODO: Ideally here we would check whether something is pickup-able. Instead we just list a few cases.
                            if (placer.prefab.IsChoppable || placer.prefab.CanContainIngredients || placer.prefab.IsIngredient) {
                                Attach(placer, chef, frame);
                            }
                        }
                        return;
                    }
                }
                #endregion

                #region dropping an item
                {
                    if (chef.data.Last().attachment is GameEntityRecord from) {
                        Detach(from, frame);
                    }
                }
                #endregion
            }
        }

        private void UpdateInteract(GameEntityRecord chef, List<GameEntityRecord> entities, ActualControllerInput input) {
            var highlighted = chef.chefState.Last().highlightedForUse;
            GameEntityRecord prevInteracting = null;
            foreach (var entity in entities) {
                var data = entity.data.Last();
                if ((data.chopInteracters?.ContainsKey(chef) ?? false) || (data.washers?.Contains(chef) ?? false)) {
                    prevInteracting = entity;
                    break;
                }
            }

            if (prevInteracting != null && highlighted != prevInteracting) {
                prevInteracting.data.AppendWith(frame, (data) => {
                    if (prevInteracting.prefab.IsBoard) {
                        data.chopInteracters = data.chopInteracters == null ? new Dictionary<GameEntityRecord, TimeSpan>() : new Dictionary<GameEntityRecord, TimeSpan>(data.chopInteracters);
                        data.chopInteracters.Remove(chef);
                    } else if (prevInteracting.prefab.IsWashingStation) {
                        data.washers = data.washers == null ? new HashSet<GameEntityRecord>() : new HashSet<GameEntityRecord>(data.washers);
                        data.washers.Remove(chef);
                    }
                    return data;
                });
            }
            if (highlighted != null && prevInteracting == null && input.secondary.isDown) {
                highlighted.data.AppendWith(frame, (data) => {
                    if (highlighted.prefab.IsBoard && data.attachment != null) {
                        data.chopInteracters = data.chopInteracters == null ? new Dictionary<GameEntityRecord, TimeSpan>() : new Dictionary<GameEntityRecord, TimeSpan>(data.chopInteracters);
                        data.chopInteracters[chef] = TimeSpan.FromSeconds(0.2);
                        data.itemBeingChopped = data.attachment;
                    } else {
                        data.washers = data.washers == null ? new HashSet<GameEntityRecord>() : new HashSet<GameEntityRecord>(data.washers);
                        data.washers.Add(chef);
                    }
                    return data;
                });
            }
        }

        private void UpdateThrow(GameEntityRecord chef, ActualControllerInput input) {
            // Very very approximate implementation.
            if (chef.data.Last().attachment is GameEntityRecord item) {
                if (input.secondary.justReleased) {
                    var direction = chef.chefState.Last().forward;
                    var throwSpeed = 18 * (direction.ToXZVector3() + new Vector3(0, (float)Math.Tan(0.0174532924 * 12), 0));
                    item.velocity.ChangeTo(throwSpeed, frame);
                    item.data.AppendWith(frame, data => {
                        data.attachmentParent = null;
                        return data;
                    });
                    chef.data.AppendWith(frame, data => {
                        data.attachment = null;
                        return data;
                    });
                }
            }
        }

        private void UpdateAim(GameEntityRecord chef, ActualControllerInput input) {
            var isAiming = false;
            if (chef.data.Last().attachment != null) {
                if (input.secondary.isDown) {
                    isAiming = true;
                }
            }
            if (isAiming != chef.chefState.Last().isAiming) {
                chef.chefState.AppendWith(frame, s => {
                    s.isAiming = isAiming;
                    return s;
                });
            }
        }


        public static (Vector2 velocity, Vector2 forward) CalculateNewChefVelocityAndForward(ChefState chefState, ActualControllerInput input) {
            var axes = new Vector2(input.axes.X, -input.axes.Y);
            if (axes.Length() < 0.01) {
                axes = default;
            } else {
                axes = Vector2.Normalize(axes);
            }
            var newForward = CalculateNewChefRotation(chefState.forward, axes);
            // Skipping movement suppression, gravity force, wind force.
            var desiredVelocity = axes * 6;  // run speed
            if (chefState.isAiming) {
                desiredVelocity = default;
            }
            if (chefState.dashTimer > 0) {
                var dashVelocity = newForward * 18; // dash speed
                var ratio = MathUtils.SinusoidalSCurve((float)(chefState.dashTimer / 0.3));  // dash time
                desiredVelocity = (1 - ratio) * desiredVelocity + ratio * dashVelocity;
            }
            // skipping impact timer
            if (Vector2.Distance(desiredVelocity, default) > 18) { // max speed
                desiredVelocity = Vector2.Normalize(desiredVelocity) * 18;
            }
            // Ignoring ProgressVelocityWrtFriction.
            return (desiredVelocity, newForward);
        }

        private void UpdateMovement(GameEntityRecord chef, ActualControllerInput input) {
            var (velocity, forward) = CalculateNewChefVelocityAndForward(chef.chefState.Last(), input);
            chef.velocity.ChangeTo(velocity.ToXZVector3(), frame);
            chef.chefState.AppendWith(frame, chefState => {
                if (input.dash.justPressed && chefState.dashTimer <= -0.1) {  // dash time minus cooldown
                    chefState.dashTimer = 0.3;  // dash time
                }
                chefState.dashTimer -= (1.0 / Config.FRAMERATE);
                chefState.forward = forward;
                return chefState;
            });
        }

        public static Vector2 CalculateNewChefRotation(Vector2 forward, Vector2 input) {
            if (input != default) {
                var cross = forward.X * input.Y - input.X * forward.Y;
                var dot = Vector2.Dot(input, forward);
                if (Math.Abs(cross) < 1e-6) {
                    cross = 1;
                }
                var maxAngle = Math.Acos(Math.Clamp(dot, -1, 1));
                var angle = Math.Min(20 * (1.0 / Config.FRAMERATE), maxAngle);
                var actualAngle = (float)(cross > 0 ? angle : -angle);
                var newForward = Vector2.TransformNormal(forward, Matrix3x2.CreateRotation(actualAngle));
                return newForward;
            }
            return forward;
        }

        private void UpdateRotation(GameEntityRecord chef, Vector2 input) {
            if (input != default) {
                chef.chefState.AppendWith(frame, s => {
                    s.forward = CalculateNewChefRotation(s.forward, input);
                    return s;
                });
            }
        }

        private void UpdateAttachmentPositions(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.data.Last().attachmentParent is GameEntityRecord parent) {
                    var attachPoint = parent.position.Last();
                    if (parent.IsChef()) {
                        attachPoint += 0.2f * parent.chefState.Last().forward.ToXZVector3() + new Vector3(0, 0.54f, 0);  // 0.2 is approximate
                    }
                    entity.position.ChangeTo(attachPoint, frame);
                    entity.velocity.ChangeTo(parent.velocity.Last(), frame);
                }
            }
        }

        public static Vector2 CalculateNewChefPositionAfterMovement(Vector2 position, Vector2 velocity, GameMap map) {
            var desired = position + velocity * (1.0f / Config.FRAMERATE);
            if (!map.IsInsideMap(desired)) {
                desired = position + new Vector2(velocity.X, 0) * (1.0f / Config.FRAMERATE);
                if (!map.IsInsideMap(desired)) {
                    desired = position + new Vector2(0, velocity.Y) * (1.0f / Config.FRAMERATE);
                    if (!map.IsInsideMap(desired)) {
                        desired = position;
                    }
                }
            }
            return desired;
        }

        private void UpdateItemPhysics(List<GameEntityRecord> entities) {
            // Update velocity
            foreach (var entity in entities) {
                if (entity.data.Last().attachmentParent == null) {
                    if (entity.velocity.Last().Length() > 0) {
                        if (!entity.IsChef()) {
                            // Heuristic for whether object is on floor.
                            if (entity.position.Last().Y < 1 && entity.velocity.Last().Y >= -0.1 && entity.velocity.Last().Y <= 0.7) {
                                var newVelocity = entity.velocity.Last() * (float)Math.Pow(0.1298857935, 1.0 / Config.FRAMERATE);
                                newVelocity.Y = 0;
                                entity.velocity.ChangeTo(newVelocity, frame);
                            } else {
                                var newVelocity = entity.velocity.Last() * (float)Math.Pow(0.1747536969, 1.0 / Config.FRAMERATE);
                                newVelocity.Y -= 9.4176f / Config.FRAMERATE; // gravity
                                entity.velocity.ChangeTo(newVelocity, frame);
                            }
                            if (entity.velocity.Last().Length() < 0.007f) {
                                entity.velocity.ChangeTo(Vector3.Zero, frame);
                            }
                        }
                    }
                }
            }
            foreach (var entity in entities) {
                if (entity.data.Last().attachmentParent == null) {
                    if (entity.velocity.Last().Length() > 0) {
                        if (entity.IsChef()) {
                            entity.position.ChangeTo(
                                CalculateNewChefPositionAfterMovement(entity.position.Last().XZ(), entity.velocity.Last().XZ(), setup.mapByChef[entity.path.ids[0]]).ToXZVector3(),
                                frame);
                        } else {
                            var newPosition = entity.position.Last() + entity.velocity.Last() * (1.0f / Config.FRAMERATE);
                            if (newPosition.Y < 0.1 && entity.velocity.Last().Y < -1) {
                                // Heuristic for collision with floor.
                                var newVelocity = entity.velocity.Last();
                                newVelocity.Y = 0;
                                newVelocity.X *= 0.68f;
                                newVelocity.Z *= 0.68f;
                                entity.velocity.ChangeTo(newVelocity, frame);
                            }
                            entity.position.ChangeTo(newPosition, frame);
                        }
                    }
                }
            }
        }

        private void UpdateCatches(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.existed.Last() && entity.prefab.CanContainIngredients && (entity.data.Last().contents?.Count ?? 0) < entity.prefab.MaxIngredientCount) {
                    foreach (var candidate in entities) {
                        if (candidate.existed.Last() && candidate.prefab.IsIngredient && candidate.data.Last().attachmentParent == null) {
                            if (Vector2.Distance(candidate.position.Last().XZ(), entity.position.Last().XZ()) < 0.8) {  // approximate
                                entity.data.AppendWith(frame, d => {
                                    d.contents = d.contents == null ? new List<int>() : new List<int>(d.contents);
                                    d.contents.Add(candidate.prefab.IngredientId);
                                    return d;
                                });
                                candidate.existed.ChangeTo(false, frame);
                            }
                        }
                    }
                }
            }
        }

        private GameEntityRecord SpawnChild(GameEntityRecord item) {
            GameEntityRecord child = null;
            if (item.nextSpawnId.Last() < item.spawned.Count) {
                child = item.spawned[item.nextSpawnId.Last()];
            }
            if (child == null) {
                var spawnedPrefab = item.prefab.Spawns[0];
                child = new GameEntityRecord {
                    path = new EntityPath {
                        ids = item.path.ids.Append(item.spawned.Count).ToArray(),
                    },
                    prefab = spawnedPrefab,
                    spawner = item,
                    existed = new Versioned<bool>(false),
                    position = new Versioned<Vector3>(Vector3.Zero),
                    displayName = spawnedPrefab.Name,
                    className = spawnedPrefab.ClassName
                };
                item.spawned.Add(child);
            }
            child.existed.ChangeTo(true, frame);
            child.position.ChangeTo(item.position.Last(), frame);
            item.nextSpawnId.ChangeTo(item.nextSpawnId.Last() + 1, frame);
            return child;
        }

        private void UpdateProgresses(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.prefab.MaxProgress > 0 && entity.prefab.CanContainIngredients && (entity.data.Last().contents?.Count ?? 0) > 0) {
                    if (entity.data.Last().attachmentParent is GameEntityRecord parent && (parent.prefab.IsMixerStation || parent.prefab.IsHeatingStation)) {
                        entity.progress.AppendWith(frame, p => p + (1.0 / Config.FRAMERATE));
                    }
                } else if (entity.prefab.IsBoard && entity.data.Last().itemBeingChopped is GameEntityRecord chopped && chopped.existed.Last()) {
                    var chopInteracters = entity.data.Last().chopInteracters;
                    if (chopInteracters != null) {
                        // Console.Write($"[{frame} {entity.path}] Chop interacters: {string.Join(", ", chopInteracters.Select(s => (s.Key.path, s.Value)))}");
                        var newData = entity.data.Last();
                        newData.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>(chopInteracters);
                        foreach (var (chef, remain) in chopInteracters) {
                            var newRemain = remain - TimeSpan.FromSeconds(1) / Config.FRAMERATE;
                            if (newRemain < TimeSpan.Zero) {
                                newData.itemBeingChopped.progress.ChangeTo(newData.itemBeingChopped.progress.Last() + 0.2, frame);
                                newRemain += TimeSpan.FromSeconds(0.2);
                            }
                            newData.chopInteracters[chef] = newRemain;
                        }
                        entity.data.ChangeTo(newData, frame);
                    }
                    var item = entity.data.Last().itemBeingChopped;
                    if (item.progress.Last() >= item.prefab.MaxProgress) {
                        GameEntityRecord child = SpawnChild(item);
                        child.data.AppendWith(frame, d => {
                            d.attachmentParent = entity;
                            return d;
                        });

                        item.existed.ChangeTo(false, frame);
                        item.data.AppendWith(frame, d => {
                            d.attachmentParent = null;
                            return d;
                        });
                        entity.data.AppendWith(frame, (Func<SpecificEntityData, SpecificEntityData>)(d => {
                            d.attachment = child;
                            d.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>();
                            d.itemBeingChopped = null;
                            return d;
                        }));
                    }
                } else if (entity.data.Last().plateRespawnTimers is List<TimeSpan> timers) {
                    List<TimeSpan> newTimers = new List<TimeSpan>();
                    foreach (var timer in timers) {
                        var newTimer = timer - TimeSpan.FromSeconds(1) / Config.FRAMERATE;
                        if (newTimer < TimeSpan.Zero) {
                            foreach (var spawner in entities) {
                                if (spawner.prefab.Name == "Dirty Plate Spawner") {
                                    var maybeStack = spawner.spawned.Where(e => e.existed.Last() && e.data.Last().attachmentParent == spawner).FirstOrDefault();
                                    if (maybeStack == null) {
                                        maybeStack = SpawnChild(spawner);
                                        maybeStack.data.AppendWith(frame, d => {
                                            d.attachmentParent = spawner;
                                            return d;
                                        });
                                        spawner.data.AppendWith(frame, d => {
                                            d.attachment = maybeStack;
                                            return d;
                                        });
                                    }
                                    var plate = SpawnChild(maybeStack);
                                    plate.data.AppendWith(frame, d => {
                                        d.attachmentParent = maybeStack;
                                        return d;
                                    });
                                    break;
                                }
                            }
                        } else {
                            newTimers.Add(newTimer);
                        }
                    }
                    entity.data.AppendWith(frame, d => {
                        d.plateRespawnTimers = newTimers;
                        return d;
                    });
                } else if (entity.data.Last().washers is HashSet<GameEntityRecord> washers) {
                    var spawner = entities.Where(e => e.prefab.Name == "Clean Plate Spawner").First();
                    var speed = (TimeSpan.FromSeconds(1) / Config.FRAMERATE * washers.Count).TotalSeconds;
                    entity.progress.ChangeTo(entity.progress.Last() + speed, frame);
                    if (entity.progress.Last() >= entity.prefab.MaxProgress) {
                        entity.progress.ChangeTo(0, frame);
                        entity.data.AppendWith(frame, d => {
                            d.numPlates--;
                            if (d.numPlates == 0) {
                                d.washers = null;
                            }
                            return d;
                        });
                        var maybeStack = spawner.spawned.Where(e => e.existed.Last() && e.data.Last().attachmentParent == spawner).FirstOrDefault();
                        if (maybeStack == null) {
                            maybeStack = SpawnChild(spawner);
                            maybeStack.data.AppendWith(frame, d => {
                                d.attachmentParent = spawner;
                                return d;
                            });
                            spawner.data.AppendWith(frame, d => {
                                d.attachment = maybeStack;
                                return d;
                            });
                        }
                        var plate = SpawnChild(maybeStack);
                        plate.data.AppendWith(frame, d => {
                            d.attachmentParent = maybeStack;
                            return d;
                        });
                    }
                }
            }
        }
    }
}