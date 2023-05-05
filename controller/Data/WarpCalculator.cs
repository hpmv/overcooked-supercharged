using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hpmv {
    public class WarpCalculator {
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
                        var attachmentSpec = new AttachmentParentWarpData();
                        if (currentRecordToEntityId.ContainsKey(attachmentParent)) {
                            attachmentSpec.ParentEntityId = currentRecordToEntityId[attachmentParent];
                        } else {
                            attachmentSpec.ParentEntityPathReference = attachmentParent.path.ToThrift();
                        }
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

                if (spec.__isset.Equals(new EntityWarpSpec.Isset() {entityId = true})) {
                    continue;
                }
                warpSpec.Entities.Add(spec);
            }
            warpSpec.Frame = desiredFrame;
            warpSpec.RemainingTime = 0; // TODO: calculate this
            Console.WriteLine("WarpSpec dump:");
            // thrift dump
            Console.WriteLine(JsonConvert.SerializeObject(warpSpec));
            return warpSpec;
        }
    }
}