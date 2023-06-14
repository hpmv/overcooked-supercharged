using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SuperchargedPatch.Extensions
{
    public static class ServerWashingStationExt
    {
        private static readonly Type type = typeof(ServerWashingStation);
        private static readonly FieldInfo f_m_plateCount = AccessTools.Field(type, "m_plateCount");
        private static readonly FieldInfo f_m_cleaningTimer = AccessTools.Field(type, "m_cleaningTimer");

        public static void SetPlateCount(this ServerWashingStation instance, int count)
        {
            f_m_plateCount.SetValue(instance, count);
        }

        public static void SetCleaningTimer(this ServerWashingStation instance, float timer)
        {
            f_m_cleaningTimer.SetValue(instance, timer);
        }
    }

    public static class ClientWashingStationExt
    {
        private static readonly Type type = typeof(ClientWashingStation);
        private static readonly FieldInfo f_m_plateCount = AccessTools.Field(type, "m_plateCount");
        private static readonly FieldInfo f_m_cleaningTimer = AccessTools.Field(type, "m_cleaningTimer");
        private static readonly FieldInfo f_m_progressUI = AccessTools.Field(type, "m_progressUI");
        private static readonly FieldInfo f_m_isWashing = AccessTools.Field(type, "m_isWashing");
        private static readonly MethodInfo m_UpdateCosmetics = AccessTools.Method(type, "UpdateCosmetics");

        public static void SetPlateCount(this ClientWashingStation instance, int count)
        {
            f_m_plateCount.SetValue(instance, count);
        }

        public static void SetCleaningTimer(this ClientWashingStation instance, float timer)
        {
            f_m_cleaningTimer.SetValue(instance, timer);
        }

        public static ProgressUIController m_progressUI(this ClientWashingStation instance)
        {
            return (ProgressUIController)f_m_progressUI.GetValue(instance);
        }

        public static void SetIsWashing(this ClientWashingStation instance, bool isWashing)
        {
            f_m_isWashing.SetValue(instance, isWashing);
        }

        public static void UpdateCosmetics(this ClientWashingStation instance)
        {
            m_UpdateCosmetics.Invoke(instance, null);
        }
    }
}
