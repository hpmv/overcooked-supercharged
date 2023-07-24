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
        private static readonly FieldInfo f_m_roundTimer = AccessTools.Field(type, "m_roundTimer");

        public static PlateReturnController GetPlateReturnController(this ServerKitchenFlowControllerBase instance)
        {
            return (PlateReturnController)f_m_plateReturnController.GetValue(instance);
        }

        public static ServerKitchenFlowControllerBase FindController()
        {
            return GameObject.FindObjectOfType<ServerKitchenFlowControllerBase>();
        }

        public static IServerRoundTimer m_roundTimer(this ServerKitchenFlowControllerBase instance)
        {
            return (IServerRoundTimer)f_m_roundTimer.GetValue(instance);
        }
    }

    public static class ClientKitchenFlowControllerBaseExt
    {
        private static readonly Type type = typeof(ClientKitchenFlowControllerBase);
        private static readonly FieldInfo f_m_dataStore = AccessTools.Field(type, "m_dataStore");

        public static DataStore GetDataStore(this ClientKitchenFlowControllerBase instance)
        {
            return (DataStore)f_m_dataStore.GetValue(instance);
        }
    }
}
