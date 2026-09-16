using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace SuperchargedPatch.Authoring
{
    // The bridge supplies its existing main-thread, paused/input-fenced gate.
    // Assemblies stay loaded in this CLR; replacement only retires behavior.
    public sealed class AuthoringModuleHost
    {
        private sealed class Entry
        {
            internal IAuthoringModule Module;
            internal string Slot, Path, Hash, Assembly, Type, Name;
            internal long Generation;
            internal object Describe() { return new Dictionary<string, object> {
                {"slot",Slot},{"path",Path},{"sha256",Hash},{"assembly",Assembly},
                {"type",Type},{"name",Name},{"generation",Generation},{"apiVersion",1}}; }
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> loadedIdentities = new HashSet<string>(StringComparer.Ordinal);
        private readonly string directory;
        private readonly int mainThread;
        private readonly Action gate;
        public readonly string CoreSha256;
        private bool busy;
        private long sequence, loads, calls, unloads;
        private object lastOperation;

        public AuthoringModuleHost(string moduleDirectory, Action requirePausedBoundary)
        {
            directory = System.IO.Path.GetFullPath(moduleDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            mainThread = Thread.CurrentThread.ManagedThreadId;
            gate = requirePausedBoundary ?? throw new ArgumentNullException("requirePausedBoundary");
            CoreSha256 = Hash(File.ReadAllBytes(typeof(IAuthoringModule).Assembly.Location));
        }
        private void Begin()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Module operations require the Unity main thread.");
            if (busy) throw new InvalidOperationException("Nested authoring module operation rejected.");
            gate(); busy = true;
        }
        private static void ValidateSlot(string slot)
        {
            if (String.IsNullOrEmpty(slot) || slot.Length > 64) throw new ArgumentException("Explicit module slot required (up to64 characters).");
            foreach (char c in slot) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-') throw new ArgumentException("Slot must contain lowercase letters, digits or hyphens.");
        }
        private string Resolve(string requested)
        {
            if (String.IsNullOrEmpty(requested)) throw new ArgumentException("Module DLL path required.");
            string path = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(requested) ? requested : System.IO.Path.Combine(directory, requested));
            if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !String.Equals(System.IO.Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Module DLL must be below the isolated modules directory.");
            return path;
        }
        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public object Load(string slot, string requested, string typeName, string expectedSha256, string expectedCoreSha256 = null)
        {
            Begin(); IAuthoringModule candidate = null; bool installed = false;
            try
            {
                ValidateSlot(slot);
                if(expectedCoreSha256!=null && !String.Equals(CoreSha256,expectedCoreSha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Module was built against a different permanent core; nothing loaded.");
                string path = Resolve(requested);
                if (String.IsNullOrEmpty(typeName)) throw new ArgumentException("Explicit module entry type required.");
                if (expectedSha256 == null || expectedSha256.Length != 64) throw new ArgumentException("Expected SHA256 required.");
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length > 8 * 1024 * 1024) throw new ArgumentException("Missing module or DLL larger than8MiB.");
                byte[] bytes = File.ReadAllBytes(path); string digest = Hash(bytes);
                if (!String.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Module SHA256 differs; nothing loaded.");
                string identity = AssemblyName.GetAssemblyName(path).FullName;
                if (loadedIdentities.Contains(identity)) throw new InvalidOperationException("Assembly identity already loaded; compile a uniquely named revision.");
                // Reserve even failed loads: this runtime cannot unload them.
                loadedIdentities.Add(identity);
                Assembly assembly = Assembly.Load(bytes); loads++;
                if (assembly.FullName != identity) throw new InvalidOperationException("DLL identity changed while loading.");
                Type type = assembly.GetType(typeName, true);
                if (!typeof(IAuthoringModule).IsAssignableFrom(type) || type.IsAbstract) throw new InvalidOperationException("Entry type does not implement the stable authoring module contract.");
                candidate = (IAuthoringModule)Activator.CreateInstance(type);
                if (candidate.ApiVersion != 1 || String.IsNullOrEmpty(candidate.Name)) throw new InvalidOperationException("Unsupported module API or empty module name.");
                Entry old;
                if (entries.TryGetValue(slot, out old)) old.Module.Dispose();
                var entry = new Entry { Slot=slot,Path=path,Hash=digest,Assembly=identity,Type=typeName,Name=candidate.Name,Module=candidate,Generation=++sequence };
                entries[slot] = entry; installed = true;
                lastOperation = new Dictionary<string, object> {{"operation","load"},{"ok",true},{"module",entry.Describe()},{"activation","Explicit hot-call activate required; no callback installed by loader."}};
                return lastOperation;
            }
            catch(Exception e) { lastOperation = new Dictionary<string, object>{{"operation","load"},{"ok",false},{"error",e.ToString()}}; throw; }
            finally { if (!installed && candidate != null) { try { candidate.Dispose(); } catch {} } busy = false; }
        }
        public object Invoke(string slot, string operation, Dictionary<string, object> args)
        {
            Begin();
            try
            {
                ValidateSlot(slot); Entry entry;
                if (!entries.TryGetValue(slot, out entry)) throw new InvalidOperationException("No module in slot " + slot);
                if (String.IsNullOrEmpty(operation)) throw new ArgumentException("Module operation required.");
                calls++;
                object result = entry.Module.Invoke(operation, args ?? new Dictionary<string,object>());
                lastOperation = new Dictionary<string, object>{{"operation",operation},{"slot",slot},{"generation",entry.Generation},{"sha256",entry.Hash},{"ok",true}};
                return new Dictionary<string, object>{{"module",entry.Describe()},{"result",result}};
            }
            catch(Exception e) { lastOperation = new Dictionary<string,object>{{"operation",operation},{"slot",slot},{"ok",false},{"error",e.ToString()}}; throw; }
            finally { busy = false; }
        }
        public object Unload(string slot)
        {
            Begin();
            try
            {
                ValidateSlot(slot); Entry entry;
                if (!entries.TryGetValue(slot, out entry)) throw new InvalidOperationException("No module in slot " + slot);
                entry.Module.Dispose(); entries.Remove(slot); unloads++;
                lastOperation = new Dictionary<string,object>{{"operation","retire"},{"ok",true},{"module",entry.Describe()},{"assemblyStillLoaded",true}};
                return lastOperation;
            }
            finally { busy = false; }
        }
        public object Describe()
        {
            var active = new List<object>(); foreach (var e in entries.Values) active.Add(e.Describe());
            return new Dictionary<string,object>{{"apiVersion",1},{"moduleDirectory",directory},{"active",active},
                {"coreSha256",CoreSha256},
                {"assembliesLoaded",loads},{"invocations",calls},{"retired",unloads},{"lastOperation",lastOperation},
                {"classification","Live authoring code; record all module hashes. No assembly unloading or game-state rollback implied."}};
        }
    }
}
