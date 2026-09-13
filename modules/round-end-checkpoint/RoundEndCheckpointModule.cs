using System;
using System.Collections.Generic;
using SuperchargedPatch.Authoring;

namespace SuperchargedPatch.Authoring.Modules
{
    // Thin explicitly loaded authority surface.  All timing-sensitive hooks and
    // lifecycle validation live in the frozen core so unloading this assembly
    // cannot strand Harmony patches or a copied native iterator implementation.
    public sealed class RoundEndCheckpointModule : IAuthoringModule
    {
        private bool disposed;
        public string Name { get { return "pristine-run-level-outro-checkpoint-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("RoundEndCheckpointModule");
            if (args == null) args = new Dictionary<string, object>();
            if (operation == "status")
            {
                if (args.Count != 0) throw new ArgumentException("status takes no arguments.");
                return NativeRoundEndLatch.Diagnostics();
            }
            if (operation == "cancel")
            {
                if (args.Count != 0) throw new ArgumentException("cancel takes no arguments.");
                return NativeRoundEndLatch.Cancel();
            }
            if (operation != "arm") throw new ArgumentException("Use status, arm or cancel.");
            if (args.Count != 1 || !args.ContainsKey("frame")) throw new ArgumentException("arm requires only frame.");
            long frame = Convert.ToInt64(args["frame"]);
            if (frame < 0 || frame > Int32.MaxValue) throw new ArgumentOutOfRangeException("frame");
            return NativeRoundEndLatch.Arm((int)frame);
        }

        public void Dispose()
        {
            if (disposed) return;
            if (NativeRoundEndLatch.IsActive)
                throw new InvalidOperationException("Cancel or restore the active round-end latch before unloading its authority module.");
            disposed = true;
        }
    }
}
