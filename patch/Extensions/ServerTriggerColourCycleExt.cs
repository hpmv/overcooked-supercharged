using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SuperchargedPatch.Extensions
{
    public static class ServerTriggerColourCycleExt
    {
        private static readonly Type type = typeof(ServerTriggerColourCycle);
        private static readonly FieldInfo f_m_currentColourIndex = AccessTools.Field(type, "m_currentColourIndex");
        
        public static void SetCurrentColourIndexAndSendServerEvent(this ServerTriggerColourCycle instance, int index)
        {
            f_m_currentColourIndex.SetValue(instance, index);
            instance.SendServerEvent(new TriggerColourCycleMessage
            {
                m_colourIndex = index
            });
        }
    }
}
