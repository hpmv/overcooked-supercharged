using BitStream;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using static UnityEngine.PostProcessing.AntialiasingModel;

namespace SuperchargedPatch
{
    public static class ServerWorkstationExt
    {
        private static FieldInfo f_m_interacters = AccessTools.Field(typeof(ServerWorkstation), "m_interacters");
        private static FieldInfo f_m_item = AccessTools.Field(typeof(ServerWorkstation), "m_item");
        private static MethodInfo m_OnInteracterAdded = AccessTools.Method(typeof(ServerWorkstation), "OnInteracterAdded");
        private static MethodInfo m_OnItemAdded = AccessTools.Method(typeof(ServerWorkstation), "OnItemAdded");
        private static MethodInfo m_OnItemRemovedItem = AccessTools.Method(typeof(ServerWorkstation), "OnItemRemovedItem");
        public static List<ServerWorkstationInteracterExt> Interacters(this ServerWorkstation workstation)
        {
            var interacters = f_m_interacters.GetValue(workstation) as IList;
            var result = new List<ServerWorkstationInteracterExt>();
            foreach (var interacter in interacters)
            {
                result.Add(new ServerWorkstationInteracterExt(interacter));
            }
            return result;
        }

        public static void OnInteracterAdded(this ServerWorkstation workstation, GameObject interacter, Vector2 directionXZ)
        {
            m_OnInteracterAdded.Invoke(workstation, new object[] { interacter, directionXZ });
        }

        public static void OnItemAdded(this ServerWorkstation workstation, IAttachment attachment)
        {
            m_OnItemAdded.Invoke(workstation, new object[] { attachment });
        }

        public static void OnItemRemovedItem(this ServerWorkstation workstation, IAttachment attachment)
        {
            m_OnItemRemovedItem.Invoke(workstation, new object[] { attachment });
        }

        public static ServerWorkableItem Item(this ServerWorkstation workstation)
        {
            return f_m_item.GetValue(workstation) as ServerWorkableItem;
        }
    }

    public class ServerWorkstationInteracterExt
    {
        private object underlying;

        private static Type type = AccessTools.Inner(typeof(ServerWorkstation), "Interacter");
        private static FieldInfo f_m_object = AccessTools.Field(type, "m_object");
        private static FieldInfo f_m_actionTimer = AccessTools.Field(type, "m_actionTimer");

        public ServerWorkstationInteracterExt(object underlying)
        {
            this.underlying = underlying;
        }


        public GameObject m_object
        {
            get
            {
                return f_m_object.GetValue(underlying) as GameObject;
            }
        }

        public float m_actionTimer
        {
            get
            {
                return (float)f_m_actionTimer.GetValue(underlying);
            }
            set
            {
                f_m_actionTimer.SetValue(underlying, value);
            }
        }
    }

    public class ClientWorkstationInteracterExt
    {
        public readonly object underlying;
        private static Type type = AccessTools.Inner(typeof(ClientWorkstation), "Interacter");
        private static FieldInfo f_Method = AccessTools.Field(type, "Method");

        public ClientWorkstationInteracterExt(object underlying)
        {
            this.underlying = underlying;
        }

        public static ClientWorkstationInteracterExt Create(TriggerCallback component, float delay, float duration, TriggerCallback.Callback method)
        {
            var interacter = Activator.CreateInstance(type, new object[] { component, delay, duration, method });
            return new ClientWorkstationInteracterExt(interacter);
        }

        public TriggerCallback.Callback Method
        {
            get
            {
                return f_Method.GetValue(underlying) as TriggerCallback.Callback;
            }
        }
    }

    public static class ClientWorkstationExt
    {
        private static readonly Type type = typeof(ClientWorkstation);
        private static readonly MethodInfo m_StartWorking = AccessTools.Method(type, "StartWorking");
        private static readonly MethodInfo m_StopWorking = AccessTools.Method(type, "StopWorking");
        private static readonly MethodInfo m_OnChop = AccessTools.Method(type, "OnChop");

        public static void StartWorking(this ClientWorkstation workstation, GameObject interacter, ClientWorkableItem item)
        {
            m_StartWorking.Invoke(workstation, new object[] { interacter, item });
        }

        public static void StopWorking(this ClientWorkstation workstation, GameObject interacter)
        {
            m_StopWorking.Invoke(workstation, new object[] { interacter });
        }

        public static void OnChop(this ClientWorkstation workstation, Transform transform)
        {
            m_OnChop.Invoke(workstation, new object[] { transform });
        }
    }

