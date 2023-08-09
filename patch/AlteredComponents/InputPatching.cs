using HarmonyLib;
using System;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{

    [HarmonyPatch(typeof(PlayerInputLookup), "GetButton")]
    public static class PatchPlayerInputLookupGetButton
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInputLookup.LogicalButtonID _id, PlayerInputLookup.Player _player, ref ILogicalButton __result)
        {
            int? foundEntityId = null;
            EntitySerialisationRegistry.m_EntitiesList.ForEach(entry =>
            {
                if (entry.m_GameObject.GetComponent<PlayerIDProvider>() is PlayerIDProvider pip)
                {
                    if (pip.GetID() == _player)
                    {
                        foundEntityId = (int)entry.m_Header.m_uEntityID;
                    }
                }
            });
            if (foundEntityId == null)
            {
                Debug.Log($"Unable to find entity ID for player input ID {_player}");
                return;
            }

            TASLogicalButtonType buttonType;
            switch (_id)
            {
                case PlayerInputLookup.LogicalButtonID.PickupAndDrop:
                    buttonType = TASLogicalButtonType.Pickup;
                    break;
                case PlayerInputLookup.LogicalButtonID.WorkstationInteract:
                    buttonType = TASLogicalButtonType.Use;
                    break;
                case PlayerInputLookup.LogicalButtonID.Dash:
                    buttonType = TASLogicalButtonType.Dash;
                    break;
                default:
                    return;
            }
            // Debug.Log($"Overriding button {_id} for player {_player} with entity ID {foundEntityId.Value}");
            __result = new TASLogicalButton(foundEntityId.Value, buttonType, __result);
        }
    }

    [HarmonyPatch(typeof(PlayerInputLookup), "GetValue")]
    public static class PatchPlayerInputLookupGetValue
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInputLookup.LogicalValueID _id, PlayerInputLookup.Player _player, ref ILogicalValue __result)
        {
            int? foundEntityId = null;
            EntitySerialisationRegistry.m_EntitiesList.ForEach(entry =>
            {
                if (entry.m_GameObject.GetComponent<PlayerIDProvider>() is PlayerIDProvider pip)
                {
                    if (pip.GetID() == _player)
                    {
                        foundEntityId = (int)entry.m_Header.m_uEntityID;
                    }
                }
            });
            if (foundEntityId == null)
            {
                Debug.Log($"Unable to find entity ID for player input ID {_player}");
                return;
            }

            TASLogicalValueType valueType;
            switch (_id)
            {
                case PlayerInputLookup.LogicalValueID.MovementX:
                    valueType = TASLogicalValueType.MovementX;
                    break;
                case PlayerInputLookup.LogicalValueID.MovementY:
                    valueType = TASLogicalValueType.MovementY;
                    break;
                default:
                    return;
            }
            // Debug.Log($"Overriding value {_id} for player {_player} with entity ID {foundEntityId.Value}");
            __result = new TASLogicalValue(foundEntityId.Value, valueType, __result);
            return;
        }
    }

    [HarmonyPatch(typeof(PlayerControls), "CanButtonBePressed")]
    public static class PatchPlayerControlsCanButtonBePressed
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerControls __instance, ref bool __result)
        {
            // The original method queries a lot of stuff that isn't useful for TAS.
            // GetDirectlyUnderPlayerControl() is the only thing that is related to
            // game logic.
            __result = __instance.GetDirectlyUnderPlayerControl();
            return false;
        }
    }

    [HarmonyPatch(typeof(ClientInputTransmitter), "GetGated", new Type[] { typeof(ILogicalButton) })]
    public static class PatchClientInputTransmitterGetGatedButton
    {
        [HarmonyPrefix]
        public static bool Prefix(ILogicalButton _toProtect, ref ILogicalButton __result)
        {
            // TODO: revisit this. We're skipping CanButtonBePressed here.
            __result = _toProtect;
            return false;
        }
    }

    [HarmonyPatch(typeof(ClientInputTransmitter), "GetGated", new Type[] { typeof(ILogicalValue) })]
    public static class PatchClientInputTransmitterGetGatedValue
    {
        [HarmonyPrefix]
        public static bool Prefix(ILogicalValue _toProtect, ref ILogicalValue __result)
        {
            // TODO: revisit this. We're skipping CanButtonBePressed here.
            __result = _toProtect;
            return false;
        }
    }
}
