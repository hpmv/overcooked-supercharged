using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPickupItemSwitcherExt
    {
        private static readonly Type type = typeof(ServerPickupItemSwitcher);
        private static readonly FieldInfo f_m_currentItemPrefabIndex = AccessTools.Field(type, "m_currentItemPrefabIndex");

        public static void SetCurrentItemPrefabIndexAndSendServerEvent(this ServerPickupItemSwitcher instance, int index)
        {
            f_m_currentItemPrefabIndex.SetValue(instance, index);
            instance.SendServerEvent(new PickupItemSwitcherMessage
            {
                m_itemIndex = index
            });
        }

        public static int GetCurrentItemPrefabIndex(this ServerPickupItemSwitcher instance)
        {
            return (int)f_m_currentItemPrefabIndex.GetValue(instance);
        }
    }

    public static class ServerPlacementItemSwitcherExt
    {
        private static readonly Type type = typeof(ServerPlacementItemSwitcher);
        private static readonly FieldInfo f_m_currentItemPrefabIndex = AccessTools.Field(type, "m_currentItemPrefabIndex");

        public static void SetCurrentItemPrefabIndexAndSendServerEvent(this ServerPlacementItemSwitcher instance, int index)
        {
            f_m_currentItemPrefabIndex.SetValue(instance, index);
            instance.SendServerEvent(new PickupItemSwitcherMessage
            {
                m_itemIndex = index
            });
        }

        public static int GetCurrentItemPrefabIndex(this ServerPlacementItemSwitcher instance)
        {
            return (int)f_m_currentItemPrefabIndex.GetValue(instance);
        }
    }
}
