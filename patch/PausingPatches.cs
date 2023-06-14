using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    // The game's pausing logic is not perfect. In certain cases, the game may perform actions
    // even while paused. This is detrimental for warping. So, we'll just disable the
    // UpdateSynchronising() functions while the game is paused. I'm not confident this is
    // safe... but we'll see.
    [HarmonyPatch(typeof(SynchroniserBase), "IsSynchronising")]
    public static class SkipSynchroniserBaseIfPaused
    {
        public static bool Prefix(SynchroniserBase __instance)
        {
            return __instance is IFlowController || !Helpers.IsPaused();
        }
    }

    [HarmonyPatch(typeof(PlayerControls), "Update")]
    public static class SkipPlayerControlsIfPaused
    {
        public static bool Prefix()
        {
            return !Helpers.IsPaused();
        }
    }
}