    [HarmonyPatch(typeof(ServerWorkstation), "SynchroniseInteractionState")]
    public static class PatchServerWorkstationSynchroniseInteractionState
    {
        // Patch this, so that we allow starting interaction without a workable item. This is needed if we need
        // to warp to the exact frame where the workable item is finished and replaced with the spawned item.
        // The original flow when chopping an item would not actually stop the chopping interaction right away,
        // but wait for the next frame and have the PlayerControls::Update function trigger EndInteraction.
        // So, if we warp to this frame, we would have m_item == null, but still calling SynchroniseInteractionState
        // with _active = true, and that would normally NRE.
        [HarmonyPrefix]
        public static bool Prefix(object /*ServerWorkstation.Interacter*/ _interactor, bool _active, ServerWorkstation __instance, WorkstationMessage ___m_data, ServerWorkableItem ___m_item)
        {
            EntitySerialisationEntry entry = EntitySerialisationRegistry.GetEntry(new ServerWorkstationInteracterExt(_interactor).m_object);
            if (entry != null)
            {
                ___m_data.m_interacting = _active;
                ___m_data.m_interactor = entry;
                if (_active && ___m_item != null)  // Added ___m_item != null check
                {
                    EntitySerialisationEntry entry2 = EntitySerialisationRegistry.GetEntry(___m_item.gameObject);
                    ___m_data.m_item = entry2;
                }
                else
                {
                    ___m_data.m_item = null;
                }
                __instance.SendServerEvent(___m_data);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkstationMessage), "Serialise")]
    public static class PatchWorkstationMessageSerialise
    {
        [HarmonyPrefix]
        public static bool Prefix(BitStreamWriter writer, WorkstationMessage __instance)
        {
            writer.Write(__instance.m_interacting);
            __instance.m_interactor.m_Header.Serialise(writer);
            if (__instance.m_interacting)
            {
                if (__instance.m_item != null)
                {
                    __instance.m_item.m_Header.Serialise(writer);
                } else
                {
                    writer.Write(0, 10);
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ClientWorkstation), "ApplyServerEvent")]
    public static class PatchClientWorkstationApplyServerEvent
    {
        [HarmonyPrefix]
        public static bool Prefix(Serialisable serialisable, ClientWorkstation __instance)
        {
            WorkstationMessage workstationMessage = (WorkstationMessage)serialisable;
            EntitySerialisationEntry entry = EntitySerialisationRegistry.GetEntry(workstationMessage.m_interactorHeader.m_uEntityID);
            GameObject interacter = (entry == null) ? null : entry.m_GameObject;
            if (workstationMessage.m_interacting)
            {
                if (workstationMessage.m_itemHeader.m_uEntityID == 0)
                {
                    __instance.StartWorking(interacter, null);
                }
                else
                {
                    GameObject gameObject = EntitySerialisationRegistry.GetEntry(workstationMessage.m_itemHeader.m_uEntityID).m_GameObject;
                    __instance.StartWorking(interacter, gameObject.RequireComponent<ClientWorkableItem>());
                }
            }
            else
            {
                __instance.StopWorking(interacter);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ClientWorkstation), "StartWorking")]
    public static class PatchClientWorkstationStartWorking
    {
        [HarmonyPrefix]
        public static bool Prefix(ClientWorkstation __instance, GameObject _interacter, ClientWorkableItem _item, ref ClientWorkableItem ___m_item, ParticleSystem ___m_chopPFXInstance,
            ClientAttachStation ___m_attachStation, Workstation ___m_workstation, IList ___m_interacters, ClientInteractable ___m_interactable)
        {
            ___m_item = _item;
            TriggerCallback callbackComponent = _interacter.RequestComponent<TriggerCallback>();
            if (callbackComponent != null)
            {
                AnimationEventData animationEventData = _interacter.RequireComponentRecursive<AnimationEventData>();
                float duration;
                float delay;
                animationEventData.GetTriggerData("Chop", "Impact", out duration, out delay);
                if (___m_chopPFXInstance != null && _item != null)
                {
                    ___m_chopPFXInstance.transform.localPosition = ___m_attachStation.GetAttachPoint(_item.gameObject).localPosition;
                    ___m_chopPFXInstance.Play();
                }
                var interacter = ClientWorkstationInteracterExt.Create(callbackComponent, delay, duration, delegate ()
                {
                    __instance.OnChop(callbackComponent.transform);
                });
                callbackComponent.RegisterCallback(___m_workstation.m_chopTrigger, interacter.Method);
                ___m_interacters.Add(interacter.underlying);
                ___m_interactable.SetStickyInteractionCallback(new Generic<bool>(__instance.InteractionIsSticky));
            }
            return false;
        }
    }


    // DEBUG ingredient container
    [HarmonyPatch(typeof(ServerIngredientCatcher), "CanHandleCatch")]
    public static class TempPatch1
    {
        [HarmonyPostfix]
        public static void Postfix(ICatchable _object, bool __result, ServerIngredientCatcher __instance, IAttachment ___m_attachment, QueryForCatching ___m_allowCatchingCallback)
        {
            var ingredientCatcher = __instance.GetComponent<IngredientCatcher>();
            Console.WriteLine($"{_object.AccessGameObject().name} CanHandleCatch: {__result}");
            Console.WriteLine($"  AllowCatch? {_object.AllowCatch(__instance, Vector2.zero)}");
            Console.WriteLine($"  IngredientCatcher requires attached? {ingredientCatcher.m_requireAttached}");
            Console.WriteLine($"  m_attachment is null ? {___m_attachment == null}");
            Console.WriteLine($"  m_attachment is attached ? {___m_attachment?.IsAttached() ?? false}");
            Console.WriteLine($"  m_allowCatchingCallback? {___m_allowCatchingCallback(_object.AccessGameObject())}");
            IOrderDefinition orderDefinition = _object.AccessGameObject().RequestInterface<IOrderDefinition>();
            if (orderDefinition != null)
            {
                AssembledDefinitionNode orderComposition = orderDefinition.GetOrderComposition().Simpilfy();
                Console.WriteLine($"  OrderComposition: {orderComposition.GetType().Name}");
            }
        }
    }


}
