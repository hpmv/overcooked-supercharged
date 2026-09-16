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
    // Local TAS does not consume ServerChefSynchroniser's ordinary world packet:
    // the installed override discards the base result, and the paired
    // ClientOnTheServerChefSynchroniser update/event handlers are empty. Keep
    // the bypass guarded per instance so an unexpected remote chef retains the
    // original implementation.
    public sealed class LocalSyncBypassModule : IAuthoringModule
    {
        private static LocalSyncBypassModule active;
        private Harmony harmony;
        private bool disposed;
        private long skipped, passedThrough;
        private string failure;

        public string Name { get { return "local-chef-world-sync-bypass-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("LocalSyncBypassModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Local-sync operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate or status.");
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"skippedServerChefUpdates",skipped},{"passedThrough",passedThrough},{"failure",failure},
                {"scope","Exact ServerChefSynchroniser.GetServerUpdate override only. Bypass requires the same object to own ClientOnTheServerChefSynchroniser; all other instances retain native behavior."}
            };
        }

        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Local-sync activation requires native pause.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another local-sync bypass is active.");
            var method = AccessTools.DeclaredMethod(typeof(ServerChefSynchroniser), "GetServerUpdate", Type.EmptyTypes);
            if (method == null || method.ReturnType != typeof(Serialisable))
                throw new InvalidOperationException("Installed ServerChefSynchroniser update contract differs.");
            var installed = Harmony.GetPatchInfo(method);
            if (installed != null && installed.Prefixes.Any(p => p.owner.StartsWith("supercharged.authoring.local-sync.", StringComparison.Ordinal)))
                throw new InvalidOperationException("Another local-sync revision is already installed.");
            harmony = new Harmony("supercharged.authoring.local-sync." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(GetType().GetMethod("BeforeServerChefUpdate", BindingFlags.Public | BindingFlags.Static)));
            }
            catch { Deactivate(); throw; }
        }

        public static bool BeforeServerChefUpdate(ServerChefSynchroniser __instance, ref Serialisable __result)
        {
            var module = active;
            if (module == null) return true;
            try
            {
                if (__instance == null || __instance.GetComponent<ClientOnTheServerChefSynchroniser>() == null)
                {
                    module.passedThrough++;
                    return true;
                }
                __result = null;
                module.skipped++;
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
            Deactivate();
            disposed = true;
        }
    }
}
