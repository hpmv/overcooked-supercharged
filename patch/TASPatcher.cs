using BepInEx;
using HarmonyLib;
using Hpmv;
using System;
using System.Reflection;

namespace SuperchargedPatch
{
    [BepInPlugin("dev.hpmv.overcooked.experimental.supercharged.tas.v1", "Hpmv Overcooked Supercharged TAS Plugin V1", "1.0.0.0")]
    [BepInProcess("Overcooked2.exe")]
    class TASPatcher : BaseUnityPlugin
    {
        private static Harmony patcher;
        private bool ready;
        public void Awake()
        {
            SuperchargedPatch.Bridge.NativeSessionBridge.Awake();
            patcher = new Harmony("dev.hpmv.overcooked.experimental.supercharged.tas.v1");
            patcher.PatchAll(Assembly.GetExecutingAssembly());
            SuperchargedPatch.Bridge.NativeSessionBridge.SetImmediateInputConsumer(TASLogicalButton.ApplyInputFrame);
            foreach (var patched in Harmony.GetAllPatchedMethods())
            {
                Console.WriteLine("Patched: " + patched.FullDescription());
            }
            DebugItemOverlay.Awake();
            ready = true;
        }

        public void FixedUpdate()
        {
            if (!ready) return;
            ControllerHandler.FixedUpdate();
        }

        public void Update()
        {
            if (!ready) return;
            UnrealTimePatch.Update();
            ControllerHandler.Update();
        }

        public void LateUpdate()
        {
            SuperchargedPatch.Bridge.NativeSessionBridge.LateUpdate();
            if (!ready) return;
            ControllerHandler.LateUpdate();
        }

        public void OnGUI()
        {
            if (!ready) return;
            DebugItemOverlay.OnGUI();
            StateInvalidityManager.OnGUI();
        }

        public void OnDestroy()
        {
            SuperchargedPatch.Bridge.NativeSessionBridge.Destroy();
            if (!ready) return;
            patcher.UnpatchSelf();
            Injector.Destroy();
            WarpHandler.Destroy();
            DebugItemOverlay.Destroy();
            StateInvalidityManager.Destroy();
        }
    }
}
