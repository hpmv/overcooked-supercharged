using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class RealGameSimulator {
        public GameSetup setup;
        public GameActionGraph Graph = new GameActionGraph();
        int frame = 0;
        Dictionary<int, int> depsRemaining = new Dictionary<int, int>();
        public int Frame { get { return frame; } }

        public int LastMadeProgressFrame { get; private set; }
        public readonly MessageStats Stats = new MessageStats();

        public List<int> inProgress = new List<int>();

        public Dictionary<int, GameEntityRecord> entityIdToRecord = new Dictionary<int, GameEntityRecord>();

        public void Reset() {
            FakeEntityRegistry.entityToTypes.Clear();
            frame = 0;
            depsRemaining.Clear();
            inProgress.Clear();
            entityIdToRecord.Clear();
            Stats.Clear();

            setup.entityRecords.CleanRecordsAfterFrame(0);
            foreach (var entry in setup.entityRecords.FixedEntities) {
                entityIdToRecord[entry.path.ids[0]] = entry;
            }

            foreach (var (i, action) in Graph.actions) {
                if (Graph.deps[i].Count == 0) {
                    inProgress.Add(i);
                    setup.sequences.NodeById[i].Predictions.StartFrame = frame;
                } else {
                    depsRemaining[i] = Graph.deps[i].Count;
                }
            }
        }

        public void ClearHistoryBeforeSimulation() {
            Graph = setup.sequences.ToGraph();
            depsRemaining.Clear();
            inProgress.Clear();

            setup.entityRecords.CleanRecordsAfterFrame(frame);
            setup.sequences.CleanTimingsAfterFrame(frame);
            setup.inputHistory.CleanHistoryAfterFrame(frame);
            setup.LastEmpiricalFrame = Math.Min(setup.LastEmpiricalFrame, frame);

            foreach (var (i, action) in Graph.actions) {
                depsRemaining[i] = Graph.deps[i].Count;
            }
            var completedActions = new HashSet<int>();
            foreach (var (i, action) in Graph.actions) {
                var timing = setup.sequences.NodeById[i];
                // Must be <, not <=, because when we are on frame N, the action processing for
                // frame N is not yet done.
                if (timing.Predictions.EndFrame < frame) {
                    completedActions.Add(i);
                    foreach (var fwd in Graph.fwds[i]) {
                        depsRemaining[fwd]--;
                    }
                }
            }

            foreach (var (i, action) in Graph.actions) {
                if (depsRemaining[i] == 0 && !completedActions.Contains(i)) {
                    inProgress.Add(i);
                    var predictions = setup.sequences.NodeById[i].Predictions;
                    if (predictions.StartFrame == null || predictions.StartFrame >= frame) {
                        // This is just for display. When we run these actions we'll give
                        // them actual frames.
                        predictions.StartFrame = frame;
                    }
                }
            }
            Console.WriteLine($"In progress: {string.Join(',', inProgress)}");
        }

        /// Advance frame in response to in-game logical frame advance.
        public void AdvanceFrame() {
            // Console.WriteLine($"Advancing frame {frame} -> {frame + 1}");
            frame++;
        }

        /// Set the frame in response to warping.
        public void SetFrameAfterWarping(int frame) {
            // Console.WriteLine($"Setting frame after warp {this.frame} -> {frame}");
            this.frame = frame;
        }

        public Dictionary<int, OneInputData> ComputeInputForNextFrame() {
            // Console.WriteLine($"Computing input for frame {frame}");
            var names = new List<string>();
            var inProgress = new Queue<int>(this.inProgress);
            var newInProgress = new List<int>();
            var newControllers = new Dictionary<GameEntityRecord, DesiredControllerInput>();
            while (inProgress.Count > 0) {
                var actionId = inProgress.Dequeue();
                var action = Graph.actions[actionId];

                var gameActionInput = new GameActionInput {
                    ControllerState = setup.entityRecords.Chefs[action.Chef][frame],
                    Entities = setup.entityRecords,
                    Frame = frame,  // not frame + 1; this is the basis of the computation
                    FrameWithinAction = frame - setup.sequences.NodeById[actionId].Predictions.StartFrame.Value,
                    Geometry = setup.geometry,
                    MapByChef = setup.mapByChef,
                };
                // Console.WriteLine($"Action {actionId} frame {frame} chef {action.Chef.displayName} controller state is {gameActionInput.ControllerState.ToProto().ToString()}");

                var output = action.Step(gameActionInput);
                if (output.SpawningClaim != null) {
                    // Console.WriteLine($"Action {actionId} claimed {output.SpawningClaim}");

                    // We can't actually delay this to the next frame even though this kinda goes against the
                    // idea of not mutating the current frame in the current frame. But that's probably OK.
                    output.SpawningClaim.spawnOwner.ChangeTo(action.ActionId, frame);
                }
                
                if (output.ControllerInput != null) {
                    newControllers[action.Chef] = output.ControllerInput.Value;
                }
                if (output.Done) {
                    // If the output is Done that means we're achieved the goal of this action already
                    // in the current frame, so mark it as done by the current frame.
                    setup.sequences.NodeById[actionId].Predictions.EndFrame = frame;
                    foreach (var fwd in Graph.fwds[actionId]) {
                        if (--depsRemaining[fwd] == 0) {
                            LastMadeProgressFrame = frame;
                            // If the newly enabled action is for a chef who does not already have an
                            // input computed for the next frame, then we can immediately start this
                            // action. Otherwise, we mark the action as started (since we otherwise
                            // can't reproduce the same marking when warping) but do not actually
                            // run the action until next frame.
                            //
                            // Note that we must be checking the inputs on the chef - not whether
                            // output.ControllerInput == null - because the enabled action may be on a
                            // different chef.
                            var nextAction = setup.sequences.NodeById[fwd];
                            nextAction.Predictions.StartFrame = frame;
                            if (newControllers.ContainsKey(nextAction.Action.Chef)) {
                                newInProgress.Add(fwd);
                            } else {
                                inProgress.Enqueue(fwd);
                            }
                        }
                    }
                } else {
                    newInProgress.Add(actionId);
                }
            }
            this.inProgress = newInProgress;

            var input = new Dictionary<int, OneInputData>();
            foreach (var entry in setup.entityRecords.Chefs) {
                var chefId = entry.Key.path.ids[0];
                var desiredInput = newControllers.GetValueOrDefault(entry.Key);
                var (newState, actualInput) = entry.Value[frame].ApplyInputAndAdvanceFrame(desiredInput);
                setup.inputHistory.FrameInputs[entry.Key].ChangeTo(actualInput, frame + 1);
                // Console.WriteLine($"Changing chef {entry.Key.displayName} frame {frame + 1} to {newState.ToProto().ToString()}; actual input is {actualInput.ToProto().ToString()}, desiredInput primary up is {desiredInput.primaryUp}");
                entry.Value.ChangeTo(newState, frame + 1);
                input[chefId] = new OneInputData {
                    Pad = new PadDirection { X = actualInput.axes.X, Y = actualInput.axes.Y },
                    Pickup = ToButtonInput(actualInput.primary),
                    Interact = ToButtonInput(actualInput.secondary),
                    Dash = ToButtonInput(actualInput.dash),
                };
            }
            return input;
        }

        private ButtonInput ToButtonInput(ButtonOutput output) {
            return new ButtonInput {
                JustPressed = output.justPressed,
                JustReleased = output.justReleased,
                Down = output.isDown
            };
        }

        private string toJson(Serialisable serialisable) {
            if (serialisable == null) return "(null)";
            string serialized;
            try {
                serialized = JsonConvert.SerializeObject(serialisable, new JsonSerializerSettings() {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                });
            } catch (Exception e) {
                serialized = e.Message;
            }
            return serialized;
        }


        public void ApplyGameUpdate(Serialisable item) {
            Stats.CountMessage(item?.GetType()?.Name ?? "message null");
            Action<int, Serialisable> entityHandler = (int entityId, Serialisable payload) => {
                Stats.CountMessage(payload?.GetType()?.Name ?? "entity null");
                if (!entityIdToRecord.ContainsKey(entityId)) {
                    Console.WriteLine("Ignoring entity event for " + entityId);
                    return;
                }
                var entityRecord = entityIdToRecord[entityId];
                var specificData = entityRecord.data.Last();
                if (payload is ChefCarryMessage ccm) {
                    specificData.attachment = ccm.m_carriableItem == 0 ? null : entityIdToRecord[(int)ccm.m_carriableItem];
                } else if (payload is MixingStateMessage msm) {
                    entityRecord.progress.ChangeTo(msm.m_mixingProgress, frame);
                } else if (payload is CookingStateMessage csm) {
                    entityRecord.progress.ChangeTo(csm.m_cookingProgress, frame);
                } else if (payload is PhysicalAttachMessage pam) {
                    if (specificData.attachmentParent != null) {
                        specificData.attachmentParent.data.AppendWith(frame, d => {
                            d.attachment = null;
                            return d;
                        });
                    }
                    specificData.attachmentParent = pam.m_parent <= 0 ? null : entityIdToRecord[pam.m_parent];
                    if (specificData.attachmentParent != null) {
                        specificData.attachmentParent.data.AppendWith(frame, d => {
                            d.attachment = entityRecord;
                            return d;
                        });
                    }
                } else if (payload is WorkstationMessage wm) {
                    var interacter = (int)wm.m_interactorHeader.m_uEntityID;
                    var board = entityRecord;
                    var chef = entityIdToRecord[interacter];
                    if (specificData.chopInteracters == null) {
                        specificData.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>();
                    } else {
                        specificData.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>(specificData.chopInteracters);
                    }
                    if (wm.m_interacting) {
                        specificData.chopInteracters[chef] = TimeSpan.FromSeconds(0.2);
                        specificData.itemBeingChopped = entityIdToRecord[(int)wm.m_itemHeader.m_uEntityID];
                    } else {
                        specificData.chopInteracters.Remove(chef);
                    }
                } else if (payload is AttachStationMessage asm) {
                    specificData.attachment = asm.m_item <= 0 ? null : entityIdToRecord[asm.m_item];
                } else if (payload is IngredientContainerMessage icm) {
                    if (icm.Type == IngredientContainerMessage.MessageType.ContentsChanged) {
                        specificData.contents = icm.Contents?.Flatten()?.ToList();
                        specificData.rawGameEntityData = icm.ToBytes();
                    }
                } else if (payload is PlateStationMessage message) {
                    specificData.plateRespawnTimers = specificData.plateRespawnTimers == null ? new List<TimeSpan>() : new List<TimeSpan>(specificData.plateRespawnTimers);
                    specificData.plateRespawnTimers.Add(TimeSpan.FromSeconds(7));
                    entityIdToRecord[(int)message.m_delivered].existed.ChangeTo(false, frame);
                    entityIdToRecord[(int)message.m_delivered].data.AppendWith(frame, d => {
                        // The plate doesn't actually get destroyed at this point; rather its shaders and stuff get changed to play a fading animation
                        // and then gets destroyed after. This is too difficult to recover, so if it gets here, we mark the object as unwarpable, so
                        // that warping would require a new plate to be spawned.
                        d.isUnwarpable = true;
                        return d;
                    });
                } else if (payload is WashingStationMessage wsm) {
                    // Console.WriteLine($"[{frame}] " + toJson(payload));
                    if (wsm.m_msgType == WashingStationMessage.MessageType.InteractionState) {
                        specificData.washers = specificData.washers == null ? new HashSet<GameEntityRecord>() : new HashSet<GameEntityRecord>(specificData.washers);
                        entityRecord.progress.ChangeTo(wsm.m_progress, frame);
                    } else {
                        specificData.numPlates = wsm.m_plateCount;
                        if (wsm.m_msgType == WashingStationMessage.MessageType.CleanedPlate) {
                            entityRecord.progress.ChangeTo(0, frame);
                        }
                    }
                } else if (payload is InputEventMessage iem) {
                    if (iem.inputEventType == InputEventMessage.InputEventType.BeginInteraction) {
                        if (entityIdToRecord.ContainsKey((int)iem.entityId)) {
                            var entity = entityIdToRecord[(int)iem.entityId];
                            if (entity.className == "sink") // hacky
                            {
                                // This is different from the other cases above - here we alter the state of a different entity!
                                var washerData = entity.data.Last();
                                washerData.washers = washerData.washers == null ? new HashSet<GameEntityRecord>() : new HashSet<GameEntityRecord>(washerData.washers);
                                var interacter = entityIdToRecord[entityId];
                                washerData.washers.Add(interacter);
                                entity.data.ChangeTo(washerData, frame);
                            }
                        }
                    } else if (iem.inputEventType == InputEventMessage.InputEventType.EndInteraction) {
                        // When ending interaction, unfortunately we don't get the object being originally interacted with,
                        // so we just have to iterate through all entities.
                        var interacter = entityIdToRecord[entityId];
                        foreach (var entity in setup.entityRecords.GenAllEntities()) {
                            if (entity.existed.Last() && entity.data.Last().washers != null && entity.data.Last().washers.Contains(interacter)) {
                                entity.data.AppendWith(frame, d => {
                                    d.washers = new HashSet<GameEntityRecord>(d.washers);
                                    d.washers.Remove(interacter);
                                    return d;
                                });
                            }
                        }
                    }
                } else if (payload is ThrowableItemMessage tim) {
                    specificData.throwableItem.IsFlying = tim.m_inFlight;
                    specificData.throwableItem.FlightTimer = TimeSpan.FromSeconds(0);
                    specificData.throwableItem.thrower = tim.m_thrower <= 0 ? null : entityIdToRecord[tim.m_thrower];
                } else if (payload is ThrowableItemAuxMessage tiam) {
                    specificData.throwableItem.ignoredColliders =
                        tiam.m_colliders.Select(c => (Entity: entityIdToRecord[(int)c.entity.m_uEntityID], ColliderIndex: c.colliderIndex)).ToArray();
                } else if (payload is CannonModMessage cmm) {
                    specificData.rawGameEntityData = cmm.ToBytes();
                } else if (payload is SessionInteractableMessage sim) {
                    var interacter = sim.m_interacterID == 0 ? null : entityIdToRecord[(int)sim.m_interacterID];
                    specificData.sessionInteracter = interacter;
                } else if (payload is PilotRotationMessage prm) {
                    specificData.pilotRotationAngle = prm.m_angle;
                }
                entityRecord.data.ChangeTo(specificData, frame);
            };

            Action<SpawnEntityMessage> spawnHandler = (SpawnEntityMessage sem) => {
                var spawner = entityIdToRecord[(int)sem.m_SpawnerHeader.m_uEntityID];
                Console.WriteLine($"Spawning {sem.m_DesiredHeader.m_uEntityID}");
                GameEntityRecord child = null;
                if (spawner.nextSpawnId.Last() < spawner.spawned.Count) {
                    child = spawner.spawned[spawner.nextSpawnId.Last()];
                }
                if (child == null) {
                    var spawnedPrefab = spawner.prefab.Spawns[sem.m_SpawnableID];
                    child = new GameEntityRecord {
                        path = new EntityPath {
                            ids = spawner.path.ids.Append(spawner.spawned.Count).ToArray(),
                        },
                        prefab = spawnedPrefab,
                        spawner = spawner,
                        existed = new Versioned<bool>(false),
                        position = new Versioned<Vector3>(Vector3.Zero),
                        displayName = spawnedPrefab.Name,
                        className = spawnedPrefab.ClassName
                    };
                    spawner.spawned.Add(child);
                }
                child.existed.ChangeTo(true, frame);
                child.position.ChangeTo(sem.m_Position.ToNumerics(), frame);
                spawner.nextSpawnId.ChangeTo(spawner.nextSpawnId.Last() + 1, frame);
                entityIdToRecord[(int)sem.m_DesiredHeader.m_uEntityID] = child;
            };

            Action<GameEntityRecord, uint> destroyEntity = (r, entityId) => {
                entityIdToRecord.Remove((int)entityId);
                r.existed.ChangeTo(false, frame);
                Console.WriteLine($"Destroying {entityId} ({r.className} {r.displayName})");
                // TODO: is this loop needed?
                foreach (var entity in setup.entityRecords.GenAllEntities()) {
                    if (entity.existed.Last() && entity.data.Last().attachmentParent == r) {
                        Console.WriteLine($"Destroying child entity {entity.className} {entity.displayName}");
                        entity.existed.ChangeTo(false, frame);
                    }
                }
            };

            // Console.WriteLine("Got message " + (MessageType) msg.Type);
            if (item is EntitySynchronisationMessage sync) {
                foreach (var (type, payload) in sync.m_Payloads) {
                    entityHandler((int)sync.m_Header.m_uEntityID, payload);
                }
            } else if (item is EntityEventMessage eem) {
                entityHandler((int)eem.m_Header.m_uEntityID, eem.m_Payload);
            } else if (item is EntityAuxMessage eam) {
                entityHandler((int)eam.m_entityHeader.m_uEntityID, eam.m_payload);
            } else if (item is SpawnEntityMessage sem) {
                // Console.WriteLine(sem);
                spawnHandler(sem);
            } else if (item is SpawnPhysicalAttachmentMessage spem) {
                spawnHandler(spem.m_SpawnEntityData);
            } else if (item is DestroyEntityMessage dem) {
                // Console.WriteLine($"[{frame}] " + toJson(item));
                destroyEntity(entityIdToRecord[(int)dem.m_Header.m_uEntityID], dem.m_Header.m_uEntityID);
            } else if (item is DestroyEntitiesMessage dems) {
                // Console.WriteLine($"[{frame}] " + toJson(item));
                destroyEntity(entityIdToRecord[(int)dems.m_rootId], dems.m_rootId);
                foreach (var i in dems.m_ids) {
                    destroyEntity(entityIdToRecord[(int)i], i);
                }
            }
        }

        public void ApplyChefUpdate(int chefId, ChefSpecificData chef) {
            if (chefId == 0) {
                // Console.WriteLine(chef);
                return;
            }
            var chefRecord = entityIdToRecord[chefId];
            try {
                var chefData = new ChefState {
                    highlightedForPickup = chef.HighlightedForPickup <= 0 ? null : entityIdToRecord[chef.HighlightedForPickup],
                    highlightedForPlacement = chef.HighlightedForPlacement <= 0 ? null : entityIdToRecord[chef.HighlightedForPlacement],
                    highlightedForUse = chef.HighlightedForUse <= 0 ? null : entityIdToRecord[chef.HighlightedForUse],
                    currentlyInteracting = chef.InteractingEntity <= 0 ? null : entityIdToRecord[chef.InteractingEntity],
                    dashTimer = chef.DashTimer,
                    lastVelocity = chef.LastVelocity.FromThrift(),
                    aimingThrow = chef.AimingThrow,
                    movementInputSuppressed = chef.MovementInputSuppressed,
                    lastMoveInputDirection = chef.LastMoveInputDirection.FromThrift(),
                    impactStartTime = chef.ImpactStartTime,
                    impactTimer = chef.ImpactTimer,
                    impactVelocity = chef.ImpactVelocity.FromThrift(),
                    leftOverTime = chef.LeftOverTime,
                };
                chefRecord.chefState.ChangeTo(chefData, frame);
            } catch (Exception e) {
                Console.WriteLine($"Error applying chef update for {chefId}: {e}");
            }
        }

        public void ApplyEntityRegistryUpdateEarly(EntityRegistryData data) {
            Console.WriteLine($"Applying early registry update for {data.EntityId} {string.Join(", ", data.SyncEntityTypes.Select(t => ((EntityType)t).ToString()))}");
            FakeEntityRegistry.entityToTypes[data.EntityId] = data.SyncEntityTypes.Select(t => (EntityType)t).ToList();
        }

        public void ApplyEntityRegistryUpdateLate(EntityRegistryData data) {
            // Console.WriteLine($"Applying late registry update for {data.EntityId}");
            if (!entityIdToRecord.ContainsKey(data.EntityId)) {
                Console.WriteLine("Ignoring entity registry update for " + data.EntityId + ": " + data.Name);
                return;
            }
            if (entityIdToRecord[data.EntityId].displayName == "") {
                entityIdToRecord[data.EntityId].displayName = data.Name;
            }
        }
        public void ApplyGameUpdateWhenWarping(Serialisable item, Dictionary<int, EntityPath> entityIdToPath) {
            Action<int> spawnHandler = (int entityId) => {
                if (!entityIdToPath.ContainsKey(entityId)) {
                    Console.WriteLine($"[WARP] Incorrect spawning capture! Spawned entity {entityId} does not have an entity path attached!");
                    return;
                }
                var path = entityIdToPath[entityId];
                Console.WriteLine($"[WARP] Matched spawned entity {entityId} to path {path}");
                var record = setup.entityRecords.GetRecordFromPath(path.ids);
                entityIdToRecord[entityId] = record;
            };

            if (item is DestroyEntityMessage dem) {
                Console.WriteLine($"[WARP] Unmapped destroyed entity {dem.m_Header.m_uEntityID}");
                entityIdToRecord.Remove((int)dem.m_Header.m_uEntityID);
            } else if (item is DestroyEntitiesMessage dems) {
                foreach (var i in dems.m_ids) {
                    Console.WriteLine($"[WARP] Unmapped destroyed entity {i}");
                    entityIdToRecord.Remove((int)i);
                }
            } else if (item is SpawnEntityMessage sem) {
                spawnHandler((int)sem.m_DesiredHeader.m_uEntityID);
            } else if (item is SpawnPhysicalAttachmentMessage spam) {
                spawnHandler((int)spam.m_SpawnEntityData.m_DesiredHeader.m_uEntityID);
            }
        }

        public void ApplyPositionUpdate(int entityId, ItemData data) {
            // Console.WriteLine($"{entityId} received {data}");
            if (entityIdToRecord.ContainsKey(entityId)) {
                if (data.__isset.pos) {
                    entityIdToRecord[entityId].position.ChangeTo(data.Pos.FromThrift(), frame);
                }
                if (data.__isset.rotation) {
                    entityIdToRecord[entityId].rotation.ChangeTo(data.Rotation.FromThrift(), frame);
                }
                if (data.__isset.velocity) {
                    entityIdToRecord[entityId].velocity.ChangeTo(data.Velocity.FromThrift(), frame);
                }
                if (data.__isset.angularVelocity) {
                    entityIdToRecord[entityId].angularVelocity.ChangeTo(data.AngularVelocity.FromThrift(), frame);
                }
            }
        }

        // Advance certain progress bars such as chopping board, washing, plate respawn. These are not
        // explicitly given via server messages; we have to time them ourselves.
        public void AdvanceAutomaticProgress() {
            var frameTime = TimeSpan.FromSeconds(1) / Config.FRAMERATE;
            foreach (var entity in setup.entityRecords.FixedEntities) {
                var chopInteracters = entity.data.Last().chopInteracters;
                if (chopInteracters != null) {
                    var newData = entity.data.Last();
                    newData.chopInteracters = new Dictionary<GameEntityRecord, TimeSpan>(chopInteracters);
                    foreach (var (chef, remain) in chopInteracters) {
                        var newRemain = remain - frameTime;
                        if (newRemain < TimeSpan.Zero) {
                            newData.itemBeingChopped.progress.ChangeTo(newData.itemBeingChopped.progress.Last() + 0.2, frame);
                            newRemain += TimeSpan.FromSeconds(0.2);
                        }
                        newData.chopInteracters[chef] = newRemain;
                    }
                    entity.data.ChangeTo(newData, frame);
                }
                if (entity.data.Last().washers is HashSet<GameEntityRecord> washers && washers.Count > 0) {
                    entity.progress.AppendWith(frame, p => p + washers.Count * 1.0 / Config.FRAMERATE);
                }
                if (entity.data.Last().plateRespawnTimers is List<TimeSpan> timers) {
                    entity.data.AppendWith(frame, d => {
                        d.plateRespawnTimers = d.plateRespawnTimers
                            .Select(t => t - frameTime).Where(t => t > TimeSpan.Zero).ToList();
                        return d;
                    });
                }
                if (entity.data.Last().throwableItem.IsFlying) {
                    entity.data.AppendWith(frame, d => {
                        d.throwableItem.FlightTimer += frameTime;
                        return d;
                    });
                }
            }
            var totalCriticalSections = 0;
            foreach (var entity in setup.entityRecords.GenAllEntities()) {
                if (entity.existed.Last()) {
                    if (entity.IsInCriticalSectionForWarping(frame)) {
                        totalCriticalSections++;
                    }
                }
            }
            if (setup.entityRecords.CriticalSectionForWarping.Last() != totalCriticalSections) {
                setup.entityRecords.CriticalSectionForWarping.ChangeTo(totalCriticalSections, frame);
            }
        }
    }
}