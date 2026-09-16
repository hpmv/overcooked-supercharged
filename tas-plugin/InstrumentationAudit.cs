using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Oc2Tas
{
    [Serializable]
    public sealed class HookEvidence
    {
        public string original, kind, handler, owner;
    }

    [Serializable]
    public sealed class LoadedPluginEvidence
    {
        public string identifier, version, assemblySha256;
    }

    [Serializable]
    public sealed class InstrumentationManifest
    {
        public int version = 1;
        public string identifier = "local.oc2tas.newbot";
        public string unityVersion, runtimeArchitecture, pluginSha256, gameAssemblySha256, executableSha256;
        public string pluginModuleVersionId, gameModuleVersionId;
        public string frameGate = "Unity WaitForEndOfFrame; ordinary Update/FixedUpdate/LateUpdate and native physics";
        public string clockPolicy = "60 Hz capture; native ClientTime/ServerTime algorithms with logical realtime source; native event delays retained";
        public string orderPolicy = "Native weighted generator; optional seed stream scoped to native RoundInstanceData identity";
        public string inputPolicy = "Four normal local registrations; logical axis/button wrappers retain native claims, held duration and control gates";
        public string focusPolicy = "Only emulated-device application-focus gate is bypassed";
        public string savePolicy = "PC/Steam managed save paths redirected into the workspace profile";
        public string hookEvidencePolicy = "Observed Harmony registrations owned by this plugin; not a substitute for behavioral validation";
        public HookEvidence[] hooks = new HookEvidence[0];
        public LoadedPluginEvidence[] loadedPlugins = new LoadedPluginEvidence[0];
        public bool inputHooksInstalled, clockHooksInstalled, startAlignmentHookInstalled, saveHooksInstalled, gameEventHooksInstalled;
    }

    [Serializable]
    public sealed class InstrumentationState
    {
        public string manifestSha256, error;
        public InstrumentationManifest manifest;
        public bool inputActive, logicalClockActive, isolateRecipeRandom, alignStartPhysics, nativePhysicsAutoSimulation;
        public int seed;
    }

    public static class InstrumentationAudit
    {
        private const string Owner = "local.oc2tas.newbot";
        private static InstrumentationManifest manifest;
        private static string manifestHash, error;

        public static InstrumentationState Capture()
        {
            if (manifest == null && error == null)
            {
                try { Build(); }
                catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            }
            return new InstrumentationState {
                manifest = manifest, manifestSha256 = manifestHash ?? "", error = error ?? "",
                inputActive = Inputs.Active, logicalClockActive = NativeTime.Active,
                isolateRecipeRandom = NativeTime.IsolateRecipeRandom, seed = NativeTime.Seed,
                alignStartPhysics = NativeTime.AlignStartPhysics, nativePhysicsAutoSimulation = Physics.autoSimulation
            };
        }

        private static void Build()
        {
            List<HookEvidence> hooks = new List<HookEvidence>();
            foreach (MethodBase original in Harmony.GetAllPatchedMethods())
            {
                Patches patches = Harmony.GetPatchInfo(original);
                if (patches == null) continue;
                Add(hooks, original, "prefix", patches.Prefixes);
                Add(hooks, original, "postfix", patches.Postfixes);
                Add(hooks, original, "transpiler", patches.Transpilers);
                Add(hooks, original, "finalizer", patches.Finalizers);
            }
            hooks.Sort(delegate(HookEvidence a, HookEvidence b) {
                return string.CompareOrdinal(a.original + "|" + a.kind + "|" + a.handler,
                                             b.original + "|" + b.kind + "|" + b.handler);
            });
            Assembly plugin = typeof(Plugin).Assembly, game = typeof(PlayerControls).Assembly;
            List<LoadedPluginEvidence> loaded = new List<LoadedPluginEvidence>();
            foreach (BepInEx.PluginInfo info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                loaded.Add(new LoadedPluginEvidence { identifier = info.Metadata.GUID,
                    version = info.Metadata.Version.ToString(), assemblySha256 = FileHash(info.Location) });
            loaded.Sort(delegate(LoadedPluginEvidence a, LoadedPluginEvidence b) { return string.CompareOrdinal(a.identifier, b.identifier); });
            InstrumentationManifest value = new InstrumentationManifest {
                unityVersion = Application.unityVersion, runtimeArchitecture = IntPtr.Size == 4 ? "x86 Mono" : "unexpected pointer size " + IntPtr.Size,
                pluginSha256 = FileHash(plugin.Location), gameAssemblySha256 = FileHash(game.Location),
                executableSha256 = FileHash(Path.Combine(BepInEx.Paths.GameRootPath, "Overcooked2.exe")),
                pluginModuleVersionId = plugin.ManifestModule.ModuleVersionId.ToString(),
                gameModuleVersionId = game.ManifestModule.ModuleVersionId.ToString(), hooks = hooks.ToArray(), loadedPlugins = loaded.ToArray(),
                inputHooksInstalled = Has(hooks, typeof(PlayerInputLookup), "GetButton", "GetButton") &&
                    Has(hooks, typeof(PlayerInputLookup), "GetValue", "GetValue") && Has(hooks, typeof(PlayerControls), "CanButtonBePressed", "PermitBackgroundDevice"),
                clockHooksInstalled = Has(hooks, typeof(ClientTime), "Update", "ReplaceRealtime") &&
                    Has(hooks, typeof(ClientTime), "Time", "ReplaceRealtime") && Has(hooks, typeof(ClientTime), "OnTimeSyncReceived", "ReplaceRealtime") &&
                    Has(hooks, typeof(ServerTime), "Update", "ReplaceRealtime"),
                startAlignmentHookInstalled = Has(hooks, typeof(ServerKitchenLoader), "Update", "GateKitchenStart"),
                saveHooksInstalled = SaveIsolation.IsInstalled,
                gameEventHooksInstalled = GameEvents.Installed
            };
            manifestHash = BytesHash(Encoding.UTF8.GetBytes(JsonText.Serialize(value)));
            manifest = value;
        }

        private static bool Has(List<HookEvidence> hooks, Type originalType, string method, string handler)
        {
            string prefix = originalType.FullName + "::" + method + "(";
            foreach (HookEvidence item in hooks)
                if (item.original.StartsWith(prefix, StringComparison.Ordinal) && item.handler.EndsWith("::" + handler, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Add(List<HookEvidence> result, MethodBase original, string kind, IEnumerable<Patch> patches)
        {
            StringBuilder signature = new StringBuilder(original.DeclaringType.FullName).Append("::").Append(original.Name).Append('(');
            ParameterInfo[] parameters = original.GetParameters();
            for (int i = 0; i < parameters.Length; i++) { if (i > 0) signature.Append(','); signature.Append(parameters[i].ParameterType.FullName); }
            signature.Append(')');
            foreach (Patch patch in patches)
                if (patch.owner == Owner)
                    result.Add(new HookEvidence { original = signature.ToString(), kind = kind, owner = patch.owner,
                        handler = patch.PatchMethod.DeclaringType.FullName + "::" + patch.PatchMethod.Name });
        }

        private static string FileHash(string path)
        { using (FileStream stream = File.OpenRead(path)) using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(stream)); }
        private static string BytesHash(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes)
        { StringBuilder result = new StringBuilder(bytes.Length * 2); foreach (byte b in bytes) result.Append(b.ToString("x2")); return result.ToString(); }
    }
}
