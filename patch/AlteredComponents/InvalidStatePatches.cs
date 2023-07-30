using HarmonyLib;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(ServerRespawnCollider), "ObjectAdded")]
    public static class PatchServerRespawnColliderObjectAdded
    {
        [HarmonyPrefix]
        public static bool Prefix(GameObject _gameObject, RespawnCollider ___m_RespawnCollider)
        {
            if (!StateInvalidityManager.PreventInvalidState)
            {
                return true;
            }
            if (___m_RespawnCollider == null)
            {
                return false;
            }
            if ((___m_RespawnCollider.m_respawnFilter.value & 1 << _gameObject.layer) != 0 && (!___m_RespawnCollider.m_onlyRespawnables || _gameObject.RequestInterfaceRecursive<IRespawnBehaviour>() != null))
            {
                StateInvalidityManager.InvalidReason = "Invalid Game State: RespawnCollider hit " + _gameObject.name;
                return false;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ServerKitchenFlowControllerBase), "HasFinished")]
    public static class PatchServerKitchenFlowControllerBaseHasFinished
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (!StateInvalidityManager.PreventInvalidState)
            {
                return;
            }
            if (__result)
            {
                StateInvalidityManager.InvalidReason = "Invalid Game State: Timer Expired";
            }
            __result = false;
        }
    }

    [HarmonyPatch(typeof(ServerFlammable), "Ignite")]
    public static class PatchServerFlammableIgnite
    {
        [HarmonyPrefix]
        public static bool Prefix(ServerFlammable __instance)
        {
            if (!StateInvalidityManager.PreventInvalidState)
            {
                return true;
            }
            StateInvalidityManager.InvalidReason = "Invalid Game State: On Fire: " + __instance.name;
            return false;
        }
    }
}
