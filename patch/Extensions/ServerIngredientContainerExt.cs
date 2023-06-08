using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class ServerIngredientContainerExt
    {
        private static Type type = typeof(ServerIngredientContainer);
        private static FieldInfo f_m_contents = AccessTools.Field(type, "m_contents");
        private static MethodInfo m_OnContentsChanged = AccessTools.Method(type, "OnContentsChanged");

        public static void SetContentsAndForceOnContentsChanged(this ServerIngredientContainer self, List<AssembledDefinitionNode> contents)
        {
            f_m_contents.SetValue(self, contents);
            m_OnContentsChanged.Invoke(self, null);
        }
    }
}
