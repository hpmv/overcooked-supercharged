using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerTeamMonitorExt
    {
        private static Type type = typeof(ServerTeamMonitor);
        private static FieldInfo f_m_score = AccessTools.Field(type, "m_score");

        public static void SetScore(this ServerTeamMonitor instance, TeamMonitor.TeamScoreStats score)
        {
            f_m_score.SetValue(instance, score);
        }
    }

    public static class ClientTeamMonitorExt
    {
        private static Type type = typeof(ClientTeamMonitor);
        private static FieldInfo f_m_score = AccessTools.Field(type, "m_score");

        public static void SetScore(this ClientTeamMonitor instance, TeamMonitor.TeamScoreStats score)
        {
            f_m_score.SetValue(instance, score);
        }
    }
}
