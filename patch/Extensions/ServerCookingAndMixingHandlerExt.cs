using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerCookingHandlerExt
    {
        private static Type type = typeof(ServerCookingHandler);
        private static FieldInfo m_ServerData = AccessTools.Field(type, "m_ServerData");
        private static FieldInfo m_cookingStateChangedCallback = AccessTools.Field(type, "m_cookingStateChangedCallback");
        
        public static void SetCookingProgressAndForceStateChangedCallback(this ServerCookingHandler self, float progress)
        {
            self.SetCookingProgress(progress);
            var serverData = (CookingStateMessage)m_ServerData.GetValue(self);
            ((Delegate)m_cookingStateChangedCallback.GetValue(self)).DynamicInvoke(serverData.m_cookingState);
        }
    }

    public static class ServerMixingHandlerExt
    {
        private static Type type = typeof(ServerMixingHandler);
        private static FieldInfo m_serverData = AccessTools.Field(type, "m_serverData");
        private static FieldInfo m_stateChangedCallback = AccessTools.Field(type, "m_stateChangedCallback");

        public static void SetMixingProgressAndForceStateChangedCallback(this ServerMixingHandler self, float progress)
        {
            self.SetMixingProgress(progress);
            var serverData = (MixingStateMessage)m_serverData.GetValue(self);
            ((Delegate)m_stateChangedCallback.GetValue(self)).DynamicInvoke(serverData.m_mixingState);
        }
    }
}
