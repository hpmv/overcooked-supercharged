using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPlayerAttachmentCarrierExt
    {
        private static Type type = typeof(ServerPlayerAttachmentCarrier);
        private static FieldInfo f_m_carriedObjects = AccessTools.Field(type, "m_carriedObjects");

        public static IAttachment[] m_carriedObjects(this ServerPlayerAttachmentCarrier self)
        {
            return (IAttachment[])f_m_carriedObjects.GetValue(self);
        }
    }
}
