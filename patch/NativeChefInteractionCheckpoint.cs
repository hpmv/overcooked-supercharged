using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    public static class NativeChefInteractionCheckpoint
    {
        private static readonly FieldInfo pickup = AccessTools.Field(typeof(ClientPlayerControlsImpl_Default), "m_lastPickupTimestamp");
        private static readonly FieldInfo suppression = AccessTools.Field(typeof(PlayerControls.ControlSchemeData), "m_supressUse");
        public sealed class Snapshot
        {
            internal ClientPlayerControlsImpl_Default Chef;
            internal PlayerControls Controls;
            internal PlayerControls.ControlSchemeData Scheme;
            internal int Id, Instance;
            internal float PickupTimestamp;
            internal bool UseSuppressed;
        }
        private static void ValidateFields()
        {
            if(pickup==null || pickup.FieldType!=typeof(float) || pickup.IsStatic
                || suppression==null || suppression.FieldType!=typeof(bool) || suppression.IsStatic)
                throw new InvalidOperationException("Installed native pickup timestamp/use-suppression fields do not match the checkpoint schema.");
        }
        public static Snapshot[] CaptureAll()
        {
            ValidateFields();
            return NativeCheckpointComponentBatch.Find<ClientPlayerControlsImpl_Default>().Select(chef=> {
                var controls=chef.GetComponent<PlayerControls>();
                var entry=EntitySerialisationRegistry.GetEntry(chef.gameObject);
                if(controls==null || controls.ControlScheme==null || entry==null)
                    throw new InvalidOperationException("Native chef interaction checkpoint is not initialized.");
                float time=(float)pickup.GetValue(chef);
                if(float.IsNaN(time)||float.IsInfinity(time)) throw new InvalidOperationException("Native pickup timestamp is not finite.");
                return new Snapshot { Chef=chef,Controls=controls,Scheme=controls.ControlScheme,
                    Id=(int)entry.m_Header.m_uEntityID,Instance=chef.gameObject.GetInstanceID(),PickupTimestamp=time,
                    UseSuppressed=(bool)suppression.GetValue(controls.ControlScheme) };
            }).OrderBy(s=>s.Id).ToArray();
        }
        public static void Validate(Snapshot[] saved)
        {
            ValidateFields();
            var current=CaptureAll();
            if(current.Length!=saved.Length) throw new InvalidOperationException("Native chef interaction membership changed.");
            for(int i=0;i<saved.Length;i++)
                if(current[i].Id!=saved[i].Id || current[i].Instance!=saved[i].Instance || current[i].Chef!=saved[i].Chef
                    || current[i].Controls!=saved[i].Controls || !ReferenceEquals(current[i].Scheme,saved[i].Scheme))
                    throw new InvalidOperationException("Native chef interaction checkpoint incarnation changed.");
        }
        public static void Restore(Snapshot[] saved)
        {
            Validate(saved);
            foreach(var s in saved) {
                // This is a ClientTime.Time timestamp, so restore its exact native
                // value alongside ClientTime; do not rebase it against Unity time.
                pickup.SetValue(s.Chef,s.PickupTimestamp);
                s.Scheme.SetUseSuppressed(s.UseSuppressed);
            }
        }
        public static bool SameBoundary(Snapshot[] a,Snapshot[] b)
        {
            if(a.Length!=b.Length) return false;
            for(int i=0;i<a.Length;i++) if(a[i].Chef!=b[i].Chef || a[i].PickupTimestamp!=b[i].PickupTimestamp || a[i].UseSuppressed!=b[i].UseSuppressed) return false;
            return true;
        }
        public static void VerifyRestored(Snapshot[] saved)
        {
            Validate(saved);
            if(!SameBoundary(saved,CaptureAll())) throw new InvalidOperationException("Native pickup cooldown/use suppression did not restore exactly.");
        }
    }
}
