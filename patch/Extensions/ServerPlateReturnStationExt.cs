using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPlateReturnStationExt
    {
        private static readonly Type type = typeof(ServerPlateReturnStation);
        private static readonly FieldInfo f_m_stack = AccessTools.Field(type, "m_stack");

        public static void SetStack(this ServerPlateReturnStation self, ServerPlateStackBase stack)
        {
            f_m_stack.SetValue(self, stack);
        }
    }
}
