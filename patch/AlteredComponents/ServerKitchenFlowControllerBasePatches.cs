using HarmonyLib;
using SuperchargedPatch.Extensions;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(ServerKitchenFlowControllerBase), "SendOrderAdded")]
    [HarmonyPatch(typeof(ServerKitchenFlowControllerBase), "SendDeliverySuccess")]
    public static class PatchServerKitchenFlowControllerBaseToSendAuxMessage
    {
        [HarmonyPostfix]
        public static void Postfix(ServerKitchenFlowControllerBase __instance)
        {
            var ordersController = __instance.GetMonitorForTeam(TeamID.One).OrdersController;
            var roundData = ordersController.GetRoundData() as WarpableRoundData;
            var roundInstanceData = ordersController.GetRoundInstanceData();
            if (roundData == null || roundInstanceData == null)
            {
                return;
            }
            __instance.SendAuxMessage(roundData.GetAuxMessage(roundInstanceData, ordersController.ActiveRecipes.Count));
        }
    }
}
