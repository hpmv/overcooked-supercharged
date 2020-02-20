using System.Collections.Generic;
using System.Linq;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class RealGameSimulator {
        public GameActionGraph Graph = new GameActionGraph();
        int frame = 0;
        Dictionary<int, int> depsRemaining = new Dictionary<int, int>();
        public GameEntityRecords Records { get; set; }
        public GameActionSequences Timings { get; set; }
        public GameMap Map { get; set; }

        List<(int actionId, int actionFrame)> inProgress = new List<(int actionId, int actionFrame)>();

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
            // Console.WriteLine("Got message " + (MessageType) msg.Type);
            if (item is EntitySynchronisationMessage sync) {
                var entityRecord = entityIdToRecord[(int)sync.m_Header.m_uEntityID];
                var specificData = entityRecord.data.Last();
                foreach (var (type, payload) in sync.m_Payloads) {
                    if (payload is ChefCarryMessage ccm) {
                        specificData.carriedItem = entityIdToRecord[(int)ccm.m_carriableItem];
                    } else if (payload is MixingStateMessage msm) {
                        specificData.progress = msm.m_mixingProgress;
                    } else if (payload is CookingStateMessage csm) {
                        specificData.progress = csm.m_cookingProgress;
                    } else if (payload is PhysicalAttachMessage pam) {
                        specificData.attachmentParent = pam.m_parent == -1 ? null : entityIdToRecord[pam.m_parent];
                    }
                }
                entityRecord.data.ChangeTo(specificData, frame);
            } else if (item is EntityEventMessage eem) {
                var entityRecord = entityIdToRecord[(int)eem.m_Header.m_uEntityID];
                var specificData = entityRecord.data.Last();
                var payload = eem.m_Payload;
                if (payload is ChefCarryMessage ccm) {
                    specificData.carriedItem = entityIdToRecord[(int)ccm.m_carriableItem];
                } else if (payload is MixingStateMessage msm) {
                    specificData.progress = msm.m_mixingProgress;
                } else if (payload is CookingStateMessage csm) {
                    specificData.progress = csm.m_cookingProgress;
                } else if (payload is PhysicalAttachMessage pam) {
                    specificData.attachmentParent = pam.m_parent == -1 ? null : entityIdToRecord[pam.m_parent];
                }
                entityRecord.data.ChangeTo(specificData, frame);
            } else if (item is SpawnEntityMessage sem) {
                var spawner = entityIdToRecord[(int)sem.m_SpawnerHeader.m_uEntityID];
                GameEntityRecord child = null;
                foreach (var spawned in spawner.spawned) {
                    if (!spawned.existed.Last()) {
                        child = spawned;
                        break;
                    }
                }
                if (child == null) {
                    child = new GameEntityRecord {
                        path = new EntityPath {
                            id = spawner.spawned.Count,
                            parent = spawner.path
                        },
                        spawner = spawner
                    };
                    spawner.spawned.Add(child);
                }
                child.existed.ChangeTo(true, frame);
                entityIdToRecord[(int)sem.m_DesiredHeader.m_uEntityID] = child;
            } else if (item is SpawnPhysicalAttachmentMessage spem) {

            } else if (item is DestroyEntityMessage dem) {
                entityIdToRecord[(int)dem.m_Header.m_uEntityID].existed.ChangeTo(false, frame);
            } else if (item is DestroyEntitiesMessage dems) {
                foreach (var i in dems.m_ids) {
                    entityIdToRecord[(int)i].existed.ChangeTo(false, frame);
                }
            }
        }

        public void ApplyChefUpdate(int chefId, CharPositionData chef) {
            var chefRecord = entityIdToRecord[chefId];
            var chefData = new ChefState {
                forward = chef.ForwardDirection.ToNumericsVector().XZ(),
                dashTimer = chef.DashTimer,
                highlightedForPickup = chef.HighlightedForPickup == -1 ? null : entityIdToRecord[chef.HighlightedForPickup],
                highlightedForPlacement = chef.HighlightedForPlacement == -1 ? null : entityIdToRecord[chef.HighlightedForPlacement],
                highlightedForUse = chef.HighlightedForUse == -1 ? null : entityIdToRecord[chef.HighlightedForUse],
            };
            chefRecord.chefState.ChangeTo(chefData, frame);
        }

        public void ApplyEntityRegistryUpdate(EntityRegistryData data) {
            FakeEntityRegistry.entityToTypes[data.EntityId] = data.SyncEntityTypes.Select(t => (EntityType)t).ToList();
            if (!entityIdToRecord.ContainsKey(data.EntityId)) {
                Records.RegisterKnownObject(data.Name, data.EntityId, data.Pos.ToNumericsVector().XZ());
            }
            entityIdToRecord[data.EntityId].displayName = data.Name;
        }

        public void ApplyPositionUpdate(int entityId, ItemData data) {
            entityIdToRecord[entityId].position.ChangeTo(data.Pos.ToNumericsVector(), frame);
        }
    }
}