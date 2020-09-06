﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SuperchargedPatch
{
    public class UnrealTimePatch {
        private const bool ENABLE_TIME_PATCH = false;

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

        public static void Update() {
            if (!ENABLE_TIME_PATCH) return;
            Time.captureFramerate = 60;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClientTime), "OnTimeSyncReceived")]
        public static void OnTimeSyncReceivedPostfix() {
            if (!ENABLE_TIME_PATCH) return;
            m_m_fLastReceivedTime.SetValue(null, Time.time);
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClientTime), "Time")]
        public static bool ClientTimePatch(ref float __result) {
            if (!ENABLE_TIME_PATCH) return true;
            __result = (float)m_m_fLocalRunningTime.GetValue(null) + Mathf.Lerp((float)m_m_fOldOffset.GetValue(null),
                (float)m_m_fCurrentOffset.GetValue(null), (Time.time - (float)m_m_fLastReceivedTime.GetValue(null)) / 3f);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClientTime), "Update")]
        public static bool ClientTimeUpdatePatch() {
            if (!ENABLE_TIME_PATCH) return true;
            float _time = Time.time;
            m_m_fDelta.SetValue(null, _time - (float)m_m_fLocalTimeLastFrame.GetValue(null));
            m_m_fLocalRunningTime.SetValue(null, m_m_fDelta.GetValue(null));
            m_m_fLocalTimeLastFrame.SetValue(null, _time);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ServerTime), "Update")]
        public static bool ServerTimeUpdatePatch() {
            if (!ENABLE_TIME_PATCH) return true;
            m_m_fServerTime.SetValue(null, (float)m_m_fServerTime.GetValue(null) + Time.time - (float)m_m_fLastTime.GetValue(null));
            m_m_fLastTime.SetValue(null, m_m_fServerTime.GetValue(null));
            if ((float)m_m_fServerTime.GetValue(null) > (float)m_m_fNextSyncTime.GetValue(null)) {
                m_m_fNextSyncTime.SetValue(null, 3f + Time.time);
                f_TimeSync.Invoke(null, new object[] { m_m_fServerTime.GetValue(null) });
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TimeManager), "Update")]
        public static bool TimeManagerUpdatePatch() {
            if (!ENABLE_TIME_PATCH) return true;
            return false;
        }
    }
}
