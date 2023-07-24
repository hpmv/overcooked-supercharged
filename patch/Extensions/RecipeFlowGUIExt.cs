using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class RecipeFlowGUIExt
    {
        private static readonly Type type = typeof(RecipeFlowGUI);
        private static readonly FieldInfo f_m_recipeWidgetPrefab = AccessTools.Field(type, "m_recipeWidgetPrefab");
        private static readonly FieldInfo f_m_nextIndex = AccessTools.Field(type, "m_nextIndex");
        private static readonly FieldInfo f_m_widgets = AccessTools.Field(type, "m_widgets");
        private static readonly FieldInfo f_m_dyingWidgets = AccessTools.Field(type, "m_dyingWidgets");
        private static readonly FieldInfo f_m_occupiedTables = AccessTools.Field(type, "m_occupiedTables");
        private static readonly MethodInfo m_LayoutWidgets = AccessTools.Method(type, "LayoutWidgets");

        public static RecipeWidgetUIController GetRecipeWidgetPrefab(this RecipeFlowGUI instance)
        {
            return (RecipeWidgetUIController)f_m_recipeWidgetPrefab.GetValue(instance);
        }

        public static int GetNextIndex(this RecipeFlowGUI instance)
        {
            return (int)f_m_nextIndex.GetValue(instance);
        }

        public static void SetNextIndex(this RecipeFlowGUI instance, int value)
        {
            f_m_nextIndex.SetValue(instance, value);
        }

        public static List<RecipeFlowGUI.RecipeWidgetData> GetActiveWidgets(this RecipeFlowGUI instance)
        {
            return (List<RecipeFlowGUI.RecipeWidgetData>)f_m_widgets.GetValue(instance);
        }

        public static List<RecipeFlowGUI.RecipeWidgetData> GetDyingWidgets(this RecipeFlowGUI instance)
        {
            return (List<RecipeFlowGUI.RecipeWidgetData>)f_m_dyingWidgets.GetValue(instance);
        }

        public static bool[] GetOccupiedTables(this RecipeFlowGUI instance)
        {
            return (bool[])f_m_occupiedTables.GetValue(instance);
        }

        public static void LayoutWidgets(this RecipeFlowGUI instance)
        {
            m_LayoutWidgets.Invoke(instance, null);
        }
    }

    public static class RecipeWidgetUIControllerExt
    {
        private static readonly Type type = typeof(RecipeWidgetUIController);
        private static readonly FieldInfo f_m_recipeTree = AccessTools.Field(type, "m_recipeTree");
        private static readonly FieldInfo f_m_tableNumber = AccessTools.Field(type, "m_tableNumber");
        private static readonly FieldInfo f_m_widgetAnimator = AccessTools.Field(type, "m_widgetAnimator");
        private static readonly FieldInfo f_m_topWidgetTile = AccessTools.Field(type, "m_topWidgetTile");

        public static void SetRecipeTree(this RecipeWidgetUIController instance, RecipeWidgetUIController.RecipeTileData[] value)
        {
            f_m_recipeTree.SetValue(instance, value);
        }

        public static void SetTableNumber(this RecipeWidgetUIController instance, int value)
        {
            f_m_tableNumber.SetValue(instance, value);
        }

        public static Animator GetAnimator(this RecipeWidgetUIController instance)
        {
            return (Animator)f_m_widgetAnimator.GetValue(instance);
        }

        public static TopRecipeWidgetTile GetTopWidgetTile(this RecipeWidgetUIController instance)
        {
            return (TopRecipeWidgetTile)f_m_topWidgetTile.GetValue(instance);
        }
    }
}
