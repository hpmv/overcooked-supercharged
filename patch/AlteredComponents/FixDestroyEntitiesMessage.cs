using HarmonyLib;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch.AlteredComponents
{
    [HarmonyPatch(typeof(DestroyEntitiesMessage), "Initialise")]
    public static class FixDestroyEntitiesMessage
    {
        [HarmonyPrefix]
        public static bool Prefix(FastList<uint> ___m_ids, ref uint ___m_rootId, uint root, FastList<uint> ids)
        {
            ___m_rootId = root;
            ___m_ids.Clear();
            ids.ForEach(id => ___m_ids.Add(id));
            return false;
        }
    }
}
