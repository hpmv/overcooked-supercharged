// CPU harness only. Native button and RoundData algorithms are copied verbatim
// from the installed-game decompilation by run_framework_native_semantics.py.
// Unity clock/focus/RNG/object APIs are controlled test doubles, not native proof.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public static class Application { public static bool isFocused = true; }
    public class GameObject
    {
        static int next;
        readonly int id = ++next;
        public bool Destroyed;
        public int GetInstanceID() => id;
        public static bool operator ==(GameObject a, GameObject b)
        {
            bool absentA = ReferenceEquals(a, null) || a.Destroyed;
            bool absentB = ReferenceEquals(b, null) || b.Destroyed;
            return absentA || absentB ? absentA == absentB : ReferenceEquals(a, b);
        }
        public static bool operator !=(GameObject a, GameObject b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => id;
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public static class Random
    {
        public struct State { public uint Value; }
        public static State state { get; set; }
        public static int DrawCount;
        public static bool ThrowNext;
        public static void InitState(int seed) { state = new State { Value = unchecked((uint)seed) }; }
        public static float Range(float lo, float hi)
        {
            DrawCount++;
            state = new State { Value = unchecked(state.Value * 1664525u + 1013904223u) };
            if (ThrowNext) { ThrowNext = false; throw new InvalidOperationException("Injected RNG failure after native RecipeCount increment."); }
            return lo + (state.Value >> 8) / 16777216f * (hi - lo);
        }
    }
}
namespace Hpmv
{
    public sealed class InputData { public Dictionary<int, OneInputData> Input; }
    public sealed class OneInputData { public Pad Pad; public ButtonInput Pickup, Interact, Dash; }
    public sealed class Pad { public double X, Y; }
    public sealed class ButtonInput { public bool Down, JustPressed, JustReleased; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
}
public delegate T Generic<T>();
public delegate T Generic<T, A, B>(A a, B b);
public class LogicalLinkInfo { }
public class AcyclicGraph<T, U>
{
    public class Node { public T m_value; }
    public void AddLink(T a, T b, U link) { }
    public Node GetNode(T value) => new() { m_value = value };
}
public interface ILogicalValue : ILogicalElement { float GetValue(); }
public abstract class RoundInstanceDataBase { }
public class CampaignLevelConfig { }
public sealed class RecipeList
{
    public Entry[] m_recipes;
    public sealed class Entry { public OrderDefinitionNode m_order; public int m_scoreForMeal; }
}
public sealed class OrderDefinitionNode
{
    public int m_uID; public string name;
    public AssembledDefinitionNode Convert() => new() { RecipeId = m_uID };
}
public sealed class AssembledDefinitionNode { public int RecipeId; }
namespace SuperchargedPatch
{
    public sealed class RoundDataAuxMessage { public AssembledDefinitionNode[] recipes; public int currentIndex; }
}
