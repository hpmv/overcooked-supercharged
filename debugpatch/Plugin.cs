using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DebugPatch
{
    [BepInEx.BepInPlugin("hpmv.DebugLoader", "DebugLoader", "1.0.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static Harmony patcher;

        public void Update()
        {
        }

        public void Awake()
        {
            Debug.Log("DebugPlugin Awaken");
            patcher = new Harmony("hpmv.DebugLoader");
            patcher.PatchAll(Assembly.GetExecutingAssembly());
            DebugLogGUI.Log("DebugPlugin Loaded");
        }

        public void OnEnable()
        {
            Debug.Log("DebugPlugin Enabled");
        }

        public void OnDisable()
        {
            Debug.Log("DebugPlugin Disabled");
        }

        public void LateUpdate()
        {
        }

        public void OnDestroy()
        {
            Harmony.UnpatchAll();
            Debug.Log("DebugPlugin Destroyed");
        }

        public void OnGUI()
        {
            PatchThrowable.OnGUI();
            DebugLogGUI.OnGUI();
        }
    }

    [HarmonyPatch(typeof(ServerThrowableItem), "UpdateSynchronising")]
    public static class PatchThrowable
    {
        private static Dictionary<ServerThrowableItem, bool> states = new Dictionary<ServerThrowableItem, bool>();
        private static GUIStyle style;
        public static void Postfix(ServerThrowableItem __instance, bool ___m_flying)
        {
            states[__instance] = ___m_flying;
        }

        public static void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle();
                style.normal.textColor = Color.white;
                style.fontSize = 40;
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.UpperLeft;
            }

            var toRemove = new List<ServerThrowableItem>();
            foreach (var pair in states)
            {
                var item = pair.Key;
                var flying = pair.Value;
                if (item == null || item.gameObject == null || item.gameObject.transform == null)
                {
                    toRemove.Add(item);
                    continue;
                }
                var pos = item.gameObject.transform.position;
                var screenPos = Camera.main.WorldToScreenPoint(pos);
                var text = flying ? "F" : "-";
                var size = style.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(screenPos.x - size.x / 2, Screen.height - screenPos.y, size.x, size.y), text, style);
            }

            foreach (var item in toRemove)
            {
                states.Remove(item);
            }
        }
    }

    [HarmonyPatch(typeof(ServerThrowableItem), "OnCollisionEnter")]
    public static class PatchThrowable2
    {
        public static bool Prefix(ServerThrowableItem __instance, Collision _collision, IThrower ___m_thrower, GenericVoid<GameObject> ___m_landedCallback)
        {
            if (!__instance.IsFlying())
            {
                return false;
            }
            IThrower thrower = _collision.gameObject.RequestInterface<IThrower>();
            if (thrower != null && thrower == ___m_thrower)
            {
                return false;
            }
            Vector3 normal = _collision.contacts[0].normal;
            float num = Vector3.Angle(normal, Vector3.up);
            DebugLogGUI.Log($"Throwable {__instance.name} collided with {_collision.gameObject.name} at {num} degrees; landed callback is {___m_landedCallback}");
            if (num <= 45f)
            {
                __instance.EndFlight();
                ___m_landedCallback(_collision.gameObject);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ServerSplattable), "OnThrowableLanded")]
    public static class PatchSplattable
    {
        public static void Prefix(ServerSplattable __instance)
        {
            DebugLogGUI.Log($"Splattable {__instance.name} landed");
        }
    }

    static class DebugLogGUI
    {
        private static GUIStyle style;
        private static List<string> logs = new List<string>();
        private static int maxLogs = 10;

        public static void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle();
                style.normal.textColor = Color.white;
                style.fontSize = 20;
                style.alignment = TextAnchor.UpperLeft;
            }

            var y = 0;

            foreach (var log in logs)
            {
                GUI.Label(new Rect(0, y, Screen.width, 20), log, style);
                y += 20;
            }
        }

        public static void Log(string log)
        {
            logs.Add(log);

            if (logs.Count > maxLogs)
            {
                logs.RemoveRange(0, logs.Count - maxLogs);
            }
        }
    }
}
