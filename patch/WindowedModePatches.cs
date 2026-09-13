using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SuperchargedPatch
{
    // User-requested display policy for this isolated TAS process. These are the
    // two native managed display writers in the installed game. Intercepting
    // their commits prevents a saved fullscreen preference from reaching Unity;
    // a later frame guard alone would permit a transient fullscreen transition.
    [HarmonyPatch]
    public static class WindowedModePatches
    {
        public const int Width = 1280;
        public const int Height = 720;
        private static FieldInfo windowedFullscreen;
        private static FieldInfo resolutionValue;
        private static int interceptedCommits;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Assembly game = typeof(GameUtils).Assembly;
            Type windowed = game.GetType("Windowed", true);
            Type resolution = game.GetType("Resolution", true);
            windowedFullscreen = RequireField(windowed, "m_fullscreen", typeof(bool));
            resolutionValue = RequireField(resolution, "m_resolution", typeof(UnityEngine.Resolution));
            yield return RequireCommit(windowed);
            yield return RequireCommit(resolution);
        }

        private static FieldInfo RequireField(Type owner, string name, Type expected)
        {
            FieldInfo field = AccessTools.Field(owner, name);
            if (field == null || field.FieldType != expected || field.IsStatic)
                throw new MissingFieldException(owner.FullName, name);
            return field;
        }

        private static MethodInfo RequireCommit(Type owner)
        {
            MethodInfo method = AccessTools.DeclaredMethod(owner, "Commit", Type.EmptyTypes);
            if (method == null || method.ReturnType != typeof(void) || method.IsStatic)
                throw new MissingMethodException(owner.FullName, "Commit");
            return method;
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, MethodBase __originalMethod)
        {
            if (__originalMethod.DeclaringType == windowedFullscreen.DeclaringType)
                windowedFullscreen.SetValue(__instance, false);
            else if (__originalMethod.DeclaringType == resolutionValue.DeclaringType)
            {
                var value = (UnityEngine.Resolution)resolutionValue.GetValue(__instance);
                value.width = Width; value.height = Height;
                resolutionValue.SetValue(__instance, value);
            }
            else throw new InvalidOperationException("Unexpected display-policy patch target.");

            ApplyWindowRequest(Screen.width, Screen.height, Screen.fullScreen,
                delegate(int width, int height, bool fullscreen) { Screen.SetResolution(width, height, fullscreen); });
            interceptedCommits++;
            return false;
        }

        // Only a windowed request may be emitted. No display-mode/desktop API,
        // preference write, window activation or foreground operation is used.
        internal static void ApplyWindowRequest(int currentWidth, int currentHeight, bool fullscreen, Action<int, int, bool> apply)
        {
            if (currentWidth != Width || currentHeight != Height || fullscreen)
                apply(Width, Height, false);
        }

        public static int InterceptedCommits { get { return interceptedCommits; } }

        // Pure policy fixtures: the callback is recorded, never sent to Unity.
        public static int SelfTest()
        {
            int count = 0;
            foreach (bool fullscreen in new bool[] { false, true })
            foreach (int width in new int[] { 1280, 1920 })
            foreach (int height in new int[] { 720, 1080 })
            {
                int calls = 0;
                ApplyWindowRequest(width, height, fullscreen, delegate(int w, int h, bool full)
                {
                    if (w != Width || h != Height || full) throw new Exception("Unsafe display request.");
                    calls++;
                });
                int expected = width == Width && height == Height && !fullscreen ? 0 : 1;
                if (calls != expected) throw new Exception("Display-policy request count disagrees.");
                count++;
            }
            return count;
        }
    }
}
