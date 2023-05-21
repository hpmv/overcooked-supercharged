using HarmonyLib;
using System.Reflection;

namespace SuperchargedPatch
{
    public static class ServerWorkableItemExt
    {
        private static FieldInfo f_m_onWorkstation = AccessTools.Field(typeof(ServerWorkableItem), "m_onWorkstation");
        private static FieldInfo f_m_progress = AccessTools.Field(typeof(ServerWorkableItem), "m_progress");
        private static FieldInfo f_m_subProgress = AccessTools.Field(typeof(ServerWorkableItem), "m_subProgress");
        private static MethodInfo m_SynchroniseClientState = AccessTools.Method(typeof(ServerWorkableItem), "SynchroniseClientState");
        public static void SetOnWorkstation(this ServerWorkableItem swi, bool onWorkstation)
        {
            f_m_onWorkstation.SetValue(swi, onWorkstation);
        }

        public static void SetProgress(this ServerWorkableItem swi, int progress)
        {
            f_m_progress.SetValue(swi, progress);
        }

        public static void SetSubProgress(this ServerWorkableItem swi, int subProgress)
        {
            f_m_subProgress.SetValue(swi, subProgress);
        }

        public static void SynchroniseClientState(this ServerWorkableItem swi)
        {
            m_SynchroniseClientState.Invoke(swi, new object[] { });
        }
    }
}
