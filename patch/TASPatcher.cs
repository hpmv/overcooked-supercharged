using BepInEx;
using BepInEx.Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuperchargedPatch
{
    [BepInPlugin("dev.hpmv.overcooked.experimental.supercharged.tas.v1", "Hpmv Overcooked Supercharged TAS Plugin V1", "1.0.0.0")]
    [BepInProcess("Overcooked2.exe")]
    class TASPatcher : BaseUnityPlugin
    {
        public void Awake()
        {
            ServerInterceptionPatches.EnableInputInjection = true;
            HarmonyWrapper.PatchAll(typeof(ServerInterceptionPatches));
            HarmonyWrapper.PatchAll(typeof(UnrealTimePatch));
        }

        public void Update()
        {
            UnrealTimePatch.Update();
            ServerInterceptionPatches.Update();
        }

        public void LateUpdate()
        {
            ServerInterceptionPatches.LateUpdate();
        }
    }
}
