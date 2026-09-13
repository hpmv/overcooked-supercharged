using Hpmv;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public sealed partial class HeadlessSession
{
    private TypedActionPlan actionPlan;
    private int actionStart;
    private string actionOutcome = "none", actionError;
    private bool actionCompleted;
    private Stopwatch actionWall;
    private JsonObject actionMappings;
    private readonly HashSet<int> authoredActionIds = new();
    private JsonObject nativeReloadDiscard;
    private void DiscardAuthoredActionsForNativeLoad()
    {
        var discarded = setup.sequences.NodeById.Keys.Where(authoredActionIds.Contains).Order().ToArray();
        nativeReloadDiscard = new JsonObject { ["source"] = "observed fresh native level-load message", ["discardedActionIds"] = JsonSerializerNode(discarded),
            ["previousErrors"] = JsonSerializerNode(errors.ToArray()), ["nativeStateChanged"] = false,
            ["scope"] = "Only this headless session's authored nodes are discarded before the original native-level reconstruction reset. Supplied setup graph definitions and prior trace/checkpoint artifacts remain unchanged." };
        foreach (var list in setup.sequences.Actions.Select((nodes, chef) => (nodes, chef)))
            for (int i = list.nodes.Count - 1; i >= 0; i--)
                if (authoredActionIds.Contains(list.nodes[i].Id)) setup.sequences.DeleteAction((list.chef, i));
        authoredActionIds.Clear(); actionPlan = null; actionMappings = null; actionOutcome = "none"; actionError = null; actionCompleted = false;
        errors.Clear(); // Prior-level errors are retained in the explicit receipt above.
    }
    private bool ActionsActive => actionOutcome is "running" or "awaiting-pause";
    private void StartActions(JsonObject request)
    {
        if (core.State != RealGameState.Paused || RawActive || ActionsActive) throw new InvalidOperationException("Actions require a settled paused session with no active segment/graph.");
        if (setup.sequences.NodeById.Values.Any(n => !n.Predictions.EndFrame.HasValue || n.Predictions.EndFrame >= core.simulator.Frame))
            throw new InvalidOperationException("Existing unfinished graph actions/claims must be handled first.");
        var mappings = registryAudit.RequireGraphMappings(core.simulator.entityIdToRecord, core.simulator.Frame);
        var plan = TypedActionPlan.Create(setup, core.simulator.entityIdToRecord, core.simulator.Frame, request);
        plan.Install(setup); actionMappings = mappings; actionPlan = plan; actionStart = core.simulator.Frame; actionOutcome = "running"; actionError = null; actionCompleted = false;
        foreach (var node in plan.Nodes) authoredActionIds.Add(node.Id);
        actionWall = Stopwatch.StartNew(); movementAction = null; stopAt = null; core.RequestResume();
    }
    private void ObserveActionFrame()
    {
        if (actionOutcome != "running" || core.State != RealGameState.Running || core.RequestPending) return;
        int frame = core.simulator.Frame;
        var expired = actionPlan.Nodes.FirstOrDefault(n => setup.sequences.NodeById[n.Id].Predictions is { StartFrame: int start, EndFrame: null } && frame - start >= n.TimeoutFrames);
        var timings = actionPlan.Nodes.Select(n => setup.sequences.NodeById[n.Id].Predictions).ToArray();
        bool completed = timings.All(t => t.EndFrame.HasValue && frame > t.EndFrame.Value);
        string error = expired is not null ? "Action timeout: " + expired.Key : frame - actionStart >= actionPlan.MaximumFrames ? "Whole graph frame deadline exceeded." :
            actionWall.Elapsed.TotalSeconds > 30 + actionPlan.MaximumFrames / 15.0 ? "Whole graph wall deadline exceeded." : null;
        if (!completed && error is null) return;
        actionCompleted = completed && error is null; actionError = error; actionOutcome = "awaiting-pause"; core.RequestPause();
    }
    private void ApplyActionExchange(InputData input)
    {
        if (!ActionsActive) return;
        if (core.State == RealGameState.Error) { InterruptActions("Original framework entered Error."); return; }
        if (core.State == RealGameState.Paused)
        {
            actionOutcome = actionOutcome == "awaiting-pause" ? actionCompleted ? "complete" : "failed" : "interrupted";
            if (actionOutcome == "interrupted") actionError = "An external pause interrupted the graph.";
            actionWall.Stop(); return;
        }
        if (actionOutcome != "awaiting-pause" || input.NextFrame != core.simulator.Frame + 1) return;
        // The bounded stop emits release/neutral through the same original
        // controller history. It does not delete nodes or reset spawn claims.
        foreach (var chef in setup.entityRecords.Chefs.Keys)
        {
            int frame = core.simulator.Frame; var state = setup.entityRecords.Chefs[chef][frame];
            var (next, actual) = state.ApplyInputAndAdvanceFrame(new DesiredControllerInput { primaryUp = state.primaryButtonDown, secondaryUp = state.secondaryButtonDown });
            next.primaryButtonDown = next.secondaryButtonDown = false;
            actual.primary = new ButtonOutput { justReleased = state.primaryButtonDown };
            actual.secondary = new ButtonOutput { justReleased = state.secondaryButtonDown };
            actual.dash = new ButtonOutput { justReleased = setup.inputHistory.FrameInputs[chef][frame].dash.isDown };
            setup.entityRecords.Chefs[chef].ChangeTo(next, input.NextFrame); setup.inputHistory.FrameInputs[chef].ChangeTo(actual, input.NextFrame);
            input.Input ??= new(); input.Input[chef.path.ids[0]] = new OneInputData { Pad = new() { X = 0, Y = 0 },
                Pickup = new() { Down = false, JustPressed = false, JustReleased = actual.primary.justReleased },
                Interact = new() { Down = false, JustPressed = false, JustReleased = actual.secondary.justReleased },
                Dash = new() { Down = false, JustPressed = false, JustReleased = actual.dash.justReleased } };
        }
    }
    private void InterruptActions(string reason)
    { if (ActionsActive) { actionOutcome = "interrupted"; actionError = reason; actionWall.Stop(); } }
    private void ClearActions(bool all = false)
    {
        if (core.State != RealGameState.Paused || core.RequestPending || ActionsActive) throw new InvalidOperationException("Clear requires settled paused completed/failed/interrupted graph.");
        if (all)
        {
            if (RawActive) throw new InvalidOperationException("Clear-all requires inactive fixed-input execution.");
            int frame = core.simulator.Frame;
            if (!discoveryOnly) registryAudit.RequireGraphMappings(core.simulator.entityIdToRecord, frame);
            var owned = setup.sequences.NodeById.Keys.ToHashSet();
            if (setup.entityRecords.GenAllEntities().Any(e => e.existed[frame] && (e.path.ids.Length != 1 || owned.Contains(e.spawnOwner[frame]))) ||
                core.simulator.entityIdToRecord.Values.Any(e => e.existed[frame] && e.path.ids.Length != 1))
                throw new InvalidOperationException("Clear-all requires a fixed-only native baseline; live spawned records/claims cannot be erased.");
            foreach (var list in setup.sequences.Actions.Select((nodes, chef) => (nodes, chef)))
                for (int i = list.nodes.Count - 1; i >= 0; i--) setup.sequences.DeleteAction((list.chef, i));
            core.simulator.ClearHistoryBeforeSimulation();
            actionPlan = null; actionOutcome = "cleared"; actionError = null; actionCompleted = false;
            movementAction = null; stopAt = null;
            return;
        }
        if (actionPlan is null) return;
        var pending = actionPlan.Nodes.Where(n => setup.sequences.NodeById.TryGetValue(n.Id, out var node) && (!node.Predictions.EndFrame.HasValue || node.Predictions.EndFrame >= core.simulator.Frame)).Select(n => n.Id).ToHashSet();
        if (setup.entityRecords.GenAllEntities().Any(e => e.existed[core.simulator.Frame] && pending.Contains(e.spawnOwner[core.simulator.Frame])))
            throw new InvalidOperationException("Cannot remove an unfinished action that already owns a live spawn claim.");
        foreach (var list in setup.sequences.Actions.Select((nodes, chef) => (nodes, chef)))
            for (int i = list.nodes.Count - 1; i >= 0; i--) if (pending.Contains(list.nodes[i].Id)) setup.sequences.DeleteAction((list.chef, i));
        // Completed original nodes and their real spawn claims remain intact.
        actionOutcome = "cleared";
    }
    private JsonObject ActionStatus() => new()
    {
        ["outcome"] = actionOutcome, ["active"] = ActionsActive, ["error"] = actionError,
        ["startFrame"] = actionPlan is null ? null : actionStart, ["maximumFrames"] = actionPlan?.MaximumFrames,
        ["mappingValidation"] = actionMappings?.DeepClone(),
        ["actions"] = actionPlan is null ? new JsonArray() : new JsonArray(actionPlan.Nodes.Select(n => {
            setup.sequences.NodeById.TryGetValue(n.Id, out var node);
            return (JsonNode)new JsonObject { ["id"] = n.Key, ["actionId"] = n.Id, ["chef"] = n.Chef.path.ids[0], ["type"] = n.Action.GetType().Name,
                ["startFrame"] = node?.Predictions.StartFrame, ["endFrame"] = node?.Predictions.EndFrame, ["timeoutFrames"] = n.TimeoutFrames,
                ["dependencies"] = new JsonArray(n.Dependencies.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
                ["resources"] = new JsonArray(n.Resources.Select(r => (JsonNode)JsonValue.Create(r)).ToArray()),
                ["observedTransfer"] = TransferStatus(n, node),
                ["preparationOnly"] = n.Action is InteractAction { Prepare: true } };
        }).ToArray()),
        ["completionMeaning"] = "Typed pickup/place require exact reconstructed native attachment pairs and observed primary release; prepare-primary only runs the original target preparation, with no transfer or spawn claim. Other actions retain original predicates. Final pause is observed. Recheck native target before a subsequent edge; consumption/combine, catch/arrival and score need separate native proof."
    };

    private JsonObject TransferStatus(TypedActionPlan.Node node, GameActionNode timing)
    {
        if (node.Action is not InteractAction { RequireObservedTransfer: true } action) return null;
        if (timing?.Predictions.StartFrame is not int start || start > core.simulator.Frame)
            return new() { ["required"] = true, ["state"] = "not-started" };
        int at = Math.Min(timing.Predictions.EndFrame ?? core.simulator.Frame, core.simulator.Frame);
        var proof = action.ObserveTransfer(new() { Frame = at, FrameWithinAction = at - start, Entities = setup.entityRecords });
        return new() { ["required"] = true, ["startFrame"] = start, ["observedAtFrame"] = at,
            ["bindingValid"] = proof.BindingValid, ["attachmentAccepted"] = proof.Accepted, ["pending"] = proof.Pending,
            ["primaryReleased"] = !setup.entityRecords.Chefs[node.Chef][at].PrimaryButtonDown,
            ["subject"] = proof.Subject is null ? null : JsonSerializerNode(proof.Subject.path.ids),
            ["source"] = proof.Source is null ? null : JsonSerializerNode(proof.Source.path.ids),
            ["item"] = proof.Item is null ? null : JsonSerializerNode(proof.Item.path.ids) };
    }
}
