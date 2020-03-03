using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class OfflineEmulator {
        public GameActionGraph Graph;
        int frame = 0;
        public GameEntityRecords Records { get; set; }
        public GameActionSequences Timings { get; set; }
        public GameMap Map { get; set; }
        public InputHistory InputHistory { get; set; }
        public int Frame { get { return frame; } }

        public List<(int actionId, int actionFrame)> inProgress = new List<(int actionId, int actionFrame)>();
        Dictionary<int, int> depsRemaining = new Dictionary<int, int>();
        public HashSet<int> CompletedActions = new HashSet<int>();

        public OfflineEmulator(GameSetup level) {
            Map = level.map;
            Records = level.entityRecords;
            Timings = level.sequences;
            InputHistory = level.inputHistory;
        }

        private void ResetToFrame(int frame) {
            Graph = Timings.ToGraph();
            FakeEntityRegistry.entityToTypes.Clear();
            this.frame = frame;
            depsRemaining.Clear();
            inProgress.Clear();

            Records.CleanRecordsFromFrame(frame);
            Timings.CleanTimingsFromFrame(frame);
            InputHistory.CleanHistoryFromFrame(frame);

            foreach (var (i, action) in Graph.actions) {
                depsRemaining[i] = Graph.deps[i].Count;
            }
            foreach (var (i, action) in Graph.actions) {
                var timing = Timings.NodeById[i];
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
                    var predictions = Timings.NodeById[i].Predictions;
                    if (predictions.StartFrame >= frame) {
                        predictions.StartFrame = frame;
                    }
                }
            }
            Console.WriteLine($"In progress: {string.Join(',', inProgress.Select(i => i.actionId))}");
        }

        public void SimulateEntireSchedule(int startFrame) {
            ResetToFrame(startFrame);

            int lastActionFrame = frame;
            while (frame < lastActionFrame + 600) {
                bool madeProgress;
                var inputs = Step(out madeProgress);
                if (madeProgress) {
                    lastActionFrame = frame;
                }
                foreach (var (chef, input) in inputs) {
                    InputHistory.FrameInputs[chef].ChangeTo(input, Frame);
                }
                ApplyInputData(inputs);
            }

            Timings.FillSaneFutureTimings(frame);
        }

        public Dictionary<GameEntityRecord, ActualControllerInput> Step(out bool madeProgress) {
            madeProgress = false;
            var names = new List<string>();
            var newInProgress = new List<(int actionId, int actionFrame)>();
            var newControllers = new Dictionary<GameEntityRecord, DesiredControllerInput>();
            foreach (var (actionId, actionFrame) in inProgress) {
                var action = Graph.actions[actionId];

                var gameActionInput = new GameActionInput {
                    ControllerState = Records.Chefs[action.Chef].Last(),
                    Entities = Records,
                    Frame = frame,
                    FrameWithinAction = actionFrame,
                    Map = Map
                };

                var output = action.Step(gameActionInput);
                if (output.SpawningClaim != null) {
                    Console.WriteLine($"Action {actionId} claimed {output.SpawningClaim}");
                    output.SpawningClaim.spawnOwner.ChangeTo(action as ISpawnClaimingAction, frame);
                }
                newControllers[action.Chef] = output.ControllerInput;
                if (output.Done) {
                    Timings.NodeById[actionId].Predictions.EndFrame = frame;
                    foreach (var fwd in Graph.fwds[actionId]) {
                        if (--depsRemaining[fwd] == 0) {
                            madeProgress = true;
                            newInProgress.Add((fwd, 0));
                            Timings.NodeById[fwd].Predictions.StartFrame = frame;
                        }
                    }
                } else {
                    newInProgress.Add((actionId, actionFrame + 1));
                }
            }
            inProgress = newInProgress;

            var inputData = new Dictionary<GameEntityRecord, ActualControllerInput>();

            foreach (var entry in Records.Chefs) {
                var desiredInput = newControllers.GetValueOrDefault(entry.Key);
                var (newState, actualInput) = entry.Value.Last().ApplyInputAndAdvanceFrame(desiredInput);
                entry.Value.ChangeTo(newState, frame);
                inputData[entry.Key] = actualInput;
            }
            return inputData;
        }

        public void ApplyInputData(Dictionary<GameEntityRecord, ActualControllerInput> input) {
            frame++;
            var allEntities = Records.GenAllEntities().Where(e => e.existed.Last()).ToList();
            foreach (var (chef, inputData) in input) {
                UpdateNearbyObjects(chef, allEntities);
                UpdateCarry(chef, inputData);
                UpdateInteract(chef, allEntities, inputData);
                UpdateThrow(chef, inputData);
                UpdateAim(chef, inputData);
                UpdateMovement(chef, inputData);
            }
            UpdateAttachmentPositions(allEntities);
            UpdateItemPositionsAccordingToVelocity(allEntities);
            UpdateCatches(allEntities);
            UpdateProgresses(allEntities);
        }

        private void UpdateNearbyObjects(GameEntityRecord chef, List<GameEntityRecord> entities) {
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
            var chefForward = chef.chefState.Last().forward;
            var coord = Map.CoordsToGridPosRounded(chef.position.Last().XZ());
            GameEntityRecord best = null;
            double bestDot = -1;
            foreach (var entity in entities) {
                var entityCoord = Map.CoordsToGridPosRounded(entity.position.Last().XZ());
                if (Math.Abs(entityCoord.x - coord.x) + Math.Abs(entityCoord.y - coord.y) == 1) {
                    if (entity.data.Last().attachmentParent == null) {
                        var gridOffsetDirection = new Vector2(1.0f * (entityCoord.x - coord.x), -1.0f * (entityCoord.y - coord.y));
                        var dot = Vector2.Dot(gridOffsetDirection, chefForward);
                        if (best == null || dot > bestDot) {
                            best = entity;
                            bestDot = dot;
                        }
                    }
                }
            }

            var newChefState = chef.chefState.Last();
            newChefState.highlightedForPickup = best;
            newChefState.highlightedForPlacement = best;
            newChefState.highlightedForUse = null;
            if (best.prefab.IsBoard) {
                if (best.data.Last().attachment is GameEntityRecord attachment && attachment.prefab.IsChoppable && attachment.prefab.MaxProgress > 0) {
                    newChefState.highlightedForUse = best;
                }
            }
            // TODO: washing case.
            chef.chefState.ChangeTo(newChefState, frame);
        }


        private static void Swap<T>(ref T lhs, ref T rhs) {
            T temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        private void UpdateCarry(GameEntityRecord chef, ActualControllerInput input) {
            if (input.primary.justPressed) {
                {
                    if (chef.data.Last().attachment is GameEntityRecord from
                        && chef.chefState.Last().highlightedForPickup is GameEntityRecord placer) {
                        // if (placer.data.Last().attachment is GameEntityRecord innerPlacer && innerPlacer.prefab.IsMixerStation) {
                        //     // Special case for mixer station.
                        //     placer = innerPlacer;
                        // }
                        // TODO: the case of picking from the floor is not handled yet.
                        var to = placer.data.Last().attachment;
                        if (to == null) {
                            placer.data.AppendWith(frame, (data) => { data.attachment = from; return data; });
                            from.data.AppendWith(frame, (data) => { data.attachmentParent = placer; return data; });
                            chef.data.AppendWith(frame, (data) => { data.attachment = null; return data; });
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
                            chef.data.AppendWith(frame, (data) => { data.attachment = null; return data; });
                            from.data.AppendWith(frame, (data) => { data.attachmentParent = null; return data; });
                            from.existed.ChangeTo(false, frame);
                        } else if (from.prefab.CanContainIngredients && to.prefab.IsIngredient) {
                            // Putting container onto ingredient.
                            from.data.AppendWith(frame, (data) => {
                                data.contents = data.contents == null ? new List<int>() : new List<int>(data.contents);
                                data.contents.Add(to.prefab.IngredientId);
                                data.attachmentParent = placer;
                                return data;
                            });
                            if (from.prefab.MaxProgress != 0) {
                                from.progress.AppendWith(frame, (progress) => Math.Min(from.prefab.MaxProgress, progress) / 2);
                            }
                            placer.data.AppendWith(frame, (data) => { data.attachment = from; return data; });
                            chef.data.AppendWith(frame, (data) => { data.attachment = null; return data; });
                            to.data.AppendWith(frame, (data) => { data.attachmentParent = null; return data; });
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
                        }

                        return;
                    }
                }

                {
                    if (chef.chefState.Last().highlightedForPickup is GameEntityRecord placer) {
                        // if (placer.data.Last().attachment is GameEntityRecord innerPlacer && innerPlacer.prefab.IsMixerStation) {
                        //     // Special case for mixer station.
                        //     placer = innerPlacer;
                        // }

                        // TODO: the case of picking from the floor is not handled yet.
                        if (placer.data.Last().attachment is GameEntityRecord attachment) {
                            placer.data.AppendWith(frame, d => {
                                d.attachment = null;
                                return d;
                            });

                            attachment.data.AppendWith(frame, d => {
                                d.attachmentParent = chef;
                                return d;
                            });

                            chef.data.AppendWith(frame, d => {
                                d.attachment = attachment;
                                return d;
                            });
                        } else {
                            if (placer.prefab.IsCrate) {
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
                                chef.data.AppendWith(frame, d => {
                                    d.attachment = child;
                                    return d;
                                });
                                child.data.AppendWith(frame, d => {
                                    d.attachmentParent = chef;
                                    return d;
                                });
                            }
                        }
                    }
                }
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

        private void UpdateMovement(GameEntityRecord chef, ActualControllerInput input) {
            var axes = new Vector2(input.axes.X, -input.axes.Y);
            if (axes.Length() < 0.01) {
                axes = default;
            } else {
                axes = Vector2.Normalize(axes);
            }
            UpdateRotation(chef, axes);
            var chefState = chef.chefState.Last();
            // Skipping movement suppression, gravity force, wind force.
            var desiredVelocity = axes * 6;  // run speed
            if (chefState.isAiming) {
                desiredVelocity = default;
            }
            if (chefState.dashTimer > 0) {
                var dashVelocity = chefState.forward * 18; // dash speed
                var ratio = MathUtils.SinusoidalSCurve((float)(chefState.dashTimer / 0.3));  // dash time
                desiredVelocity = (1 - ratio) * desiredVelocity + ratio * dashVelocity;
            }
            chefState.dashTimer -= (1.0 / 60);
            // skipping impact timer
            if (Vector2.Distance(desiredVelocity, default) > 18) { // max speed
                desiredVelocity = Vector2.Normalize(desiredVelocity) * 18;
            }
            // Ignoring ProgressVelocityWrtFriction.
            chef.velocity.ChangeTo(desiredVelocity.ToXZVector3(), frame);
            if (input.dash.justPressed && chefState.dashTimer < 0.1) {  // dash time minus cooldown
                chefState.dashTimer = 0.3;  // dash time
            }
            chef.chefState.ChangeTo(chefState, frame);
        }

        private void UpdateRotation(GameEntityRecord chef, Vector2 input) {
            if (input != default) {
                var forward = chef.chefState.Last().forward;
                var cross = forward.X * input.Y - input.X * forward.Y;
                var dot = Vector2.Dot(input, forward);
                if (Math.Abs(cross) < 1e-6) {
                    cross = 1;
                }
                var maxAngle = Math.Acos(Math.Clamp(dot, -1, 1));
                var angle = Math.Min(20 * (1.0 / 60), maxAngle);
                var actualAngle = (float)(cross > 0 ? angle : -angle);
                var newForward = Vector2.TransformNormal(forward, Matrix3x2.CreateRotation(actualAngle));
                chef.chefState.AppendWith(frame, s => {
                    s.forward = newForward;
                    return s;
                });
            }
        }

        private void UpdateAttachmentPositions(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.data.Last().attachmentParent is GameEntityRecord parent) {
                    var attachPoint = parent.position.Last();
                    if (parent.IsChef()) {
                        attachPoint += 0.2f * parent.chefState.Last().forward.ToXZVector3();  // 0.2 is approximate
                    }
                    entity.position.ChangeTo(attachPoint, frame);
                }
            }
        }


        private void UpdateItemPositionsAccordingToVelocity(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.data.Last().attachmentParent == null) {
                    if (entity.velocity.Last().Length() > 0) {
                        if (entity.IsChef()) {
                            var desired = entity.position.Last() + entity.velocity.Last() * (1.0f / 60);
                            if (!Map.IsInsideMap(desired.XZ())) {
                                desired = entity.position.Last() + new Vector3(entity.velocity.Last().X, 0, 0) * (1.0f / 60);
                                if (!Map.IsInsideMap(desired.XZ())) {
                                    desired = entity.position.Last() + new Vector3(0, 0, entity.velocity.Last().Z) * (1.0f / 60);
                                    if (!Map.IsInsideMap(desired.XZ())) {
                                        desired = entity.position.Last();
                                    }
                                }
                            }
                            entity.position.ChangeTo(desired, frame);
                        } else {
                            entity.position.ChangeTo(entity.position.Last() + entity.velocity.Last() * (1.0f / 60), frame);
                        }
                    }
                }
            }
        }

        private void UpdateCatches(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                foreach (var candidate in entities) {
                    if (entity.prefab.CanContainIngredients && (entity.data.Last().contents?.Count ?? 0) < entity.prefab.MaxIngredientCount) {
                        if (candidate.prefab.IsIngredient && candidate.data.Last().attachmentParent == null) {
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


        private void UpdateProgresses(List<GameEntityRecord> entities) {
            foreach (var entity in entities) {
                if (entity.prefab.MaxProgress > 0 && entity.prefab.CanContainIngredients && (entity.data.Last().contents?.Count ?? 0) > 0) {
                    if (entity.data.Last().attachmentParent is GameEntityRecord parent && (parent.prefab.IsMixerStation || parent.prefab.IsHeatingStation)) {
                        entity.progress.AppendWith(frame, p => p + (1.0 / 60));
                    }
                } else if (entity.prefab.IsBoard && entity.data.Last().itemBeingChopped != null) {
                    var chopInteracters = entity.data.Last().chopInteracters;
                    if (chopInteracters != null) {
                        // Console.Write($"[{frame} {entity.path}] Chop interacters: {string.Join(", ", chopInteracters.Select(s => (s.Key.path, s.Value)))}");
                        var newData = entity.data.Last();
                        newData.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>(chopInteracters);
                        foreach (var (chef, remain) in chopInteracters) {
                            var newRemain = remain - TimeSpan.FromSeconds(1) / 60;
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
                        child.data.AppendWith(frame, d => {
                            d.attachmentParent = entity;
                            return d;
                        });
                        item.nextSpawnId.ChangeTo(item.nextSpawnId.Last() + 1, frame);

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
                }
            }
        }
    }
}