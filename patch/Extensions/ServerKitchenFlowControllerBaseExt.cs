using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class ServerKitchenFlowControllerBaseExt
    {
        private static readonly Type type = typeof(ServerKitchenFlowControllerBase);
        private static readonly FieldInfo f_m_plateReturnController = AccessTools.Field(type, "m_plateReturnController");

        public static PlateReturnController GetPlateReturnController(this ServerKitchenFlowControllerBase instance)
        {
            return (PlateReturnController)f_m_plateReturnController.GetValue(instance);
        }

        public static ServerKitchenFlowControllerBase FindController()
        {
            return GameObject.FindObjectOfType<ServerKitchenFlowControllerBase>();
        }
    }
}
