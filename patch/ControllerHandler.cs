using System;
using System.Collections;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Extensions;

namespace SuperchargedPatch
{
    public static class ControllerHandler
    {
        public static MultiplayerController MultiplayerController;

        public static void LateUpdate()
        {
            // Make sure to flush any network messages before capturing state. Normally this
            // is done as part of MultiplayerController.LateUpdate(), but we're also executing in
            // LateUpdate so the order is not deterministic.
            MultiplayerController?.FlushAllPendingBatchedMessages();

            if (Helpers.IsPaused())
            {
                WarpHandler.HandleWarpRequestIfAny();
            }
            var data = Injector.Server.CurrentFrameData;
            if (!Helpers.IsPaused())
            {
                if (Injector.Server.CurrentInput.__isset.nextFrame)
                {
                    ActiveStateCollector.NotifyFrame(Injector.Server.CurrentInput.NextFrame);
                }
            }
            ActiveStateCollector.CollectDataForFrame(data);
            data.LastFramePaused = Helpers.IsPaused();
            if (Injector.Server.CurrentInput.RequestPause)
            {
                Helpers.Pause();
            }
            else if (Injector.Server.CurrentInput.RequestResume)
            {
                Helpers.Resume();
            }
            StateInvalidityManager.PreventInvalidState = Injector.Server.CurrentInput.PreventInvalidState;

            data.NextFramePaused = TimeManager.IsPaused(TimeManager.PauseLayer.Main);
            Injector.Server.CommitFrame();
        }

        public static void FixedUpdate()
        {
            Injector.Server.CurrentFrameData.PhysicsFramesElapsed += 1;
        }

        public static void Update()
        {
            if (Injector.Server.CurrentFrameData.PhysicsFramesElapsed == 0)
            {
                FramesSinceLastNoPhysicsFrame = 0;
            }
            else
            {
                FramesSinceLastNoPhysicsFrame += 1;
            }
            Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame = FramesSinceLastNoPhysicsFrame;
        }

        public static int FramesSinceLastNoPhysicsFrame = 0;
    }

    [HarmonyPatch(typeof(MultiplayerController), "Update")]
    public static class MultiplayerControllerAwakePatch
    {
        public static void Postfix(MultiplayerController __instance)
        {
            // Do this, because FindObjectOfType is very very slow.
            ControllerHandler.MultiplayerController = __instance;
        }
    }

    [HarmonyPatch(typeof(ClientFlowControllerBase), "RunLevelIntro")]
    public static class PatchClientFlowControllerBaseRunLevelIntro
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerator __result)
        {
            __result = WaitForNoPhysicsFrame(__result);
        }

        private static IEnumerator WaitForNoPhysicsFrame(IEnumerator then)
        {
            for (int i = 0; ; i++)
            {
                yield return null;
                if (Injector.Server.CurrentFrameData.PhysicsFramesElapsed == 0)
                {
                    Console.WriteLine("Successfully started with non-physics frame after " + i + " tries");
                    break;
                }
                if (i == 6)
                {
                    Console.WriteLine("Warning: FAILED TO WAIT FOR NO PHYSICS FRAME");
                    break;
                }
            }
            while (then.MoveNext())
            {
                yield return then.Current;
            }
            yield break;
        }
    }

    [HarmonyPatch(typeof(ServerFlowControllerBase), "ChangeGameState")]
    public static class PatchServerFlowControllerBaseChangeGameState
    {
        [HarmonyPostfix]
        public static void Postfix(GameState state)
        {
            if (state == GameState.InLevel)
            {
                Console.WriteLine("At game start, physics phase shift is " + ControllerHandler.FramesSinceLastNoPhysicsFrame);
            }
        }
    }
}
