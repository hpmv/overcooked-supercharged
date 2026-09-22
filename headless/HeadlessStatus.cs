using Hpmv;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public sealed partial class HeadlessSession
{
    /// <summary>Read-only polling state; no registry audit, entity or graph formatting.</summary>
    public JsonObject Status()
    {
        lock (gate)
        {
            int frame = core.simulator.Frame;
            return new()
            {
                ["ok"] = traceFailure is null, ["kind"] = "supercharged-headless-status", ["version"] = 1,
                ["connected"] = connected, ["traceFailure"] = traceFailure,
                ["state"] = core.State.ToString(), ["frame"] = frame, ["requestPending"] = core.RequestPending,
                ["exchanges"] = exchanges,
                ["resumePhaseMetadataVersion"] = RealGameConnector.ResumePhaseMetadataProtocolVersion,
                ["resumePhaseMetadataEmissions"] = core.ResumePhaseMetadataEmissions,
                ["freshLevelLoadObserved"] = freshLoadObserved,
                ["needsFreshLevelBaseline"] = !freshLoadObserved, ["lastEmpiricalFrame"] = setup.LastEmpiricalFrame,
                ["movementAction"] = movementAction,
                ["movementCompleted"] = movementAction is int id && setup.sequences.NodeById[id].Predictions.EndFrame.HasValue,
                ["invalidStateReason"] = setup.entityRecords.InvalidStateReason[frame],
                ["errors"] = new JsonArray(errors.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()),
                ["rawInput"] = RawStatus(),
                ["typedActions"] = new JsonObject
                {
                    ["outcome"] = actionOutcome, ["active"] = ActionsActive, ["error"] = actionError,
                    ["startFrame"] = actionPlan is null ? null : actionStart,
                    ["maximumFrames"] = actionPlan?.MaximumFrames, ["actionCount"] = actionPlan?.Nodes.Count ?? 0
                },
                ["qualification"] = "Polling status only; use full inspection and native evidence for state, mapping and task validation."
            };
        }
    }

    private JsonObject StatusCommand(JsonObject request)
    {
        foreach (string key in request.Select(p => p.Key))
            if (key is not ("command" or "full" or "development"))
                throw new ArgumentException("Status is read-only and does not accept " + key + ".");
        if (request["full"]?.GetValue<bool>() == true || request["development"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use inspect for full state; status has no development controls.");
        return Status();
    }
}
