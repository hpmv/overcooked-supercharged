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
        public bool isFlying;
        public bool isUnwarpable;
        public byte[] rawGameEntityData;

        public Save.SpecificEntityData ToProto() {
            var result = new Save.SpecificEntityData {
                AttachmentParent = attachmentParent?.path?.ToProto(),
                Attachment = attachment?.path?.ToProto(),
                ItemBeingChopped = itemBeingChopped?.path?.ToProto(),
                IsFlying = isFlying,
                IsUnwarpable = isUnwarpable,
                RawGameEntityData = rawGameEntityData == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(rawGameEntityData),
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
                isFlying = data.IsFlying,
                isUnwarpable = data.IsUnwarpable,
                rawGameEntityData = data.RawGameEntityData.IsEmpty ? null : data.RawGameEntityData.ToByteArray(),
            };
        }
    }

}