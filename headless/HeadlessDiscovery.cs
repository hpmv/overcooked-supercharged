using Hpmv;

namespace Supercharged.Headless;

public sealed partial class HeadlessSession
{
    private readonly Dictionary<int, ItemData> discoveryItems = new();
    private readonly Dictionary<int, ChefSpecificData> discoveryChefs = new();
    private InputData ObserveDiscovery(OutputData output, bool freshLoad)
    {
        if (freshLoad) { discoveryItems.Clear(); discoveryChefs.Clear(); }
        foreach (var (id, delta) in output.Items)
        {
            if (!discoveryItems.TryGetValue(id, out var item)) discoveryItems[id] = item = new();
            if (delta.__isset.pos) item.Pos = delta.Pos?.DeepCopy();
            if (delta.__isset.rotation) item.Rotation = delta.Rotation?.DeepCopy();
            if (delta.__isset.velocity) item.Velocity = delta.Velocity?.DeepCopy();
            if (delta.__isset.angularVelocity) item.AngularVelocity = delta.AngularVelocity?.DeepCopy();
            if (delta.__isset.entityPathReference) item.EntityPathReference = delta.EntityPathReference?.DeepCopy();
        }
        foreach (var (id, chef) in output.Chefs) discoveryChefs[id] = chef.DeepCopy();
        // The raw observation frame is reported without running reconstruction
        // against an empty setup or inventing a native chef/prefab record.
        core.simulator.SetFrameAfterWarping(output.FrameNumber);
        core.State = output.NextFramePaused ? RealGameState.Paused : RealGameState.Running;
        var input = new InputData { NextFrame = output.FrameNumber, RequestPause = !output.NextFramePaused,
            PreventInvalidState = false, Input = new() };
        trace?.Exchange(output, input, output.NextFramePaused); Exchange?.Invoke(output, input);
        return input;
    }
}
