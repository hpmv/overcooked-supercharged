namespace SuperchargedPatch
{
    public static class NativeCannonWarpGuard
    {
        // Native ServerCannon may retain a stale loadedObject after landing. It
        // is not an activity predicate; actual sessions/iterators/control state are.
        public static string UnsettledReason(bool serverSession, bool clientSession, bool flying, int launches,
            bool parented, bool callbackPending, bool handlerActive, bool animationActive)
        {
            if(serverSession || clientSession) return "native cannon session active";
            if(flying || launches != 0) return "native cannon flight/exit iterator active";
            if(parented || callbackPending || handlerActive) return "native cannon participant/cleanup active";
            if(animationActive) return "native cannon animation not settled";
            return null;
        }
    }
}
