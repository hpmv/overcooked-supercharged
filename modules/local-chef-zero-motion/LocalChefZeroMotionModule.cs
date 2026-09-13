using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Local chef controls call RigidbodyMotion.Movement twice per update for
    // wind and surface velocity. The installed method is exactly
    // MovePosition(body.position + movement * delta). Avoid invoking PhysX
    // only when that displacement is bit-exact zero; nonzero local movement,
    // every non-chef body and every other Rigidbody API retain native behavior.
    public sealed class LocalChefZeroMotionModule : IAuthoringModule
    {
        private static LocalChefZeroMotionModule active;
        private Harmony harmony;
        private FieldInfo motionBody;
        private bool disposed;
        private readonly List<object> receipts = new List<object>();
        private long skipped, passedThrough, nonlocal, nonzero;
        private string failure;

        public string Name { get { return "local-chef-exact-zero-motion-bypass-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("LocalChefZeroMotionModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Local-chef zero-motion operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"skippedExactZeroCalls",skipped},{"passedThrough",passedThrough},
                {"nonlocal",nonlocal},{"nonzero",nonzero},{"failure",failure},
                {"receipts",receipts.ToArray()},
                {"scope","Only RigidbodyMotion.Movement(Vector3,float) calls with bit-exact zero displacement on an object owning both ServerChefSynchroniser and ClientOnTheServerChefSynchroniser. Nonzero movement and all other objects retain the installed implementation."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Local-chef zero-motion activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another local-chef zero-motion module is active.");
            var movement = AccessTools.DeclaredMethod(typeof(RigidbodyMotion), "Movement", new[] {typeof(Vector3), typeof(float)});
            motionBody = AccessTools.Field(typeof(RigidbodyMotion), "m_rigidbody");
            if (movement == null || movement.ReturnType != typeof(void)
                || motionBody == null || motionBody.FieldType != typeof(Rigidbody))
                throw new InvalidOperationException("Installed RigidbodyMotion movement contract differs.");
            var installed = Harmony.GetPatchInfo(movement);
            if (installed != null && installed.Prefixes.Any(p => p.owner.StartsWith("supercharged.authoring.local-chef-zero-motion.", StringComparison.Ordinal)))
                throw new InvalidOperationException("Another local-chef zero-motion revision is already installed.");
            harmony = new Harmony("supercharged.authoring.local-chef-zero-motion." + GetType().Assembly.GetName().Name);
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
                var displacement = __0 * __1;
                if (displacement.x != 0f || displacement.y != 0f || displacement.z != 0f)
                {
                    module.passedThrough++; module.nonzero++;
                    return true;
                }
                module.skipped++;
                if (module.receipts.Count < 32)
                {
                    var position = body.position;
                    module.receipts.Add(new Dictionary<string, object> {
                        {"unityFrame",Time.frameCount},{"bodyInstanceId",body.GetInstanceID()},
                        {"movement",new[]{__0.x,__0.y,__0.z}},{"delta",__1},
                        {"position",new[]{position.x,position.y,position.z}},
                        {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)}
                    });
                }
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
