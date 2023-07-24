using HarmonyLib;
using OrderController;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerOrderControllerBaseExt
    {
        private static readonly Type type = typeof(ServerOrderControllerBase);
        private static readonly FieldInfo f_m_nextOrderID = AccessTools.Field(type, "m_nextOrderID");
        private static readonly FieldInfo f_m_activeOrders = AccessTools.Field(type, "m_activeOrders");
        private static readonly FieldInfo f_m_roundData = AccessTools.Field(type, "m_roundData");
        private static readonly FieldInfo f_m_roundInstanceData = AccessTools.Field(type, "m_roundInstanceData");
        private static readonly FieldInfo f_m_timerUntilOrder = AccessTools.Field(type, "m_timerUntilOrder");
        private static readonly FieldInfo f_m_comboIndex = AccessTools.Field(type, "m_comboIndex");
        private static readonly MethodInfo m_GetNextTimeBetweenOrders = AccessTools.Method(type, "GetNextTimeBetweenOrders");

        public static void SetNextOrderID(this ServerOrderControllerBase instance, uint value)
        {
            f_m_nextOrderID.SetValue(instance, value);
        }

        public static void SetActiveOrders(this ServerOrderControllerBase instance, List<ServerOrderData> value)
        {
            f_m_activeOrders.SetValue(instance, value);
        }

        public static RoundData GetRoundData(this ServerOrderControllerBase instance)
        {
            return (RoundData)f_m_roundData.GetValue(instance);
        }

        public static RoundInstanceDataBase GetRoundInstanceData(this ServerOrderControllerBase instance)
        {
            return (RoundInstanceDataBase)f_m_roundInstanceData.GetValue(instance);
        }

        public static void SetTimerUntilOrder(this ServerOrderControllerBase instance, float value)
        {
            f_m_timerUntilOrder.SetValue(instance, value);
        }

        public static void SetComboIndex(this ServerOrderControllerBase instance, int value)
        {
            f_m_comboIndex.SetValue(instance, value);
        }

        public static float GetNextTimeBetweenOrders(this ServerOrderControllerBase instance)
        {
            return (float)m_GetNextTimeBetweenOrders.Invoke(instance, null);
        }
    }

    public static class ClientOrderControllerBaseExt
    {
        private static readonly Type type = typeof(ClientOrderControllerBase);
        private static readonly FieldInfo f_m_activeOrders = AccessTools.Field(type, "m_activeOrders");
        private static readonly FieldInfo f_m_gui = AccessTools.Field(type, "m_gui");

        public static void SetActiveOrders(this ClientOrderControllerBase instance, List<ClientOrderControllerBase_ActiveOrderExt> value)
        {
            var list = f_m_activeOrders.GetValue(instance) as IList;
            list.Clear();
            foreach (var item in value)
            {
                list.Add(item.instance);
            }
        }

        public static RecipeFlowGUI GetGUI(this ClientOrderControllerBase instance)
        {
            return (RecipeFlowGUI)f_m_gui.GetValue(instance);
        }
    }

    public class ClientOrderControllerBase_ActiveOrderExt
    {
        private static readonly Type type = typeof(ClientOrderControllerBase).GetNestedType("ActiveOrder", BindingFlags.NonPublic);
        public readonly object instance;

        public ClientOrderControllerBase_ActiveOrderExt(OrderID id, RecipeList.Entry entry, RecipeFlowGUI.ElementToken token)
        {
            instance = Activator.CreateInstance(type, id, entry, token);
        }
    }
}
