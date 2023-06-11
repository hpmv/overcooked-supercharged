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

        public static List<AssembledDefinitionNode> m_contents(this ServerIngredientContainer self)
        {
            return (List<AssembledDefinitionNode>)f_m_contents.GetValue(self);
        }
    }

    public static class ClientIngredientContainerExt
    {
        private static Type type = typeof(ClientIngredientContainer);
        private static FieldInfo f_m_contents = AccessTools.Field(type, "m_contents");

        public static List<AssembledDefinitionNode> m_contents(this ClientIngredientContainer self)
        {
            return (List<AssembledDefinitionNode>)f_m_contents.GetValue(self);
        }
    }
}
