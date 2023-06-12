using HarmonyLib;
using SuperchargedPatch.Extensions;
using System;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(PlateReturnController), "FoodDelivered")]
    public static class PatchPlateReturnControllerFoodDelivered
    {
        [HarmonyPostfix]
        public static void Postfix(object ___m_platesToReturn)
        {
            var list = new PlateReturnController_PlatesPendingReturnFastListExt(___m_platesToReturn);
            var lastPlate = list[list.Count - 1];
            var msg = new PlateReturnControllerAuxMessage();
            msg.InitializePlateDelivered(EntitySerialisationRegistry.GetEntry(lastPlate.m_station.gameObject).m_Header.m_uEntityID, lastPlate.m_timer);
            ServerKitchenFlowControllerBaseExt.FindController().SendAuxMessage(msg);
        }
    }

    [HarmonyPatch(typeof(PlateReturnController), "Update")]
    public static class PatchPlateReturnControllerUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix(object ___m_platesToReturn, PlateReturnController __instance)
        {
            var removed = new List<int>();
            var platesToReturn = new PlateReturnController_PlatesPendingReturnFastListExt(___m_platesToReturn);
            for (int i = platesToReturn.Count - 1; i >= 0; i--)
            {
                var plate = platesToReturn[i];
                plate.m_timer -= TimeManager.GetDeltaTime(LayerMask.NameToLayer("Default"));
                if (plate.m_timer < 0f)
                {
                    if (plate.m_station == null || plate.m_station.gameObject == null || !plate.m_station.gameObject.activeInHierarchy || !__instance.IsInLevelBounds(plate.m_station))
                    {
                        plate.m_station = __instance.FindBestReturnStation(plate.m_platingStepData);
                        Console.WriteLine("WARNING: Had to find a best return station");
                    }
                    else if (plate.m_station.CanReturnPlate())
                    {
                        plate.m_station.ReturnPlate();
                        platesToReturn.RemoveAt(i);
                        removed.Add(i);
                    }
                }
            }
            if (removed.Count != 0)
            {
                var msg = new PlateReturnControllerAuxMessage();
                msg.InitializeRemovePlates(removed);
                ServerKitchenFlowControllerBaseExt.FindController().SendAuxMessage(msg);
            }
            return false;
        }
    }
}
