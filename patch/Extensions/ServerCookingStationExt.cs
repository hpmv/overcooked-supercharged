using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerCookingStationExt
    {
        private static readonly Type type = typeof(ServerCookingStation);
        private static readonly FieldInfo f_m_isTurnedOn = AccessTools.Field(type, "m_isTurnedOn");
        private static readonly FieldInfo f_m_isCooking = AccessTools.Field(type, "m_isCooking");

        public static void SetIsTurnedOn(this ServerCookingStation station, bool value)
        {
            f_m_isTurnedOn.SetValue(station, value);
        }

        public static void SetIsCooking(this ServerCookingStation station, bool value)
        {
            f_m_isCooking.SetValue(station, value);
        }
    }
}
