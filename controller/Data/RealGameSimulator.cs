using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class RealGameSimulator {
        public GameActionGraph Graph = new GameActionGraph();
        int frame = 0;
        Dictionary<int, int> depsRemaining = new Dictionary<int, int>();
        public GameEntityRecords Records { get; set; }
        public GameActionSequences Timings { get; set; }
        public GameMap Map { get; set; }
        public int Frame { get { return frame; } }

        public List<(int actionId, int actionFrame)> inProgress = new List<(int actionId, int actionFrame)>();

        Dictionary<int, GameEntityRecord> entityIdToRecord = new Dictionary<int, GameEntityRecord>();

        public void Reset() {
            FakeEntityRegistry.entityToTypes.Clear();
            frame = 0;
            depsRemaining.Clear();
            inProgress.Clear();
            entityIdToRecord.Clear();

            Records.CleanRecordsFromFrame(0);
            foreach (var entry in Records.FixedEntities) {
                entityIdToRecord[entry.path.id] = entry;
            }

            for (int i = 0; i < Graph.actions.Count; i++) {
                if (Graph.actions[i] != null) {
                    if (Graph.deps[i].Count == 0) {
                        inProgress.Add((i, 0));
                        Timings.NodeById[i].Predictions.StartFrame = frame;
                    } else {
                        depsRemaining[i] = Graph.deps[i].Count;
                    }
                }
            }
        }

        public InputData Step() {
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
                            newInProgress.Add((fwd, 0));
                            Timings.NodeById[fwd].Predictions.StartFrame = frame;
                        }
                    }
                } else {
                    newInProgress.Add((actionId, actionFrame + 1));
                }
            }
            inProgress = newInProgress;

            var inputData = new InputData {
                Input = new Dictionary<int, OneInputData>()
            };
            foreach (var entry in Records.Chefs) {
                var chefId = entry.Key.path.id;
                var desiredInput = newControllers.GetValueOrDefault(entry.Key);
                var (newState, actualInput) = entry.Value.Last().ApplyInputAndAdvanceFrame(desiredInput);
                entry.Value.ChangeTo(newState, frame);
                inputData.Input[chefId] = new OneInputData {
                    Pad = new PadDirection { X = actualInput.axes.X, Y = actualInput.axes.Y },
                    PickDown = actualInput.primary.justPressed,
                    ChopDown = actualInput.secondary.isDown,
                    ThrowDown = actualInput.secondary.justPressed,
                    ThrowUp = actualInput.secondary.justReleased,
                    DashDown = actualInput.dash.justPressed
                };
            }
            frame++;
            return inputData;
        }


        public void ApplyGameUpdate(Serialisable item) {
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
                            id = spawner.spawned.Count,
                            parent = spawner.path
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
                child.position.ChangeTo(sem.m_Position.ToNumericsVector(), frame);
                spawner.nextSpawnId.ChangeTo(spawner.nextSpawnId.Last() + 1, frame);
                entityIdToRecord[(int)sem.m_DesiredHeader.m_uEntityID] = child;
            };

            // Console.WriteLine("Got message " + (MessageType) msg.Type);
            if (item is EntitySynchronisationMessage sync) {
                if (!entityIdToRecord.ContainsKey((int)sync.m_Header.m_uEntityID)) {
                    Console.WriteLine("Ignoring entity sync event for " + sync.m_Header.m_uEntityID);
                    return;
                }
                var entityRecord = entityIdToRecord[(int)sync.m_Header.m_uEntityID];
                var specificData = entityRecord.data.Last();
                foreach (var (type, payload) in sync.m_Payloads) {
                    if (payload is ChefCarryMessage ccm) {
                        specificData.carriedItem = ccm.m_carriableItem == 0 ? null : entityIdToRecord[(int)ccm.m_carriableItem];
                    } else if (payload is MixingStateMessage msm) {
                        specificData.progress = msm.m_mixingProgress;
                    } else if (payload is CookingStateMessage csm) {
                        specificData.progress = csm.m_cookingProgress;
                    } else if (payload is PhysicalAttachMessage pam) {
                        specificData.attachmentParent = pam.m_parent <= 0 ? null : entityIdToRecord[pam.m_parent];
                    }
                }
                entityRecord.data.ChangeTo(specificData, frame);
            } else if (item is EntityEventMessage eem) {
                if (!entityIdToRecord.ContainsKey((int)eem.m_Header.m_uEntityID)) {
                    Console.WriteLine("Ignoring entity event for " + eem.m_Header.m_uEntityID);
                    return;
                }
                var entityRecord = entityIdToRecord[(int)eem.m_Header.m_uEntityID];
                var specificData = entityRecord.data.Last();
                var payload = eem.m_Payload;
                if (payload is ChefCarryMessage ccm) {
                    specificData.carriedItem = ccm.m_carriableItem == 0 ? null : entityIdToRecord[(int)ccm.m_carriableItem];
                } else if (payload is MixingStateMessage msm) {
                    specificData.progress = msm.m_mixingProgress;
                } else if (payload is CookingStateMessage csm) {
                    specificData.progress = csm.m_cookingProgress;
                } else if (payload is PhysicalAttachMessage pam) {
                    Console.WriteLine("Attachment msg for " + entityRecord + ": " + pam.m_parent);
                    specificData.attachmentParent = pam.m_parent <= 0 ? null : entityIdToRecord[pam.m_parent];
                }
                entityRecord.data.ChangeTo(specificData, frame);
            } else if (item is SpawnEntityMessage sem) {
                spawnHandler(sem);
            } else if (item is SpawnPhysicalAttachmentMessage spem) {
                spawnHandler(spem.m_SpawnEntityData);
            } else if (item is DestroyEntityMessage dem) {
                entityIdToRecord[(int)dem.m_Header.m_uEntityID].existed.ChangeTo(false, frame);
            } else if (item is DestroyEntitiesMessage dems) {
                foreach (var i in dems.m_ids) {
                    entityIdToRecord[(int)i].existed.ChangeTo(false, frame);
                }
            }
        }

        public void ApplyChefUpdate(int chefId, CharPositionData chef) {
            if (chefId == 0) {
                Console.WriteLine(chef);
                return;
            }
            var chefRecord = entityIdToRecord[chefId];
            var chefData = new ChefState {
                forward = chef.ForwardDirection.ToNumericsVector().XZ(),
                dashTimer = chef.DashTimer,
                highlightedForPickup = chef.HighlightedForPickup <= 0 ? null : entityIdToRecord[chef.HighlightedForPickup],
                highlightedForPlacement = chef.HighlightedForPlacement <= 0 ? null : entityIdToRecord[chef.HighlightedForPlacement],
                highlightedForUse = chef.HighlightedForUse <= 0 ? null : entityIdToRecord[chef.HighlightedForUse],
            };
            chefRecord.chefState.ChangeTo(chefData, frame);
        }

        public void ApplyEntityRegistryUpdateEarly(EntityRegistryData data) {
            // Console.WriteLine($"Applying early registry update for {data.EntityId}");
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

        public void ApplyPositionUpdate(int entityId, ItemData data) {
            if (entityIdToRecord.ContainsKey(entityId)) {
                entityIdToRecord[entityId].position.ChangeTo(data.Pos.ToNumericsVector(), frame);
            }
        }
    }
}