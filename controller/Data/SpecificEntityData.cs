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
        public List<GameEntityRecord> interacters;
        public GameEntityRecord interactingWith;
        public int numPlates;
        public List<(GameEntityRecord station, TimeSpan timer)> plateRespawns;
        public SpecificEntityData_ThrowableItem throwableItem;
        public byte[] rawGameEntityData;
        public GameEntityRecord sessionInteracter;
        public float pilotRotationAngle;
        public int switchingIndex;
        public List<GameEntityRecord> stackContents;

        public Save.SpecificEntityData ToProto() {
            var result = new Save.SpecificEntityData {
                AttachmentParent = attachmentParent?.path?.ToProto(),
                Attachment = attachment?.path?.ToProto(),
                ItemBeingChopped = itemBeingChopped?.path?.ToProto(),
                ThrowableItem = throwableItem.ToProto(),
                RawGameEntityData = rawGameEntityData == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(rawGameEntityData),
                SessionInteracter = sessionInteracter?.path?.ToProto(),
                InteractingWith = interactingWith?.path?.ToProto(),
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
            if (interacters != null) {
                foreach (var interacter in interacters) {
                    result.Interacters.Add(interacter.path.ToProto());
                }
            }
            result.NumPlates = numPlates;
            if (plateRespawns != null) {
                foreach (var (station, timer) in plateRespawns) {
                    result.PlateRespawns.Add(
                        new Save.SpecificEntityData_PlateRespawn {
                            PlateReturnStation = station.path.ids[0],
                            Timer = timer.TotalMilliseconds,
                        }
                    );
                }
            }
            if (stackContents != null) {
                foreach (var item in stackContents) {
                    result.StackContents.Add(item.path.ToProto());
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
                interacters = data.Interacters.Count == 0 ? null : data.Interacters.Select(interacter => interacter.FromProtoRef(context)).ToList(),
                interactingWith = data.InteractingWith.FromProtoRef(context),
                numPlates = data.NumPlates,
                plateRespawns = data.PlateRespawns.Count == 0 ? null :
                    data.PlateRespawns.Select(timer => (context.GetRootRecord(timer.PlateReturnStation), TimeSpan.FromMilliseconds(timer.Timer))).ToList(),
                throwableItem = data.ThrowableItem?.FromProto(context) ?? default,
                rawGameEntityData = data.RawGameEntityData.IsEmpty ? null : data.RawGameEntityData.ToByteArray(),
                sessionInteracter = data.SessionInteracter.FromProtoRef(context),
                pilotRotationAngle = data.PilotRotationAngle,
                switchingIndex = data.SwitchingIndex,
                stackContents = data.StackContents.Count == 0 ? null : data.StackContents.Select(item => item.FromProtoRef(context)).ToList(),
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

    public static class ListShallowCloning {
        public static List<T> ShallowCopyAndEnsureList<T>(this List<T> list) where T : class {
            if (list == null) {
                return new List<T>();
            }
            return new List<T>(list);
        }
    }
}