using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch
{
    public class UnrealTimePatch
    {
        private static FieldInfo m_m_fDelta = typeof(ClientTime).GetField("m_fDelta", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo m_m_fLocalTimeLastFrame = typeof(ClientTime).GetField("m_fLocalTimeLastFrame", BindingFlags.Static | BindingFlags.NonPublic);

        public static void Update()
        {
            Time.captureFramerate = 60;
        }


        [HarmonyPatch(typeof(ClientTime), "OnTimeSyncReceived")]
        public static class OnTimeSyncReceivedPostfix
        {
            [HarmonyPrefix]
            public static bool Prefix() => false;
        }


        [HarmonyPatch(typeof(ClientTime), "Time")]
        public static class ClientTimePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref float __result)
            {
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
            public static bool Prefix() => false;
        }

        [HarmonyPatch(typeof(TimeManager), "Update")]
        public static class TimeManagerUpdatePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(TimeManager __instance)
            {
                Helpers.CurrentTimeManager = __instance;
                return false;
            }
        }
    }
}
