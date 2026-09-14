using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // TimeManager freezes Rigidbody public state for an authoring pause, but
    // Unity can continue automatic PhysX simulations on later render frames.
    // Those simulations mutate hidden contact/manifold history without moving
    // the logical TAS frame.  Disable only automatic simulation while the
    // bridge owns that pause, and restore it before the same Helpers.Resume
    // call releases the frozen bodies.
    public sealed class AuthoringPhysicsPauseGateModule : IAuthoringModule
    {
        private static AuthoringPhysicsPauseGateModule active;
        private readonly List<object> receipts = new List<object>();
        private Harmony harmony;
        private bool gated, pendingMaintenance, maintenanceFixedObserved;
        private bool originalAutoSimulation, disposed;
        private long pauses, resumes, disables, enables, maintenanceFixedCallbacks, maintenanceGates;
        private string failure;

        public string Name { get { return "authoring-physics-pause-gate-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("AuthoringPhysicsPauseGateModule");
            if (args == null) args = new Dictionary<string, object>();
            if (args.Count != 0) throw new ArgumentException("Operation takes no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return Status(operation);
        }

        private void Activate()
        {
            RequireFence();
            if (ReferenceEquals(active, this)) return;
            if (active != null) throw new InvalidOperationException("Another authoring physics pause gate is active.");
            if (!Physics.autoSimulation)
                throw new InvalidOperationException("Automatic physics simulation was already disabled before activation.");
            Type helpers = typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers", true);
            Type controller = typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.ControllerHandler", true);
            MethodInfo pause = AccessTools.DeclaredMethod(helpers, "Pause", Type.EmptyTypes);
            MethodInfo resume = AccessTools.DeclaredMethod(helpers, "Resume", Type.EmptyTypes);
            MethodInfo fixedUpdate = AccessTools.DeclaredMethod(controller, "FixedUpdate", Type.EmptyTypes);
            MethodInfo update = AccessTools.DeclaredMethod(controller, "Update", Type.EmptyTypes);
            if (pause == null || resume == null || fixedUpdate == null || update == null ||
                    pause.ReturnType != typeof(void) || resume.ReturnType != typeof(void) ||
                    fixedUpdate.ReturnType != typeof(void) || update.ReturnType != typeof(void))
                throw new InvalidOperationException("Installed authoring pause/resume contract differs.");
            originalAutoSimulation = Physics.autoSimulation;
            harmony = new Harmony("supercharged.authoring.physics-pause-gate." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(pause, postfix: new HarmonyMethod(GetType().GetMethod("AfterPause", BindingFlags.Public | BindingFlags.Static)));
                harmony.Patch(resume, prefix: new HarmonyMethod(GetType().GetMethod("BeforeResume", BindingFlags.Public | BindingFlags.Static)));
                harmony.Patch(fixedUpdate, postfix: new HarmonyMethod(GetType().GetMethod("AfterFixedUpdate", BindingFlags.Public | BindingFlags.Static)));
                harmony.Patch(update, prefix: new HarmonyMethod(GetType().GetMethod("BeforeUpdate", BindingFlags.Public | BindingFlags.Static)));
                Gate("activate");
            }
            catch { DeactivateInternal(); throw; }
        }

        public static void AfterPause()
        {
            AuthoringPhysicsPauseGateModule module = active;
            if (module == null || !NativeSessionBridge.KitchenReady) return;
            try { module.pauses++; module.BeginMaintenance(); module.failure = null; }
            catch (Exception error) { module.failure = "Pause: " + error; throw; }
        }

        public static void BeforeResume()
        {
            AuthoringPhysicsPauseGateModule module = active;
            if (module == null || !NativeSessionBridge.KitchenReady) return;
            try
            {
                module.resumes++;
                module.pendingMaintenance = false;
                module.maintenanceFixedObserved = false;
                module.Ungate("resume");
                module.failure = null;
            }
            catch (Exception error) { module.failure = "Resume: " + error; throw; }
        }

        public static void AfterFixedUpdate()
        {
            AuthoringPhysicsPauseGateModule module = active;
            if (module == null || !module.pendingMaintenance || !NativeSessionBridge.KitchenReady ||
                    !TimeManager.IsPaused(TimeManager.PauseLayer.Main)) return;
            module.maintenanceFixedObserved = true;
            module.maintenanceFixedCallbacks++;
        }

        public static void BeforeUpdate()
        {
            AuthoringPhysicsPauseGateModule module = active;
            if (module == null || !module.pendingMaintenance || !module.maintenanceFixedObserved ||
                    !NativeSessionBridge.KitchenReady || !TimeManager.IsPaused(TimeManager.PauseLayer.Main)) return;
            try
            {
                module.Gate("post-maintenance");
                module.pendingMaintenance = false;
                module.maintenanceFixedObserved = false;
                module.maintenanceGates++;
                module.failure = null;
            }
            catch (Exception error) { module.failure = "Maintenance: " + error; throw; }
        }

        private void BeginMaintenance()
        {
            if (gated)
            {
                if (Physics.autoSimulation) throw new InvalidOperationException("Paused automatic simulation became enabled.");
                return;
            }
            if (!Physics.autoSimulation)
                throw new InvalidOperationException("Automatic simulation changed before paused maintenance.");
            if (!pendingMaintenance)
            {
                pendingMaintenance = true;
                maintenanceFixedObserved = false;
                AddReceipt("pause-await-maintenance", true);
            }
        }

        private void Gate(string reason)
        {
            if (gated)
            {
                if (Physics.autoSimulation) throw new InvalidOperationException("Paused automatic simulation became enabled.");
                return;
            }
            if (!Physics.autoSimulation)
                throw new InvalidOperationException("Automatic simulation changed before the pause gate acquired it.");
            Physics.autoSimulation = false;
            if (Physics.autoSimulation) throw new InvalidOperationException("Automatic simulation disable did not read back exactly.");
            gated = true; disables++;
            AddReceipt(reason, false);
        }

        private void Ungate(string reason)
        {
            if (!gated)
            {
                if (Physics.autoSimulation != originalAutoSimulation)
                    throw new InvalidOperationException("Advancing automatic simulation differs from its captured value.");
                return;
            }
            Physics.autoSimulation = originalAutoSimulation;
            if (Physics.autoSimulation != originalAutoSimulation)
                throw new InvalidOperationException("Automatic simulation restore did not read back exactly.");
            gated = false; enables++;
            AddReceipt(reason, originalAutoSimulation);
        }

        private void AddReceipt(string reason, bool value)
        {
            if (receipts.Count >= 64) receipts.RemoveAt(0);
            receipts.Add(new Dictionary<string, object> {
                {"reason", reason}, {"unityFrame", Time.frameCount}, {"autoSimulation", value}
            });
        }

        private object Status(string operation)
        {
            return new Dictionary<string, object> {
                {"name", Name}, {"apiVersion", 1}, {"operation", operation},
                {"active", ReferenceEquals(active, this)}, {"gated", gated},
                {"pendingMaintenance", pendingMaintenance}, {"maintenanceFixedObserved", maintenanceFixedObserved},
                {"originalAutoSimulation", originalAutoSimulation}, {"liveAutoSimulation", Physics.autoSimulation},
                {"pauses", pauses}, {"resumes", resumes}, {"disables", disables}, {"enables", enables},
                {"maintenanceFixedCallbacks", maintenanceFixedCallbacks}, {"maintenanceGates", maintenanceGates},
                {"failure", failure}, {"receipts", receipts.ToArray()},
                {"scope", "Authoring-only pause gate: automatic Unity physics remains unchanged on every advancing frame; exactly one FixedUpdate/physics maintenance interval is admitted after a transition into Helpers.Pause, then automatic simulation is disabled before Update and restored immediately before Helpers.Resume. No Rigidbody, collider, contact, score, input or gameplay field is written."}
            };
        }

        private void Deactivate()
        {
            RequireFence();
            DeactivateInternal();
        }

        private void DeactivateInternal()
        {
            if (gated)
            {
                try { Ungate("deactivate"); }
                catch { }
            }
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            if (ReferenceEquals(active, this)) active = null;
            gated = false; pendingMaintenance = false; maintenanceFixedObserved = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            DeactivateInternal();
            disposed = true;
        }

        private static void RequireFence()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Authoring physics pause gate requires the paused input fence.");
        }
    }
}
