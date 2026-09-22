using System.Reflection;

public sealed class TimeManager
{
    public enum PauseLayer { Main, UI, Camera, System, Network }

    private readonly List<object>[] m_arbitrationSupressors =
        Enumerable.Range(0, 5).Select(_ => new List<object>()).ToArray();
    private List<object> savedMainOwners;

    public static TimeManager Current { get; set; }
    public static bool IsPaused(PauseLayer layer) =>
        Current != null && Current.m_arbitrationSupressors[(int)layer]?.Count > 0;

    public void SetPaused(PauseLayer layer, bool paused, object owner)
    {
        var owners = m_arbitrationSupressors[(int)layer] ??
            throw new InvalidOperationException("pause layer unavailable");
        if (paused) owners.Add(owner);
        else owners.RemoveAll(value => Equals(value, owner));
    }

    public IReadOnlyList<object> Owners(PauseLayer layer) => m_arbitrationSupressors[(int)layer];
    public int StableCount => m_arbitrationSupressors[(int)PauseLayer.Main]
        .Count(value => ReferenceEquals(value, SuperchargedPatch.Helpers.StableOwner));

    public void BreakMainOwnersForTest()
    {
        savedMainOwners = m_arbitrationSupressors[(int)PauseLayer.Main];
        m_arbitrationSupressors[(int)PauseLayer.Main] = null;
    }

    public void RestoreMainOwnersForTest(object owner)
    {
        m_arbitrationSupressors[(int)PauseLayer.Main] = savedMainOwners ?? new List<object>();
        if (!m_arbitrationSupressors[(int)PauseLayer.Main].Any(value => ReferenceEquals(value, owner)))
            m_arbitrationSupressors[(int)PauseLayer.Main].Add(owner);
    }
}

namespace UnityEngine
{
    public static class Time { public static int frameCount; }
}

namespace SuperchargedPatch.Bridge
{
    public static class NativeSessionBridge
    {
        public static bool InputBlocked { get; set; }
        public static bool KitchenReady { get; set; }
    }
}

namespace SuperchargedPatch
{
    public static class UnrealTimePatch
    {
        public static int TrueCalls, FalseCalls;
        public static void SetAuthoringPause(bool paused)
        {
            if (paused) TrueCalls++;
            else FalseCalls++;
        }
    }

    public static class Helpers
    {
        private static readonly object timeManagerPauseArbitration = typeof(TimeManager);
        public static object StableOwner => timeManagerPauseArbitration;
        public static TimeManager CurrentTimeManager { get; set; }

        public static void Pause()
        {
            MethodInfo target = typeof(Helpers).GetMethod(nameof(Pause),
                BindingFlags.Static | BindingFlags.Public);
            if (!HarmonyLib.Harmony.RunPrefix(target)) return;
            CurrentTimeManager.SetPaused(TimeManager.PauseLayer.Main, true,
                timeManagerPauseArbitration);
            UnrealTimePatch.SetAuthoringPause(true);
        }

        public static void Resume()
        {
            CurrentTimeManager.SetPaused(TimeManager.PauseLayer.Main, false,
                timeManagerPauseArbitration);
            UnrealTimePatch.SetAuthoringPause(false);
        }
    }
}

namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public MethodInfo method;
        public HarmonyMethod(MethodInfo value) { method = value; }
    }

    public sealed class Harmony
    {
        private static MethodBase target;
        private static MethodInfo prefix;
        private static string owner;
        public string Id { get; }
        public Harmony(string id) { Id = id; }

        public void Patch(MethodBase original, HarmonyMethod prefix = null,
            HarmonyMethod postfix = null, HarmonyMethod transpiler = null,
            HarmonyMethod finalizer = null)
        {
            target = original;
            Harmony.prefix = prefix?.method;
            owner = Id;
        }

        public void UnpatchSelf()
        {
            if (owner != Id) return;
            target = null;
            prefix = null;
            owner = null;
        }

        public static bool RunPrefix(MethodBase method)
        {
            if (!Equals(target, method) || prefix == null) return true;
            return (bool)prefix.Invoke(null, null);
        }
    }

    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
    }
}
