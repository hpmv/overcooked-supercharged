using HarmonyLib;
using Hpmv;
using System;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using SuperchargedPatch.Extensions;

namespace SuperchargedPatch
{
    public static class ControllerHandler
    {
        public static void LateUpdate()
        {
            // Make sure to flush any network messages before capturing state. Normally this
            // is done as part of MultiplayerController.LateUpdate(), but we're also executing in
            // LateUpdate so the order is not deterministic.
            GameObject.FindObjectOfType<MultiplayerController>()?.FlushAllPendingBatchedMessages();

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
            } else if (Injector.Server.CurrentInput.RequestResume)
            {
                Helpers.Resume();
            }
            StateInvalidityManager.PreventInvalidState = Injector.Server.CurrentInput.PreventInvalidState;

            data.NextFramePaused = TimeManager.IsPaused(TimeManager.PauseLayer.Main);
            Injector.Server.CommitFrame();
        }
    }
}
