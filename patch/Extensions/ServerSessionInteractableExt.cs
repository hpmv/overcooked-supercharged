using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class ServerSessionInteractableExt
    {
        private static Type type = typeof(ServerSessionInteractable);
        private static FieldInfo f_m_session = AccessTools.Field(type, "m_session");
        private static MethodInfo m_StartSession = AccessTools.Method(type, "StartSession");
        private static MethodInfo m_OnSessionEnded = AccessTools.Method(type, "OnSessionEnded");

        public static ServerSessionInteractable_SessionBaseExt GetSession(this ServerSessionInteractable self)
        {
            var session = f_m_session.GetValue(self);
            if (session == null)
            {
                return null;
            }
            return new ServerSessionInteractable_SessionBaseExt(session);
        }

        public static void StartSession(this ServerSessionInteractable self, GameObject interacter, Vector2 directionXZ)
        {
            m_StartSession.Invoke(self, new object[] {interacter, directionXZ});
        }

        public static void OnSessionEnded(this ServerSessionInteractable self)
        {
            m_OnSessionEnded.Invoke(self, null);
        }
    }

    public class ServerSessionInteractable_SessionBaseExt
    {
        private static Type type = typeof(ServerSessionInteractable).GetNestedType("SessionBase", BindingFlags.NonPublic);
        private static MethodInfo m_OnSessionEnded = AccessTools.Method(type, "OnSessionEnded");

        private object self;

        public ServerSessionInteractable_SessionBaseExt(object self)
        {
            this.self = self;
        }

        public void OnSessionEnded()
        {
            m_OnSessionEnded.Invoke(self, null);
        }
    }
}
