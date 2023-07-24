using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class ServerRoundTimerExt
    {
        private static Type type = typeof(ServerRoundTimer);
        private static FieldInfo f_m_timeLimit = AccessTools.Field(type, "m_timeLimit");
        private static FieldInfo f_m_roundTimer = AccessTools.Field(type, "m_roundTimer");
        private static FieldInfo f_m_timeLeft = AccessTools.Field(type, "m_timeLeft");

        public static float GetTimeLimit(this ServerRoundTimer instance)
        {
            return (float)f_m_timeLimit.GetValue(instance);
        }

        public static void SetRoundTimer(this ServerRoundTimer instance, float time)
        {
            f_m_roundTimer.SetValue(instance, time);
            f_m_timeLeft.SetValue(instance, Mathf.CeilToInt(Mathf.Max(instance.GetTimeLimit() - time, 0f)));
        }
    }

    public static class ClientRoundTimerExt
    {
        private static Type type = typeof(ClientRoundTimer);
        private static FieldInfo f_m_timeLimit = AccessTools.Field(type, "m_timeLimit");
        private static FieldInfo f_m_roundTimer = AccessTools.Field(type, "m_roundTimer");
        private static FieldInfo f_m_timeLeft = AccessTools.Field(type, "m_timeLeft");
        private static FieldInfo f_m_dataStore = AccessTools.Field(type, "m_dataStore");
        private static FieldInfo sf_k_timeUpdatedId = AccessTools.Field(type, "k_timeUpdatedId");

        public static float GetTimeLimit(this ClientRoundTimer instance)
        {
            return (float)f_m_timeLimit.GetValue(instance);
        }

        public static void SetRoundTimer(this ClientRoundTimer instance, float time)
        {
            f_m_roundTimer.SetValue(instance, time);
            var timeLeft = Mathf.CeilToInt(Mathf.Max(instance.GetTimeLimit() - time, 0f));
            f_m_timeLeft.SetValue(instance, timeLeft);
            var dataStore = f_m_dataStore.GetValue(instance) as DataStore;
            dataStore.Write((DataStore.Id)sf_k_timeUpdatedId.GetValue(null), (float)timeLeft);
        }
    }
}
