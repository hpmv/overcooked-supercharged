// Adapted from the independently authored Oc2Tas bridge in plugin/SaveIsolation.cs.
// Only the namespace changes; original session/save/transport semantics are retained.
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace SuperchargedPatch.Bridge
{
    // Install before the game's profile bootstrap. Every PC save, load and delete
    // goes through these managed paths; Unity's native persistentDataPath is untouched.
    public static class SaveIsolation
    {
        public static bool IsInstalled { get; private set; }
        public static string Root { get; private set; }

        public static void Install(Harmony h, string root)
        {
            if (h == null) throw new ArgumentNullException("h");
            string profile = Path.GetFullPath(Path.Combine(root, "profile"));
            if (IsInstalled)
            {
                if (!String.Equals(Root, profile, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Save isolation is already bound to another profile.");
                return;
            }
            MethodInfo pcDirectory = AccessTools.Method(typeof(PCSaveManager), "GetSaveDirectory");
            MethodInfo steamDirectory = AccessTools.Method(typeof(SteamSaveManager), "GetSaveDirectory");
            MethodInfo fileAddress = AccessTools.Method(typeof(PCSaveManager), "GetFileAddress");
            if (pcDirectory == null || steamDirectory == null || fileAddress == null)
                throw new MissingMethodException("Cannot safely isolate this game version's PC save paths.");
            Directory.CreateDirectory(profile);
            Root = profile;
            // GetFileAddress is an additional guard covering save/load/delete even
            // if an unexpected subclass overrides the virtual directory function.
            h.Patch(fileAddress, null, new HarmonyMethod(typeof(SaveIsolation), "AddressPostfix"));
            h.Patch(pcDirectory, new HarmonyMethod(typeof(SaveIsolation), "DirectoryPrefix"));
            if (steamDirectory != pcDirectory)
                h.Patch(steamDirectory, new HarmonyMethod(typeof(SaveIsolation), "DirectoryPrefix"));
            IsInstalled = true;
        }

        private static bool DirectoryPrefix(ref string __result)
        {
            if (String.IsNullOrEmpty(Root)) throw new InvalidOperationException("TAS profile path is unavailable.");
            __result = Root.Replace('\\', '/') + "/";
            return false;
        }

        private static void AddressPostfix(ref string __result)
        {
            if (String.IsNullOrEmpty(Root)) throw new InvalidOperationException("TAS profile path is unavailable.");
            string name = Path.GetFileName(__result);
            if (String.IsNullOrEmpty(name) || !name.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unrecognized save filename; refusing an unisolated save operation.");
            // PCSaveManager locates the parent with LastIndexOf('/').
            __result = Path.Combine(Root, name).Replace('\\', '/');
        }
    }
}
