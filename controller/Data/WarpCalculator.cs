using System.Collections.Generic;

namespace Hpmv {
    public class WarpCalculator {
        public static WarpSpec CalculateWarp(
            Dictionary<int, GameEntityRecord> currentEntityIdToRecord,
            GameEntityRecords records,
            int desiredFrame
        ) {
            var warpSpec = new WarpSpec();

            // Delete entities that don't exist in the desired frame.
            foreach (var kv in currentEntityIdToRecord) {
                if (!kv.Value.existed[desiredFrame]) {
                    warpSpec.EntitiesToDelete.Add(kv.Key);
                }
            }

            var currentRecordToEntityId = new Dictionary<GameEntityRecord, int>();
            foreach (var kv in currentEntityIdToRecord) {
                currentRecordToEntityId[kv.Value] = kv.Key;
            }

            // Spawn entities that didn't exist in the current frame.
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
                // TODO: how to know if the object is a PhysicalAttachment to begin with?
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

                warpSpec.Entities.Add(spec);
            }
            warpSpec.Frame = desiredFrame;
            warpSpec.RemainingTime = 0; // TODO: calculate this
            return warpSpec;
        }
    }
}