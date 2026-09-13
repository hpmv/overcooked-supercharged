using HarmonyLib;
using System;
using System.Collections.Generic;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(ClientPlateStation), "DeliverPlate")]
    public static class PatchClientPlateStationDeliverPlate {
        [HarmonyPostfix]
        public static void Postfix(ClientPlate _plate)
        {
            NativePlateLifecycle.ObserveServed(_plate.gameObject);
        }
    }

    // Native delivery/fade/destruction and registration lifetimes run unchanged.
    // Logical unavailability and physical retirement are separate observations.
    public static class NativePlateLifecycle
    {
        private static readonly Dictionary<GameObject, int> delivered = new Dictionary<GameObject, int>();
        public static string LastObservationError = "";
        public static int PendingDeliveryFades { get { return delivered.Count; } }
        internal static void RegistryCleared()
        {
            // Native Clear bypasses RemoveEntry. This resets only our old-registry
            // observation; it does not manufacture per-entity retirement events.
            delivered.Clear(); LastObservationError="";
        }
        public static void ObserveServed(GameObject plate)
        {
            var entry=EntitySerialisationRegistry.GetEntry(plate);
            if(entry==null) return;
            int instance=plate.GetInstanceID();
            if(delivered.ContainsKey(plate)) return;
            delivered.Add(plate,instance);
            AuxMessageSender.SendAuxMessage(entry.m_Header.m_uEntityID,new PlateLifecycleAuxMessage { Phase=1, InstanceBits=unchecked((uint)instance) });
        }
        internal sealed class Removal
        {
            internal GameObject Object;
            internal uint Id;
            internal int Instance;
        }
        internal static Removal BeforeRemoval(EntitySerialisationEntry entry)
        {
            if(entry==null || ReferenceEquals(entry.m_GameObject,null)) return null;
            int instance;
            if(!delivered.TryGetValue(entry.m_GameObject,out instance)) return null;
            return new Removal { Object=entry.m_GameObject,Id=entry.m_Header.m_uEntityID,Instance=instance };
        }
        internal static void AfterRemoval(Removal removal)
        {
            if(removal==null) return;
            var successor=EntitySerialisationRegistry.GetEntry(removal.Id);
            if(successor!=null && ReferenceEquals(successor.m_GameObject,removal.Object)) return;
            delivered.Remove(removal.Object);
            if(successor!=null) {
                // A callback reused this ID before RemoveEntry returned. Never
                // send an ID-only retirement that could remove its successor.
                LastObservationError="Served plate ID "+removal.Id+" was reused during native removal; retirement receipt was not emitted.";
                return;
            }
            AuxMessageSender.SendAuxMessage(removal.Id,new PlateLifecycleAuxMessage { Phase=2,InstanceBits=unchecked((uint)removal.Instance) });
            // Preserve the existing controller retirement event, now at the actual
            // native removal boundary rather than at the beginning of its fade.
            EntityRetirementMessageSender.SendMessageToRetireEntity(removal.Id);
        }
    }

    [HarmonyPatch(typeof(EntitySerialisationRegistry), "Clear")]
    internal static class ObserveNativeRegistryClear
    {
        private static void Postfix() { NativePlateLifecycle.RegistryCleared(); }
    }

    [HarmonyPatch(typeof(EntitySerialisationRegistry), "RemoveEntry")]
    internal static class ObserveNativePlateRemoval
    {
        private static void Prefix(EntitySerialisationEntry entry,out NativePlateLifecycle.Removal __state) { __state=NativePlateLifecycle.BeforeRemoval(entry); }
        private static void Postfix(NativePlateLifecycle.Removal __state) { NativePlateLifecycle.AfterRemoval(__state); }
    }

    public sealed class PlateLifecycleAuxMessage : AuxMessageBase
    {
        public uint Phase,InstanceBits;
        public override AuxEntityType GetAuxEntityType() { return AuxEntityType.PlateLifecycleAux; }
        public override void Serialise(BitStreamWriter writer) { writer.Write(1u,32);writer.Write(Phase,32);writer.Write(InstanceBits,32); }
    }
}
