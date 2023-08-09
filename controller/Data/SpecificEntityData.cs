using System;
using System.Collections.Generic;
using System.Linq;
using OrderController;

namespace Hpmv
{
    public struct SpecificEntityData
    {
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
        public SpecificEntityData_KitchenFlowController kitchenFlowController;
        public bool isCookingStationTurnedOn;
        public bool isCookingStationCooking;
        public GameEntityRecord plateReturnStationStack;

        public Save.SpecificEntityData ToProto()
        {
            var result = new Save.SpecificEntityData
            {
                AttachmentParent = attachmentParent?.path?.ToProto(),
                Attachment = attachment?.path?.ToProto(),
                ItemBeingChopped = itemBeingChopped?.path?.ToProto(),
                ThrowableItem = throwableItem.ToProto(),
                RawGameEntityData = rawGameEntityData == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(rawGameEntityData),
                SessionInteracter = sessionInteracter?.path?.ToProto(),
                InteractingWith = interactingWith?.path?.ToProto(),
                PilotRotationAngle = pilotRotationAngle,
                SwitchingIndex = switchingIndex,
                KitchenFlowController = kitchenFlowController.ToProto(),
                IsCookingStationTurnedOn = isCookingStationTurnedOn,
                IsCookingStationCooking = isCookingStationCooking,
                PlateReturnStationStack = plateReturnStationStack?.path?.ToProto(),
            };
            if (contents != null)
            {
                result.Contents.AddRange(contents);
            }
            if (chopInteracters != null)
            {
                foreach (var (chef, time) in chopInteracters)
                {
                    result.ChopInteracters[chef.path.ids[0]] = time.TotalMilliseconds;
                }
            }
            if (interacters != null)
            {
                foreach (var interacter in interacters)
                {
                    result.Interacters.Add(interacter.path.ToProto());
                }
            }
            result.NumPlates = numPlates;
            if (plateRespawns != null)
            {
                foreach (var (station, timer) in plateRespawns)
                {
                    result.PlateRespawns.Add(
                        new Save.SpecificEntityData_PlateRespawn
                        {
                            PlateReturnStation = station.path.ids[0],
                            Timer = timer.TotalMilliseconds,
                        }
                    );
                }
            }
            if (stackContents != null)
            {
                foreach (var item in stackContents)
                {
                    result.StackContents.Add(item.path.ToProto());
                }
            }
            return result;
        }
    }

    public static class SpecificEntityDataFromProto
    {
        public static SpecificEntityData FromProto(this Save.SpecificEntityData data, LoadContext context)
        {
            return new SpecificEntityData
            {
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
                kitchenFlowController = data.KitchenFlowController?.FromProto(context) ?? default,
                isCookingStationTurnedOn = data.IsCookingStationTurnedOn,
                isCookingStationCooking = data.IsCookingStationCooking,
                plateReturnStationStack = data.PlateReturnStationStack.FromProtoRef(context),
            };
        }
    }

    public struct SpecificEntityData_ThrowableItem
    {
        public bool IsFlying { get; set; }
        public TimeSpan FlightTimer { get; set; }
        public GameEntityRecord thrower { get; set; }
        public (GameEntityRecord Entity, int ColliderIndex)[] ignoredColliders { get; set; }

        public Hpmv.Save.SpecificEntityData_ThrowableItem ToProto()
        {
            var result = new Hpmv.Save.SpecificEntityData_ThrowableItem
            {
                IsFlying = IsFlying,
                FlightTimer = FlightTimer.TotalMilliseconds,
                Thrower = thrower?.path?.ToProto(),
            };
            if (ignoredColliders != null)
            {
                foreach (var (entity, colliderIndex) in ignoredColliders)
                {
                    result.IgnoredColliders.Add(new Hpmv.Save.SavedCollider
                    {
                        Entity = entity.path.ToProto(),
                        ColliderIndex = colliderIndex,
                    });
                }
            }
            return result;
        }
    }

    public static class SpecificEntityData_ThrowableItemFromProto
    {
        public static SpecificEntityData_ThrowableItem FromProto(this Save.SpecificEntityData_ThrowableItem data, LoadContext context)
        {
            return new SpecificEntityData_ThrowableItem
            {
                IsFlying = data.IsFlying,
                FlightTimer = TimeSpan.FromMilliseconds(data.FlightTimer),
                thrower = data.Thrower.FromProtoRef(context),
                ignoredColliders = data.IgnoredColliders.Select(collider => (collider.Entity.FromProtoRef(context), collider.ColliderIndex)).ToArray(),
            };
        }
    }

    public struct SpecificEntityData_KitchenFlowController
    {
        public ServerOrderData[] activeOrders;
        public int nextOrderId;
        public int lastComboIndex;
        public TimeSpan timeSinceLastOrder;
        public TeamMonitor.TeamScoreStats teamScore;
        public List<FutureOrder> futureOrders;

        public Save.SpecificEntityData_KitchenFlowController ToProto()
        {
            var result = new Save.SpecificEntityData_KitchenFlowController
            {
                NextOrderId = nextOrderId,
                LastComboIndex = lastComboIndex,
                TimeSinceLastOrder = timeSinceLastOrder.TotalMilliseconds,
                TeamScore = teamScore == null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(teamScore.ToBytes()),
            };
            if (activeOrders != null)
            {
                foreach (var order in activeOrders)
                {
                    result.ActiveOrders.Add(Google.Protobuf.ByteString.CopyFrom(order.ToBytes()));
                }
            }
            if (futureOrders != null)
            {
                foreach (var order in futureOrders)
                {
                    result.FutureOrders.Add(new Save.FutureOrder
                    {
                        Ingredients = { order.Order },
                        OrderIndex = order.Index,
                    });
                }
            }
            return result;
        }
    }

    public static class SpecificEntityData_KitchenFlowControllerFromProto
    {
        public static SpecificEntityData_KitchenFlowController FromProto(this Save.SpecificEntityData_KitchenFlowController data, LoadContext context)
        {
            return new SpecificEntityData_KitchenFlowController
            {
                activeOrders = data.ActiveOrders.Select(order => new ServerOrderData().FromBytes(order.ToByteArray())).ToArray(),
                nextOrderId = data.NextOrderId,
                lastComboIndex = data.LastComboIndex,
                timeSinceLastOrder = TimeSpan.FromMilliseconds(data.TimeSinceLastOrder),
                teamScore = data.TeamScore.Length == 0 ? null : new TeamMonitor.TeamScoreStats().FromBytes(data.TeamScore.ToByteArray()),
                futureOrders = data.FutureOrders.Select(order => new FutureOrder(order.Ingredients.ToList(), order.OrderIndex)).ToList(),
            };
        }
    }

    public record class FutureOrder(List<int> Order, int Index);

    public static class ListShallowCloning
    {
        public static List<T> ShallowCopyAndEnsureList<T>(this List<T> list) where T : class
        {
            if (list == null)
            {
                return new List<T>();
            }
            return new List<T>(list);
        }

        public static T[] EmptyIfNull<T>(this T[] array)
        {
            return array ?? new T[0];
        }
    }
}