using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string methodName) { }
    }
    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public readonly List<int> labels = new();
        public readonly List<int> blocks = new();
        public CodeInstruction(OpCode value, object argument = null) { opcode = value; operand = argument; }
    }
    public static class AccessTools
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, All);
        public static MethodInfo Method(Type type, string name, Type[] parameters) => type.GetMethod(name, All, null, parameters, null);
        public static MethodInfo PropertyGetter(Type type, string name) => type.GetProperty(name, All)?.GetGetMethod(true);
    }
}

namespace UnityEngine
{
    public static class Time
    {
        public static int frameCount { get; set; }
        public static float time { get; set; }
        public static float realtimeSinceStartup { get; set; }
        public static int captureFramerate { get; set; }
    }
}

public sealed class ServerTime { public void Update() { } }
public sealed class ClientTime { public void Update() { } public float Time() => 0; public void OnTimeSyncReceived() { } }
public sealed class TimeManager { public void Update() { } }

namespace Team17.Online.Multiplayer.Messaging
{
    public interface Serialisable { }
    public sealed class Message : Serialisable { }
    public class ServerWorldObjectSynchroniser
    {
        public Serialisable GetServerUpdate()
        {
            float first = UnityEngine.Time.time;
            float second = UnityEngine.Time.time;
            return first == second ? new Message() : null;
        }
    }
}

namespace SuperchargedPatch
{
    public static class Helpers { public static TimeManager CurrentTimeManager; }
}

namespace SuperchargedPatch.Bridge
{
    public static class NativeSessionBridge { public static bool KitchenReady; }
}
