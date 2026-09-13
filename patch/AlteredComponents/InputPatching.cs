using System;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    internal static class NativeInputOwner
    {
        internal static GameObject Find(PlayerInputLookup.Player player, out int entityId)
        {
            GameObject result = null;
            entityId = -1;
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                if (entry.m_GameObject == null) continue;
                var provider = entry.m_GameObject.GetComponent<PlayerIDProvider>();
                if (provider == null || provider.GetID() != player) continue;
                if (result != null && result != entry.m_GameObject)
                    throw new InvalidOperationException("Ambiguous native chef ownership for input player " + player);
                result = entry.m_GameObject;
                entityId = (int)entry.m_Header.m_uEntityID;
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(PlayerInputLookup), "GetButton")]
    public static class PatchPlayerInputLookupGetButton
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInputLookup.LogicalButtonID _id, PlayerInputLookup.Player _player, ref ILogicalButton __result)
        {
            TASLogicalButtonType type;
            switch (_id)
            {
                case PlayerInputLookup.LogicalButtonID.PickupAndDrop: type = TASLogicalButtonType.Pickup; break;
                case PlayerInputLookup.LogicalButtonID.WorkstationInteract: type = TASLogicalButtonType.Use; break;
                case PlayerInputLookup.LogicalButtonID.Dash: type = TASLogicalButtonType.Dash; break;
                default: return;
            }
            int entityId;
            var owner = NativeInputOwner.Find(_player, out entityId);
            if (owner != null) __result = TASLogicalButton.GetOrCreate(entityId, type, __result, owner);
        }
    }

    [HarmonyPatch(typeof(PlayerInputLookup), "GetValue")]
    public static class PatchPlayerInputLookupGetValue
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerInputLookup.LogicalValueID _id, PlayerInputLookup.Player _player, ref ILogicalValue __result)
        {
            TASLogicalValueType type;
            switch (_id)
            {
                case PlayerInputLookup.LogicalValueID.MovementX: type = TASLogicalValueType.MovementX; break;
                case PlayerInputLookup.LogicalValueID.MovementY: type = TASLogicalValueType.MovementY; break;
                default: return;
            }
            int entityId;
            var owner = NativeInputOwner.Find(_player, out entityId);
            if (owner != null) __result = new TASLogicalValue(entityId, type, __result, owner);
        }
    }

    // Observe only. Native focus, menus, direct-control gates and claims execute.
    [HarmonyPatch(typeof(ClientInputTransmitter), "GetGated", new Type[] { typeof(ILogicalButton) })]
    public static class ObserveClientNativeInputGate
    {
        [HarmonyPostfix]
        public static void Postfix(ILogicalButton _toProtect, ILogicalButton __result) { TASLogicalButton.ObserveNativeGate(_toProtect, __result); }
    }

    [HarmonyPatch(typeof(PlayerControls.ControlSchemeData), "GetGated", new Type[] { typeof(ILogicalButton) })]
    public static class ObserveControlsNativeInputGate
    {
        [HarmonyPostfix]
        public static void Postfix(ILogicalButton _toProtect, ILogicalButton __result) { TASLogicalButton.ObserveNativeGate(_toProtect, __result); }
    }
}

