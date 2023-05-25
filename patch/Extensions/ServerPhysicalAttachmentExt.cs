using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPhysicalAttachmentExt
    {
        private static Type type = typeof(ServerPhysicalAttachment);
        private static FieldInfo f_m_ServerData = AccessTools.Field(type, "m_ServerData");

        public static PhysicalAttachMessage m_ServerData(this ServerPhysicalAttachment self)
        {
            return f_m_ServerData.GetValue(self) as PhysicalAttachMessage;
        }
    }
}
