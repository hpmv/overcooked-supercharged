using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Composition callbacks during authoring can queue a redundant native event.
    // Preserve the exact observed queue flag and payload, including a real
    // pending event, rather than clearing the flag unconditionally.
    public static class NativeStationSyncCheckpoint
    {
        public sealed class State
        {
            internal ServerSynchroniserBase Component;
            internal Type Type,MessageType;
            internal int Id;
            internal bool Pending,On,Cooking;
            internal byte[] Payload;
        }
        private static object Read(State s,string name) {return AccessTools.Field(s.Type,name).GetValue(s.Component);}
        private static void Write(State s,string name,object value) {AccessTools.Field(s.Type,name).SetValue(s.Component,value);}
        public static State[] Capture()
        {
            var components=new List<ServerSynchroniserBase>();
            components.AddRange(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Cast<ServerSynchroniserBase>());
            components.AddRange(NativeCheckpointComponentBatch.Find<ServerMixingStation>().Cast<ServerSynchroniserBase>());
            return components.Select(c=> {
                var s=new State {Component=c,Type=c is ServerCookingStation?typeof(ServerCookingStation):typeof(ServerMixingStation),
                    Id=(int)EntitySerialisationRegistry.GetId(c.gameObject)};
                s.Pending=(bool)Read(s,"m_pendingSyncMessage");
                s.On=(bool)Read(s,s.Type==typeof(ServerCookingStation)?"m_isTurnedOn":"m_bMixerOn");
                s.Cooking=s.Type==typeof(ServerCookingStation)&&(bool)Read(s,"m_isCooking");
                var message=(Serialisable)Read(s,"m_data");s.MessageType=message.GetType();s.Payload=message.ToBytes();
                return s;
            }).OrderBy(s=>s.Id).ThenBy(s=>s.Type.FullName).ToArray();
        }
        public static void Validate(State[] saved)
        {
            var current=Capture();
            if(saved.Length!=current.Length)throw new InvalidOperationException("Native station sync membership changed.");
            for(int i=0;i<saved.Length;i++)if(saved[i].Id!=current[i].Id || saved[i].Component!=current[i].Component || saved[i].MessageType!=current[i].MessageType)
                throw new InvalidOperationException("Native station sync incarnation changed.");
        }
        public static void Restore(State[] saved)
        {
            Validate(saved);
            foreach(var s in saved) {
                var message=(Serialisable)Read(s,"m_data");
                if(!message.Deserialise(new BitStream.BitStreamReader(s.Payload)))throw new InvalidOperationException("Native station cached payload could not be restored.");
                Write(s,"m_pendingSyncMessage",s.Pending);
                Write(s,s.Type==typeof(ServerCookingStation)?"m_isTurnedOn":"m_bMixerOn",s.On);
                if(s.Type==typeof(ServerCookingStation))Write(s,"m_isCooking",s.Cooking);
            }
            if(!Same(saved,Capture()))throw new InvalidOperationException("Native station pending synchronization state differs after restore.");
        }
        public static bool Same(State[] a,State[] b)
        {
            if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i].Component!=b[i].Component || a[i].Pending!=b[i].Pending || a[i].On!=b[i].On
                || a[i].Cooking!=b[i].Cooking || !a[i].Payload.SequenceEqual(b[i].Payload))return false;
            return true;
        }
    }
}
