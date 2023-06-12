using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(ClientPlateStation), "DeliverPlate")]
    public static class PatchClientPlateStationDeliverPlate {
        [HarmonyPostfix]
        public static void Postfix(ClientPlate _plate)
        {
            _plate.GetComponent<ServerPlate>().SendMessageToRetireEntity();
            EntitySerialisationRegistry.UnregisterObject(_plate.gameObject);
        }
    }
}
