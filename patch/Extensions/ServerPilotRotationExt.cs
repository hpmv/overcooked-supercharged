using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPilotRotationExt
    {
        private static Type type = typeof(ServerPilotRotation);
        private static FieldInfo f_m_angle = AccessTools.Field(type, "m_angle");
        private static FieldInfo f_m_startAngle = AccessTools.Field(type, "m_startAngle");
        private static FieldInfo f_m_message = AccessTools.Field(type, "m_message");
        public static void SetAngle(this ServerPilotRotation self, float angle)
        {
            f_m_angle.SetValue(self, angle);
        }

        public static float m_angle(this ServerPilotRotation self)
        {
            return (float)f_m_angle.GetValue(self);
        }

        public static float m_startAngle(this ServerPilotRotation self)
        {
            return (float)f_m_startAngle.GetValue(self);
        }

        public static PilotRotationMessage m_message(this ServerPilotRotation self)
        {
            return (PilotRotationMessage)f_m_message.GetValue(self);
        }
    }

    public static class ClientPilotRotationExt
    {
        private static Type type = typeof(ClientPilotRotation);
        private static FieldInfo f_m_nextRotation = AccessTools.Field(type, "m_nextRotation");

        public static void SetNextRotation(this ClientPilotRotation self, UnityEngine.Quaternion nextRotation)
        {
            f_m_nextRotation.SetValue(self, nextRotation);
        }
    }
}
