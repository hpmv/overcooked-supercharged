using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hpmv {
    public static class WarpCalculator {
        public static WarpSpec CalculateWarp(
            Dictionary<int, GameEntityRecord> currentEntityIdToRecord,
            GameEntityRecords records,
            int originalFrame,
            int desiredFrame
        ) {
            var warpSpec = new WarpSpec() {
                EntitiesToDelete = new List<int>(),
                Entities = new List<EntityWarpSpec>(),
            };

            // Delete entities that don't exist in the desired frame. Also delete entities
            // that are unwarpable.
            var currentRecordToEntityId = new Dictionary<GameEntityRecord, int>();
            foreach (var kv in currentEntityIdToRecord) {
                if (!kv.Value.existed[desiredFrame]) {
                    warpSpec.EntitiesToDelete.Add(kv.Key);
                } else if (kv.Value.data[originalFrame].isUnwarpable) {
                    warpSpec.EntitiesToDelete.Add(kv.Key);
                } else {
                    currentRecordToEntityId[kv.Value] = kv.Key;
                }
            }

            Func<GameEntityRecord, EntityIdOrRef> getEntityIdOrRef = (record) => {
                if (currentRecordToEntityId.ContainsKey(record)) {
                    return new EntityIdOrRef { EntityId = currentRecordToEntityId[record] };
                } else {
                    return new EntityIdOrRef { EntityPathReference = record.path.ToThrift() };
                }
            };

            // Spawn entities that didn't exist in the original frame.
            foreach (var record in records.GenAllEntities()) {
                if (!record.existed[desiredFrame]) {
                    continue;
                }

                EntityWarpSpec spec;
                if (!currentRecordToEntityId.ContainsKey(record)) {
                    var spawningPath = record.prefab.SpawningPath;
                    var spawningPathEncoded = new List<int> {spawningPath.InitialFixedEntityId};
                    foreach (var spawnableId in spawningPath.SpawnableIds) {
                        spawningPathEncoded.Add(spawnableId);
                    }

                    spec = new EntityWarpSpec {
                        SpawningPath = spawningPathEncoded,
                        EntityPathReference = record.path.ToThrift(),
                    };
                } else {
                    spec = new EntityWarpSpec {
                        EntityId = currentRecordToEntityId[record],
                    };
                }
                if (record.prefab.CanBeAttached) {
                    if (record.data[desiredFrame].attachmentParent is GameEntityRecord attachmentParent) {
                        // TODO: We probably want to switch to attachment child instead of parent.
                        var attachmentSpec = new AttachmentParentWarpData();
                        attachmentSpec.ParentEntity = getEntityIdOrRef(attachmentParent);
                        spec.AttachmentParent = attachmentSpec;
                    } else {
                        spec.AttachmentParent = new AttachmentParentWarpData();
                        spec.Position = record.position[desiredFrame].ToThrift();
                        spec.Rotation = record.rotation[desiredFrame].ToThrift();
                        spec.Velocity = record.velocity[desiredFrame].ToThrift();
                        spec.AngularVelocity = record.angularVelocity[desiredFrame].ToThrift();
                    }
                }
                if (record.prefab.IsChef) {
                    spec.Position = record.position[desiredFrame].ToThrift();
                    spec.Rotation = record.rotation[desiredFrame].ToThrift();
                    spec.Velocity = record.velocity[desiredFrame].ToThrift();
                    spec.AngularVelocity = record.angularVelocity[desiredFrame].ToThrift();
                }
                if (record.prefab.IsCannon) {
                    var data = record.data[desiredFrame];
                    spec.Cannon = new CannonWarpData {
                        ModData = data.rawGameEntityData,
                    };
                    spec.PilotRotation = new PilotRotationWarpData {
                        Angle = data.pilotRotationAngle,
                    };
                }
                if (record.prefab.IsChef) {
                    var chefState = record.chefState[desiredFrame];
                    var interactingEntityId = -1;
                    if (chefState.currentlyInteracting != null) {
                        if (chefState.currentlyInteracting.path.ids.Length > 1) {
                            Console.WriteLine("[WARP] Error: Chef is interacting with a spawned entity; this is not yet supported!");
                        } else {
                            interactingEntityId = chefState.currentlyInteracting.path.ids[0];
                        }
                    }
                    spec.Chef = new ChefSpecificData {
                        HighlightedForPickup = -1, // not used
                        HighlightedForUse = -1, // not used
                        HighlightedForPlacement = -1, // not used
                        DashTimer = chefState.dashTimer,
                        InteractingEntity = interactingEntityId,
                        LastVelocity = chefState.lastVelocity.ToThrift(),
                        AimingThrow = chefState.aimingThrow,
                        MovementInputSuppressed = chefState.movementInputSuppressed,
                        LastMoveInputDirection = chefState.lastMoveInputDirection.ToThrift(),
                        ImpactStartTime = chefState.impactStartTime,
                        ImpactTimer = chefState.impactTimer,
                        ImpactVelocity = chefState.impactVelocity.ToThrift(),
                        LeftOverTime = chefState.leftOverTime,
                    };
                }
                if (record.prefab.IsBoard) {
                    spec.Workstation = new WorkstationWarpData();
                    var data = record.data[desiredFrame];
                    if (data.itemBeingChopped != null) {
                        spec.Workstation.Item = getEntityIdOrRef(data.itemBeingChopped);
                    }
                    if (data.chopInteracters != null) {
                        spec.Workstation.Interacters = new List<WorkstationInteracterWarpData>();
                        foreach (var kv in data.chopInteracters) {
                            spec.Workstation.Interacters.Add(new WorkstationInteracterWarpData {
                                ChefEntityId = kv.Key.path.ids[0],
                                ActionTimer = 0.2 - kv.Value.TotalSeconds,
                            });
                        }
                    }
                }
                if (record.prefab.IsChoppable) {
                    var data = record.data[desiredFrame];
                    var onWorkstation = data.attachmentParent != null && data.attachmentParent.prefab.IsBoard;
                    spec.WorkableItem = new WorkableItemWarpData {
                        OnWorkstation = onWorkstation,
                        Progress = (int)Math.Round(record.progress[desiredFrame] / 0.2),
                        SubProgress = 0,
                    };
                }
                if (record.prefab.IsThrowable) {
                    var data = record.data[desiredFrame].throwableItem;
                    spec.ThrowableItem = new ThrowableItemWarpData
                    {
                        IsFlying = data.IsFlying,
                        FlightTimer = data.FlightTimer.TotalMilliseconds,
                        ThrowerEntityId = data.thrower == null ? -1 : data.thrower.path.ids[0],
                        ThrowStartColliders = new List<ColliderRef>(),
                    };
                    if (data.ignoredColliders != null)
                    {
                        foreach (var collider in data.ignoredColliders)
                        {
                            spec.ThrowableItem.ThrowStartColliders.Add(new ColliderRef
                            {
                                ColliderIndex = collider.ColliderIndex,
                                Entity = getEntityIdOrRef(collider.Entity),
                            });
                        }
                    }
                }
                if (record.prefab.IsTerminal) {
                    spec.Terminal = new TerminalWarpData();
                    var data = record.data[desiredFrame];
                    if (data.sessionInteracter != null) {
                        spec.Terminal.InteracterEntityId = data.sessionInteracter.path.ids[0];
                    }
                }

                if (spec.__isset.Equals(new EntityWarpSpec.Isset() {entityId = true})) {
                    continue;
                }
                warpSpec.Entities.Add(spec);
            }
            warpSpec.Frame = desiredFrame;
            warpSpec.RemainingTime = 0; // TODO: calculate this
            // Console.WriteLine("WarpSpec dump:");
            // Console.WriteLine(JsonConvert.SerializeObject(warpSpec));
            return warpSpec;
        }
    }
}