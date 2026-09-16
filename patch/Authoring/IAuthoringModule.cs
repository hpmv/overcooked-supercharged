using System.Collections.Generic;

namespace SuperchargedPatch.Authoring
{
    // Stable CLR2 contract. A module is constructed without installing hooks.
    // Activation is an explicit paused Invoke call. Dispose detaches only its
    // own callbacks; it must not change the native kitchen or run simulation.
    public interface IAuthoringModule
    {
        string Name { get; }
        int ApiVersion { get; }
        object Invoke(string operation, Dictionary<string, object> args);
        void Dispose();
    }
}
