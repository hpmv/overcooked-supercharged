using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SuperchargedPatch
{
    public class UnrealTimePatch
    {
        private const bool ENABLE_TIME_PATCH = true;

        private static FieldInfo m_m_fDelta = typeof(ClientTime).GetField("m_fDelta", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fOldOffset = typeof(ClientTime).GetField("m_fOldOffset", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fCurrentOffset = typeof(ClientTime).GetField("m_fCurrentOffset", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fLocalRunningTime = typeof(ClientTime).GetField("m_fLocalRunningTime", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fLastReceivedTime = typeof(ClientTime).GetField("m_fLastReceivedTime", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fLocalTimeLastFrame = typeof(ClientTime).GetField("m_fLocalTimeLastFrame", BindingFlags.Static | BindingFlags.NonPublic);

        private static FieldInfo m_m_fServerTime = typeof(ServerTime).GetField("m_fServerTime", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fLastTime = typeof(ServerTime).GetField("m_fLastTime", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fNextSyncTime = typeof(ServerTime).GetField("m_fNextSyncTime", BindingFlags.Static | BindingFlags.NonPublic);

        private static Type t_ServerMessenger = typeof(ServerTime).Assembly.GetType("ServerMessenger");
        private static MethodInfo f_TimeSync = t_ServerMessenger.GetMethod("TimeSync", BindingFlags.Static | BindingFlags.Public);

        public static void Update()
        {
            if (!ENABLE_TIME_PATCH) return;
            Time.captureFramerate = 60;
        }


        [HarmonyPatch(typeof(ClientTime), "OnTimeSyncReceived")]
        public static class OnTimeSyncReceivedPostfix
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                return !ENABLE_TIME_PATCH;
            }
        }


        [HarmonyPatch(typeof(ClientTime), "Time")]
        public static class ClientTimePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref float __result)
            {
                if (!ENABLE_TIME_PATCH) return true;
                __result = Time.time;
                return false;
            }
        }

        [HarmonyPatch(typeof(ClientTime), "Update")]
        public static class ClientTimeUpdatePatch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (!ENABLE_TIME_PATCH) return true;
                float _time = Time.time;
                m_m_fDelta.SetValue(null, _time - (float)m_m_fLocalTimeLastFrame.GetValue(null));
                m_m_fLocalTimeLastFrame.SetValue(null, _time);
                return false;
            }
        }

        [HarmonyPatch(typeof(ServerTime), "Update")]
        public static class ServerTimeUpdatePatch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (!ENABLE_TIME_PATCH) return true;
                return false;
            }
        }

        [HarmonyPatch(typeof(TimeManager), "Update")]
        public static class TimeManagerUpdatePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(TimeManager __instance)
            {
                Helpers.CurrentTimeManager = __instance;
                if (!ENABLE_TIME_PATCH) return true;
                return false;
            }
        }
    }
}
