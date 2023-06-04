using BepInEx;
using BepInEx.Harmony;
using HarmonyLib;
using Hpmv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SuperchargedPatch
{
    [BepInPlugin("dev.hpmv.overcooked.experimental.supercharged.tas.v1", "Hpmv Overcooked Supercharged TAS Plugin V1", "1.0.0.0")]
    [BepInProcess("Overcooked2.exe")]
    class TASPatcher : BaseUnityPlugin
    {
        private static Harmony patcher;
        public void Awake()
        {
            patcher = new HarmonyLib.Harmony("dev.hpmv.overcooked.experimental.supercharged.tas.v1");
            patcher.PatchAll(Assembly.GetExecutingAssembly());
            foreach (var patched in Harmony.GetAllPatchedMethods()) {
                Console.WriteLine("Patched: " + patched.FullDescription());
            }
            DebugItemOverlay.Awake();
        }

        public void Update()
        {
            UnrealTimePatch.Update();
        }

        public void LateUpdate()
        {
            ServerInterceptionPatches.LateUpdate();
        }

        public void OnGUI()
        {
            DebugItemOverlay.OnGUI();
        }

        public void OnDestroy()
        {
            patcher.UnpatchSelf();
            Injector.Destroy();
            WarpHandler.Destroy();
            DebugItemOverlay.Destroy();
        }
    }
}
