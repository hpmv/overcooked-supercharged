using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    internal sealed record VesselThrowReceipt(int Source, int SourceOrdinal, long SourceRegistration,
        int Thrower, int Receiver, int Vessel, int Home, int FlightFramesRemaining, JsonObject HomeGuard, RouteActionHandle OriginalAction);

    private static long ThrowRegistrationSequence(JsonNode state, int id)
    {
        if (state["entityRegistration"] is not JsonObject audit || !Flag(audit, "installed") ||
            Number(audit["errorCount"]) != 0 || Number(audit["roundDropped"]) != 0) return -1;
        var last = (audit["events"] as JsonArray)?.OfType<JsonObject>()
            .Where(e => IdOf(e["entity"] as JsonObject ?? new(), "entityId") == id)
            .OrderByDescending(e => e["sequence"]?.GetValue<long>() ?? -1).FirstOrDefault();
        return last?["kind"]?.ToString() == "register" ? last["sequence"]?.GetValue<long>() ?? -1 : -1;
    }

    // Read-only access to an existing throw's captured provenance. It cannot
    // manufacture a completed throw or substitute a different receiving pot.
    internal VesselThrowReceipt? ObserveInterceptedPotThrow(RouteActionHandle handle, JsonNode snapshot)
    {
        var state = snapshot["state"] ?? snapshot;
        var action = handle.Action;
        if (!ReferenceEquals(handle.Owner, this) || handle.IsDone || handle.Error is not null ||
            action.Spec["type"]?.ToString() != "throw" ||
            action.Stage is not ("throw-await-flight" or "throw-await-arrival") ||
            !throwStates.TryGetValue(action, out var data) || !data.FlightObserved ||
            data.SourceOrdinal < 0 || data.SourceRegistration < 0 ||
            data.VesselId == 0 || data.VesselAnchorId == 0 || data.BeforeVesselCounts.Count != 0 ||
            data.IngredientCounts.Count != 1 || data.IngredientCounts.GetValueOrDefault(CarnivalRecipes.Frankfurter.Id) != 1 ||
            action.Spec["nativeSupplyHome"] is not JsonObject home || home["role"]?.ToString() != "pot") return null;
        var holders = (state["chefs"] as JsonArray)?.OfType<JsonObject>().Where(c => IdOf(c, "heldEntityId") == data.ItemId).ToArray() ?? [];
        if (holders.Length != 1 || IdOf(holders[0], "entityId") == data.ThrowerId) return null;
        int receiver = IdOf(holders[0], "playerId");
        if (receiver is not (0 or 3)) return null;
        var receipt = new VesselThrowReceipt(data.ItemId, data.SourceOrdinal, data.SourceRegistration,
            data.ThrowerId, receiver, data.VesselId, data.VesselAnchorId,
            (action.Spec["flightTimeoutFrames"]?.GetValue<int>() ?? 360) - (action.Frames - data.ReleaseFrame), home.DeepClone().AsObject(), handle);
        try { _ = ValidateInterceptedPotThrow(handle, receipt, state); }
        catch (InvalidOperationException) { return null; }
        return receipt;
    }

    // True means native consumption and exact vessel delta are both observed.
    // The original Throw action still owns its own two-sample success barrier.
    internal bool ValidateInterceptedPotThrow(RouteActionHandle handle, VesselThrowReceipt receipt, JsonNode snapshot)
    {
        var state = snapshot["state"] ?? snapshot;
        bool activeProof = throwStates.TryGetValue(handle.Action, out var data);
        // CompleteThrow removes its mutable action state after the original
        // two-sample native barrier succeeds. The already-issued receipt pins
        // that exact handle and its immutable source/home identities; it does
        // not bypass that success path or create a replacement action.
        bool completedProof = !activeProof && handle.IsDone && handle.Action.Done && handle.Error is null;
        if (!ReferenceEquals(handle.Owner, this) || !ReferenceEquals(handle, receipt.OriginalAction) || handle.Error is not null ||
            !activeProof && !completedProof || activeProof && (!data!.FlightObserved ||
            data.ItemId != receipt.Source || data.SourceOrdinal != receipt.SourceOrdinal ||
            data.SourceRegistration != receipt.SourceRegistration || data.ThrowerId != receipt.Thrower ||
            data.VesselId != receipt.Vessel || data.VesselAnchorId != receipt.Home) ||
            IdOf(handle.Action.Spec, "targetEntityId") != receipt.Vessel ||
            IdOf(receipt.HomeGuard, "vessel") != receipt.Vessel || IdOf(receipt.HomeGuard, "home") != receipt.Home ||
            NativeSupplyHome.Invalid(state, receipt.HomeGuard) is not null)
            throw new InvalidOperationException("Intercepted pot supply lost its original throw or native vessel/home identity.");
        var vessel = activeProof ? RequireThrowVessel(data!, state) : RequireEntity(state, receipt.Vessel);
        var counts = ThrowVesselContents(vessel);
        bool matches = counts.Count == 1 && counts.GetValueOrDefault(CarnivalRecipes.Frankfurter.Id) == 1;
        var item = Entity(state, receipt.Source);
        bool consumed = item is null || item["active"]?.GetValue<bool>() == false;
        var holders = (state["chefs"] as JsonArray)?.OfType<JsonObject>().Where(c => IdOf(c, "heldEntityId") == receipt.Source).ToArray() ?? [];
        if (consumed)
        {
            if (holders.Length != 0 || !matches)
                throw new InvalidOperationException("Intercepted source disappeared without its original pot gaining exactly one native Frankfurter.");
            return true;
        }
        if (IdOf(item!, "observedOrdinal") != receipt.SourceOrdinal ||
            ThrowRegistrationSequence(state, receipt.Source) != receipt.SourceRegistration ||
            IdOf(item!, "previousThrowerEntityId") != receipt.Thrower || Flag(item!, "throwFlying") ||
            CarnivalRecipes.ClassifyEntity(item!).Food.IsRuined ||
            !CarnivalRecipes.ClassifyEntity(item!).Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }))
            throw new InvalidOperationException("Intercepted ingredient no longer has the captured source incarnation and native throw provenance.");
        if (holders.Any(c => IdOf(c, "playerId") != receipt.Receiver) || holders.Length > 1 ||
            holders.Length == 1 && ThrowVesselContents(vessel).Count != 0 ||
            holders.Length == 0 && !matches)
            throw new InvalidOperationException("Intercepted ingredient left its reserved catcher without the exact receiving-pot delta.");
        // Native insertion may update the vessel just before the loose source
        // is destroyed. No success is reported until destruction is observed.
        return false;
    }
}
