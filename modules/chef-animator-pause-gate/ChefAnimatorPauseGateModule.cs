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
    // Diagnostic authoring fence. Native TimeManager already stops gameplay
    // during a bridge-owned pause, but Unity's DirectorManager continues to
    // invoke Animator::WriteProperties on render callbacks. Prevent only those
    // paused chef evaluations, then restore the exact component-enabled flags
    // immediately before the same Helpers.Resume call releases native physics.
    public sealed class ChefAnimatorPauseGateModule : IAuthoringModule
    {
        private sealed class Saved
        {
            internal Animator Animator;
            internal int InstanceId;
            internal string Path;
            internal bool Enabled;
        }

        private static ChefAnimatorPauseGateModule active;
        private readonly List<Saved> saved = new List<Saved>();
        private readonly List<object> receipts = new List<object>();
        private Harmony harmony;
        private bool gated, disposed;
        private long pauses, resumes, disables, enables;
        private string failure;

        public string Name { get { return "chef-animator-pause-gate-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ChefAnimatorPauseGateModule");
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
            if (active != null) throw new InvalidOperationException("Another chef Animator pause gate is active.");
            Type helpers = typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers", true);
            MethodInfo pause = AccessTools.DeclaredMethod(helpers, "Pause", Type.EmptyTypes);
            MethodInfo resume = AccessTools.DeclaredMethod(helpers, "Resume", Type.EmptyTypes);
            if (pause == null || resume == null || pause.ReturnType != typeof(void) || resume.ReturnType != typeof(void))
                throw new InvalidOperationException("Installed authoring pause/resume contract differs.");
            CaptureAnimators();
            harmony = new Harmony("supercharged.authoring.chef-animator-pause-gate." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(pause, postfix: new HarmonyMethod(GetType().GetMethod("AfterPause", BindingFlags.Public | BindingFlags.Static)));
                harmony.Patch(resume, prefix: new HarmonyMethod(GetType().GetMethod("BeforeResume", BindingFlags.Public | BindingFlags.Static)));
                Gate("activate");
            }
            catch { DeactivateInternal(); throw; }
        }

        private void CaptureAnimators()
        {
            saved.Clear();
            ServerChefSynchroniser[] chefs = (ServerChefSynchroniser[])UnityEngine.Object.FindObjectsOfType(typeof(ServerChefSynchroniser));
            if (chefs.Length != 4) throw new InvalidOperationException("Pause gate requires exactly four live chefs.");
            var identities = new HashSet<int>();
            foreach (ServerChefSynchroniser chef in chefs)
            {
                Animator[] animators = chef.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1)
                    throw new InvalidOperationException("Each chef must have exactly one child Animator: " + PathOf(chef.transform));
                Animator animator = animators[0];
                if (!identities.Add(animator.GetInstanceID())) throw new InvalidOperationException("Chef Animator identity is duplicated.");
                saved.Add(new Saved { Animator = animator, InstanceId = animator.GetInstanceID(),
                    Path = PathOf(animator.transform), Enabled = animator.enabled });
            }
            saved.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            if (saved.Any(row => !row.Enabled))
                throw new InvalidOperationException("The diagnostic requires all four chef Animators enabled before activation.");
        }

        public static void AfterPause()
        {
            ChefAnimatorPauseGateModule module = active;
            if (module == null || !NativeSessionBridge.KitchenReady) return;
            try { module.pauses++; module.Gate("pause"); module.failure = null; }
            catch (Exception error) { module.failure = "Pause: " + error; throw; }
        }

        public static void BeforeResume()
        {
            ChefAnimatorPauseGateModule module = active;
            if (module == null || !NativeSessionBridge.KitchenReady) return;
            try { module.resumes++; module.Ungate("resume"); module.failure = null; }
            catch (Exception error) { module.failure = "Resume: " + error; throw; }
        }

        private void Gate(string reason)
        {
            ValidateIdentities();
            if (gated)
            {
                if (saved.Any(row => row.Animator.enabled))
                    throw new InvalidOperationException("A gated chef Animator became enabled.");
                return;
            }
            foreach (Saved row in saved) row.Animator.enabled = false;
            if (saved.Any(row => row.Animator.enabled)) throw new InvalidOperationException("Chef Animator disable did not read back exactly.");
            gated = true; disables++;
            AddReceipt(reason, false);
        }

        private void Ungate(string reason)
        {
            ValidateIdentities();
            if (!gated)
            {
                if (saved.Any(row => row.Animator.enabled != row.Enabled))
                    throw new InvalidOperationException("An ungated chef Animator enabled flag changed.");
                return;
            }
            foreach (Saved row in saved) row.Animator.enabled = row.Enabled;
            if (saved.Any(row => row.Animator.enabled != row.Enabled))
                throw new InvalidOperationException("Chef Animator enabled restore did not read back exactly.");
            gated = false; enables++;
            AddReceipt(reason, true);
        }

        private void ValidateIdentities()
        {
            if (saved.Count != 4) throw new InvalidOperationException("Chef Animator snapshot is incomplete.");
            foreach (Saved row in saved)
                if (row.Animator == null || row.Animator.GetInstanceID() != row.InstanceId || PathOf(row.Animator.transform) != row.Path)
                    throw new InvalidOperationException("Chef Animator incarnation changed: " + row.Path);
        }

        private void AddReceipt(string reason, bool enabled)
        {
            if (receipts.Count >= 64) receipts.RemoveAt(0);
            receipts.Add(new Dictionary<string, object> {
                {"reason", reason}, {"unityFrame", Time.frameCount}, {"enabled", enabled},
                {"animatorIds", saved.Select(row => row.InstanceId).ToArray()}
            });
        }

        private object Status(string operation)
        {
            return new Dictionary<string, object> {
                {"name", Name}, {"apiVersion", 1}, {"operation", operation},
                {"active", ReferenceEquals(active, this)}, {"gated", gated},
                {"pauses", pauses}, {"resumes", resumes}, {"disables", disables}, {"enables", enables},
                {"failure", failure}, {"receipts", receipts.ToArray()},
                {"animators", saved.Select(row => (object)new Dictionary<string, object> {
                    {"instanceId", row.InstanceId}, {"path", row.Path},
                    {"savedEnabled", row.Enabled}, {"liveEnabled", row.Animator != null && row.Animator.enabled}
                }).ToArray()},
                {"scope", "Diagnostic authoring-only gate: disable the four local-chef Animators only while Helpers owns the main pause, and restore their exact enabled flags immediately before Helpers.Resume. No advancing callback or game-data field is patched."}
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
            saved.Clear(); gated = false;
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
                throw new InvalidOperationException("Chef Animator pause gate requires the authoring pause fence.");
        }

        private static string PathOf(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
    }
}
