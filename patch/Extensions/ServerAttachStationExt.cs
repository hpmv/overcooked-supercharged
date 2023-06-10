using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerAttachStationExt
    {
        private static Type type = typeof(ServerAttachStation);
        private static FieldInfo f_m_item = AccessTools.Field(type, "m_item");

        public static IAttachment m_item(this ServerAttachStation self)
        {
            return (IAttachment)f_m_item.GetValue(self);
        }
    }
}
