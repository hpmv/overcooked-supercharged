using System;
using System.Collections.Generic;
using System.Linq;

namespace Hpmv {
    public struct SpecificEntityData {
        public GameEntityRecord attachmentParent;
        public GameEntityRecord attachment;
        public List<int> contents;
        // 0.2 seconds per interaction, 7 max progress.
        public Dictionary<GameEntityRecord, TimeSpan> chopInteracters;
        public GameEntityRecord itemBeingChopped;
        public HashSet<GameEntityRecord> washers;
        public int numPlates;
        public List<TimeSpan> plateRespawnTimers;
        public SpecificEntityData_ThrowableItem throwableItem;
        public byte[] rawGameEntityData;
        public GameEntityRecord sessionInteracter;
        public float pilotRotationAngle;
        public int switchingIndex;

        public Save.SpecificEntityData ToProto() {
            var result = new Save.SpecificEntityData {
                AttachmentParent = attachmentParent?.path?.ToProto(),
                Attachment = attachment?.path?.ToProto(),
                ItemBeingChopped = itemBeingChopped?.path?.ToProto(),
                ThrowableItem = throwableItem.ToProto(),
                RawGameEntityData = rawGameEntityData == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(rawGameEntityData),
                SessionInteracter = sessionInteracter?.path?.ToProto(),
                PilotRotationAngle = pilotRotationAngle,
                SwitchingIndex = switchingIndex,
            };
            if (contents != null) {
                result.Contents.AddRange(contents);
            }
            if (chopInteracters != null) {
                foreach (var (chef, time) in chopInteracters) {
                    result.ChopInteracters[chef.path.ids[0]] = time.TotalMilliseconds;
                }
            }
            if (washers != null) {
                foreach (var washer in washers) {
                    result.Washers.Add(washer.path.ToProto());
                }
            }
            result.NumPlates = numPlates;
            if (plateRespawnTimers != null) {
                foreach (var timer in plateRespawnTimers) {
                    result.PlateRespawnTimers.Add(timer.TotalMilliseconds);
                }
            }
            return result;
        }
    }

    public static class SpecificEntityDataFromProto {
        public static SpecificEntityData FromProto(this Save.SpecificEntityData data, LoadContext context) {
            return new SpecificEntityData {
                attachmentParent = data.AttachmentParent.FromProtoRef(context),
                attachment = data.Attachment.FromProtoRef(context),
                itemBeingChopped = data.ItemBeingChopped.FromProtoRef(context),
                contents = data.Contents.Count == 0 ? null : data.Contents.ToList(),
                chopInteracters = data.ChopInteracters.Count == 0 ? null :
                    data.ChopInteracters.ToDictionary(kv => context.GetRootRecord(kv.Key), kv => TimeSpan.FromMilliseconds(kv.Value)),
                washers = data.Washers.Count == 0 ? null : data.Washers.Select(washer => washer.FromProtoRef(context)).ToHashSet(),
                numPlates = data.NumPlates,
                plateRespawnTimers = data.PlateRespawnTimers.Count == 0 ? null : data.PlateRespawnTimers.Select(t => TimeSpan.FromMilliseconds(t)).ToList(),
                throwableItem = data.ThrowableItem?.FromProto(context) ?? default,
                rawGameEntityData = data.RawGameEntityData.IsEmpty ? null : data.RawGameEntityData.ToByteArray(),
                sessionInteracter = data.SessionInteracter.FromProtoRef(context),
                pilotRotationAngle = data.PilotRotationAngle,
                switchingIndex = data.SwitchingIndex,
            };
        }
    }

    public struct SpecificEntityData_ThrowableItem {
        public bool IsFlying { get; set; }
        public TimeSpan FlightTimer { get; set; }
        public GameEntityRecord thrower { get; set; }
        public (GameEntityRecord Entity, int ColliderIndex)[] ignoredColliders { get; set; }

        public Hpmv.Save.SpecificEntityData_ThrowableItem ToProto() {
            var result = new Hpmv.Save.SpecificEntityData_ThrowableItem {
                IsFlying = IsFlying,
                FlightTimer = FlightTimer.TotalMilliseconds,
                Thrower = thrower?.path?.ToProto(),
            };
            if (ignoredColliders != null) {
                foreach (var (entity, colliderIndex) in ignoredColliders) {
                    result.IgnoredColliders.Add(new Hpmv.Save.SavedCollider {
                        Entity = entity.path.ToProto(),
                        ColliderIndex = colliderIndex,
                    });
                }
            }
            return result;
        }
    }

    public static class SpecificEntityData_ThrowableItemFromProto {
        public static SpecificEntityData_ThrowableItem FromProto(this Save.SpecificEntityData_ThrowableItem data, LoadContext context) {
            return new SpecificEntityData_ThrowableItem {
                IsFlying = data.IsFlying,
                FlightTimer = TimeSpan.FromMilliseconds(data.FlightTimer),
                thrower = data.Thrower.FromProtoRef(context),
                ignoredColliders = data.IgnoredColliders.Select(collider => (collider.Entity.FromProtoRef(context), collider.ColliderIndex)).ToArray(),
            };
        }
    }
}