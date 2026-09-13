using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private string lastBakeryDepartureHold = "";
    private sealed record BakeryPortalArrival(Point2 Position, int Receiver, int ReceiverOrdinal, int Frame);
    private BakeryPortalArrival? bakeryPortalArrival;
    private JsonObject? lastMixerReturnEvaluation;

    private void ObserveBakeryPortalArrival(int player, JsonObject action)
    {
        if (player != 1 || action["type"]?.ToString() != "portal" || action["destinationRegion"]?.ToString() != "upper-right" ||
            Region(1) != "upper-right" || !TrafficControlsReady(1) || N(chefs[1]["position"]?["y"]) > .9) return;
        var edge = model.Transitions.SingleOrDefault(e => e.Kind == "portal" && e.Verified && e.FromRegion == "lower-left" && e.ToRegion == "upper-right");
        if (edge?.DestinationKey is null) return;
        int receiver = model.Resolve(edge.DestinationKey).EntityId;
        if (Entity(receiver)?["observedOrdinal"] is null || Position(1).Distance(Station(receiver)!.Position) > 2) return;
        bakeryPortalArrival = new(Position(1), receiver, I(Entity(receiver)?["observedOrdinal"]), Frame);
        Log("nativeBakeryPortalArrivalMeasured", JsonSerializer.SerializeToNode(bakeryPortalArrival)!.AsObject());
    }

    private int[] ActiveBakeryCommitments() => Stations("bowl").Select(s => s.EntityId).Where(bowl =>
        bowlAssignments.TryGetValue(bowl, out int index) && index >= delivered && index < recipes.Length && IsDonut(recipes[index]) &&
        bowlHomes.TryGetValue(bowl, out int home) && Attached(home) == bowl && !HeldByAnyone(bowl) &&
        Entity(home) is { } station && KitchenModel.Components(station).Contains("MixingStation") &&
        Entity(bowl)?["mixingProgress"] is { } progress && double.IsFinite(N(progress)) && N(progress) >= 0 && N(Entity(bowl)?["mixingTime"]) == 12 &&
        (CompatiblePartialMixer(bowl, out _, out _, out _) || EmptyFood(bowl) && counterSupplies.Values.Any(a =>
            a.Vessel == bowl && recipes[index].RequiredInputs.Any(i => i.Id == a.Ingredient))))
        .OrderByDescending(bowl => N(Entity(bowl)?["mixingProgress"])).ThenBy(bowl => bowl).ToArray();

    // Called only where the existing policy would board the washing cannon.
    // A native partial batch and an already-issued raw address are promises
    // to finish its ingredient kit before abandoning the unique pantry chef.
    private bool TryFinishActiveBakeryBeforeWash()
    {
        if (workers[1] is not null || Held(1) != 0 || Region(1) != "upper-right" || !TrafficControlsReady(1) || NativeCannonFlight(1)) return false;
        int bowl = ActiveBakeryCommitments().FirstOrDefault();
        if (bowl == 0) { lastBakeryDepartureHold = ""; return false; }
        int index = bowlAssignments[bowl]; int[] actual = Food(bowl).IngredientIds;
        var missing = recipes[index].RequiredInputs.Where(i => !actual.Contains(i.Id)).OrderBy(i => i.ChopSeconds > 0 ? 1 : 0).ThenBy(i => i == CarnivalRecipes.Flour ? 0 : 1).ToArray();
        string reason = "await-exact-native-ingredient-acceptance";
        if (Free(bowl) && !counterSupplies.Values.Any(a => a.Vessel == bowl) && missing.FirstOrDefault() is { } ingredient)
        {
            if (ingredient == CarnivalRecipes.Flour || ingredient == CarnivalRecipes.Egg)
            {
                if (Supply(1, ingredient, bowl, true)) reason = "supply-required-raw-before-washing";
            }
            else if (ingredient == CarnivalRecipes.Chocolate || ingredient == CarnivalRecipes.Raspberry)
            {
                int board = Board("upper-right", false);
                if (LooseIngredientCount(ingredient.Id) == 0 && EmptyAttachment(board) && Free(board) &&
                    (TrySupplyPreparedFlavor(bowl, ingredient) || Supply(1, ingredient, board, false))) reason = "prepare-required-flavor-before-washing";
            }
        }
        string signature = bowl + "/" + reason + "/" + string.Join(",", missing.Select(i => i.Id));
        if (signature != lastBakeryDepartureHold)
        {
            Log("nativeBakeryDepartureDeferred", new JsonObject { ["frame"] = Frame, ["bowl"] = bowl, ["orderIndex"] = index,
                ["nativeMixProgress"] = Entity(bowl)?["mixingProgress"]?.DeepClone(), ["reason"] = reason,
                ["missingIngredientIds"] = JsonSerializer.SerializeToNode(missing.Select(i => i.Id)),
                ["issuedAddresses"] = JsonSerializer.SerializeToNode(counterSupplies.Where(p => p.Value.Vessel == bowl).Select(p => new { source = p.Key, vessel = p.Value.Vessel, ingredient = p.Value.Ingredient })) });
            lastBakeryDepartureHold = signature;
        }
        return true;
    }

    private JsonObject? ActiveMixerReturnEvidence()
    {
        lastMixerReturnEvaluation = new() { ["frame"] = Frame, ["reason"] = "supplier or measured native portal unavailable" };
        if (workers[1] is not null || Held(1) != 0 || Region(1) != "lower-left" || !TrafficControlsReady(1) || NativeCannonFlight(1)) return null;
        var edge = model.Transitions.SingleOrDefault(e => e.Kind == "portal" && e.Verified && e.FromRegion == "lower-left" && e.ToRegion == "upper-right");
        if (edge?.DestinationKey is null || bakeryPortalArrival is not { } measured || measured.Frame >= Frame ||
            model.Resolve(edge.DestinationKey).EntityId != measured.Receiver || !SameObservedEntity(measured.Receiver, measured.ReceiverOrdinal)) return null;
        // The telemetry teleport point is the animation start above the wall.
        // Budget from this round's observed controlled native arrival instead.
        Point2 arrival = measured.Position;
        double speed = N(chefs[1]["runSpeed"]) * N(chefs[1]["surfaceSpeedMultiplier"]);
        if (!(speed > 0) || !double.IsFinite(speed)) return null;
        var portalPath = Navigation.ToStation(model, Position(1), model.Resolve(edge.SourceKey), TrafficObstacles(1));
        lastMixerReturnEvaluation["portalPath"] = JsonSerializer.SerializeToNode(portalPath);
        if (!portalPath.Success) return null;
        foreach (int bowl in ActiveBakeryCommitments())
        {
            if (!CompatiblePartialMixer(bowl, out int index, out int[] actual, out _)) continue;
            double progress = N(Entity(bowl)?["mixingProgress"]);
            var ingredient = recipes[index].RequiredInputs.Where(i => !actual.Contains(i.Id) &&
                !counterSupplies.Values.Any(a => a.Vessel == bowl && a.Ingredient == i.Id) &&
                (i == CarnivalRecipes.Flour || i == CarnivalRecipes.Egg || LooseIngredientCount(i.Id) == 0))
                .OrderBy(i => i.ChopSeconds > 0 ? 1 : 0).ThenBy(i => i == CarnivalRecipes.Flour ? 0 : 1).FirstOrDefault();
            if (ingredient is null || !Free(bowl)) continue;
            int crate = model.Stations.Single(s => s.Ingredient == ingredient.Name).EntityId;
            int handoff = ingredient.ChopSeconds > 0 ? Board("upper-right", false) : Counter(25.2, -13.2);
            if (!Free(crate, handoff) || !EmptyAttachment(handoff)) continue;
            var pickup = Navigation.ToStation(model, arrival, Station(crate)!, TrafficObstacles(1));
            lastMixerReturnEvaluation["bowl"] = bowl; lastMixerReturnEvaluation["pickupPath"] = JsonSerializer.SerializeToNode(pickup);
            if (!pickup.Success) continue;
            var staging = Navigation.ToStation(model, pickup.Points[^1], Station(handoff)!, TrafficObstacles(1));
            lastMixerReturnEvaluation["stagingPath"] = JsonSerializer.SerializeToNode(staging);
            if (!staging.Success) continue;
            double supplier = portalPath.Length / speed + 2 + (pickup.Length + staging.Length) / speed + 2 + ingredient.ChopSeconds;
            var receivers = new[] { 0, 3 }.Where(HeatCookAvailable).Select(player =>
            {
                var first = Navigation.ToStation(model, Position(player), Station(handoff)!, TrafficObstacles(player));
                var second = first.Success ? Navigation.ToStation(model, first.Points[^1], Station(bowl)!, TrafficObstacles(player)) : first;
                double run = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
                double seconds = first.Success && second.Success && run > 0 && double.IsFinite(run) ? (first.Length + second.Length) / run + 2 : double.PositiveInfinity;
                return (Player: player, Seconds: seconds);
            }).OrderBy(p => p.Seconds).ToArray();
            lastMixerReturnEvaluation["supplierSeconds"] = supplier; lastMixerReturnEvaluation["remainingGuardSeconds"] = 21 - progress;
            lastMixerReturnEvaluation["receiverSeconds"] = JsonSerializer.SerializeToNode(receivers.Select(r => new { player = r.Player, seconds = double.IsFinite(r.Seconds) ? (double?)r.Seconds : null }));
            if (receivers.Length == 0 || !double.IsFinite(receivers[0].Seconds) || supplier + receivers[0].Seconds >= 21 - progress) continue;
            return new JsonObject { ["frame"] = Frame, ["bowl"] = bowl, ["orderIndex"] = index, ["ingredient"] = ingredient.Id,
                ["nativeMixProgress"] = progress, ["remainingGuardSeconds"] = 21 - progress,
                ["estimatedPortalSupplyAndRelaySeconds"] = supplier + receivers[0].Seconds, ["availableReceiver"] = receivers[0].Player,
                ["portalSource"] = model.Resolve(edge.SourceKey).EntityId, ["portalDestination"] = model.Resolve(edge.DestinationKey!).EntityId,
                ["previousNativeArrival"] = JsonSerializer.SerializeToNode(measured),
                ["cleanPassItem"] = Attached(Counter(15.6, -20.4)), ["nativeDryingCount"] = Entity(Single("drying"))?["plateCount"]?.DeepClone(),
                ["nativeSinkCount"] = Entity(Single("sink"))?["plateCount"]?.DeepClone(),
                ["reason"] = "Active native partial mixer needs the unique pantry supplier before additional washing; stored native plates remain untouched" };
        }
        return null;
    }

    private bool TryReturnForActiveMixer()
    {
        var evidence = ActiveMixerReturnEvidence();
        if (evidence is null || !Start(1, "active-mixer-return-to-bakery", [Portal("upper-right")], [])) return false;
        Log("nativeActiveMixerSupplierReturn", evidence); return true;
    }
}
