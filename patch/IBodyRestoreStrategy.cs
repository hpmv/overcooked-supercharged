using System;

namespace SuperchargedPatch
{
    // Versioned authoring-only algorithm boundary. Capture/history ownership and
    // final postconditions remain in the permanent plugin assembly.
    public interface IBodyRestoreStrategy
    {
        string Name { get; }
        int ApiVersion { get; }
        void Restore(NativeBodyPoseCheckpoint.Snapshot[] saved);
    }
}
