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
    }
}
