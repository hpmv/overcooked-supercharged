using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // RigidbodyMotion.Movement(Vector3,float) contains only
    // MovePosition(body.position + velocity * delta). During an authoring pause
    // the installed local controls repeatedly call it with an exact zero
    // displacement. That is publicly a no-op but rewrites PhysX's private
    // kinematic target. Skip only that fenced pause call; all advancing and all
    // non-local/nonzero calls retain the installed game implementation.
    public sealed class PausedChefMotionBypassModule : IAuthoringModule
    {
        private static PausedChefMotionBypassModule active;
        private Harmony harmony;
        private FieldInfo motionBody;
        private bool disposed;
        private readonly List<object> receipts = new List<object>();
        private long skipped, passedThrough, running, unfenced, nonlocal, nonzeroTarget;
        private string failure;

        public string Name { get { return "authoring-paused-local-chef-zero-motion-bypass-v2"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("PausedChefMotionBypassModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Paused-chef-motion operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"skippedPausedNoOpCalls",skipped},{"passedThrough",passedThrough},
                {"running",running},{"unfenced",unfenced},{"nonlocal",nonlocal},{"nonzeroTarget",nonzeroTarget},
                {"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Only exact-zero RigidbodyMotion.Movement calls on paired local chefs while native Main pause and the authoring input fence are both active. Advancing gameplay always passes through."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Paused-chef-motion activation requires the authoring pause fence.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another paused-chef-motion bypass is active.");
            var movement = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "Movement", new[] {typeof(Vector3), typeof(float)});
            motionBody = AccessTools.Field(typeof(RigidbodyMotion), "m_rigidbody");
            if (movement == null || movement.ReturnType != typeof(void)
                || motionBody == null || motionBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Installed RigidbodyMotion movement contract differs.");
            var installed = Harmony.GetPatchInfo(movement);
            if (installed != null && installed.Prefixes.Any(p => p.owner.StartsWith("supercharged.authoring.paused-chef-motion.", StringComparison.Ordinal)))
                throw new InvalidOperationException("Another paused-chef-motion revision is already installed.");
            harmony = new Harmony("supercharged.authoring.paused-chef-motion." + GetType().Assembly.GetName().Name);
            active = this;
            try { harmony.Patch(movement, prefix: new HarmonyMethod(GetType().GetMethod("BeforeMovement", BindingFlags.Public | BindingFlags.Static))); }
            catch { Deactivate(); throw; }
        }

        public static bool BeforeMovement(RigidbodyMotion __instance, Vector3 __0, float __1)
        {
            var module = active;
            if (module == null) return true;
            try
            {
                if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                {
                    module.passedThrough++; module.running++;
                    return true;
                }
                if (!NativeSessionBridge.InputBlocked)
                {
                    module.passedThrough++; module.unfenced++;
                    return true;
                }
                if (__instance == null)
                {
                    module.passedThrough++; module.nonlocal++;
                    return true;
                }
                var body = (Rigidbody)module.motionBody.GetValue(__instance);
                if (body == null || body.GetComponent<ServerChefSynchroniser>() == null
                    || body.GetComponent<ClientOnTheServerChefSynchroniser>() == null)
                {
                    module.passedThrough++; module.nonlocal++;
                    return true;
                }
                var before = body.position;
                var displacement = __0 * __1;
                var target = before + displacement;
                if (target != before)
                {
                    module.passedThrough++; module.nonzeroTarget++;
                    return true;
                }
                module.skipped++;
                if (module.receipts.Count < 64)
                    module.receipts.Add(new Dictionary<string, object> {
                        {"unityFrame",Time.frameCount},{"bodyInstanceId",body.GetInstanceID()},
                        {"velocity",new[]{__0.x,__0.y,__0.z}},{"delta",__1},
                        {"displacement",new[]{displacement.x,displacement.y,displacement.z}},
                        {"position",new[]{before.x,before.y,before.z}},{"target",new[]{target.x,target.y,target.z}}
                    });
                return false;
            }
            catch (Exception error)
            {
                module.failure = error.Message;
                module.passedThrough++;
                return true;
            }
        }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            if (ReferenceEquals(active, this)) active = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate(); disposed = true;
        }
    }
}
