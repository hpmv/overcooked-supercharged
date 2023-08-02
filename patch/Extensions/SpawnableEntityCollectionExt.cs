using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class SpawnableEntityCollectionExt
    {
        private static readonly Type type = typeof(SpawnableEntityCollection);
        private static readonly FieldInfo f_m_spawnables = AccessTools.Field(type, "m_spawnables");

        public static List<GameObject> GetSpawnables(this SpawnableEntityCollection collection)
        {
            return (List<GameObject>)f_m_spawnables.GetValue(collection);
        }
    }
}
