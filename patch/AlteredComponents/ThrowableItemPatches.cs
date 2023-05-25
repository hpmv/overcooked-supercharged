using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(ServerThrowableItem), "HandleThrow")]
    public static class PatchServerThrowableItemHandleThrow
    {
        public static void Postfix(ServerThrowableItem __instance, Collider[] ___m_ThrowStartColliders, int ___m_ignoredCollidersCount)
        {
            var aux = new ThrowableItemAuxMessage()
            {
                m_colliders = ___m_ThrowStartColliders.Take(___m_ignoredCollidersCount).ToArray(),
            };
            __instance.SendAuxMessage(aux);
        }
    }
}
