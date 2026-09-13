using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record EmptyBowlSwap(int Near, int Far, int NearIndex, int FarIndex,
        int NearFlavor, int FarFlavor, JsonObject NearHome, JsonObject FarHome);

    // Assignments can outlive an empty bowl's previous batch. The native
    // supplier safely finishes the original near lane first; put the earliest
    // unissued batch there only before either empty lane has a commitment.
    private EmptyBowlSwap? InspectFifoEmptyBowls()
    {
        if (!options.FifoEmptyBowls || workers[1] is not null || Held(1) != 0 ||
            Region(1) is not ("upper-right" or "lower-left") || !TrafficControlsReady(1) ||
            !NeutralNativeInput(1) || IsCannonArrivalPassenger(1) || predictiveBakeryVisit is not null ||
            trafficYield is not null || IsBakeryParticipant(1) || IsSauceParticipant(1) ||
            IsEarlyOnionParticipant(1) || IsFryerParticipant(1) || IsPotParticipant(1)) return null;

        // Use topology captured at initialization, never a current-pose sort.
        var bowls = supplyHomes.Where(p => p.Value.Role == "bowl").ToArray();
        if (bowls.Length != 2) return null;
        int near = bowls.Where(p => Math.Abs(p.Value.HomePosition.X - 24) < .05).Select(p => p.Key).SingleOrDefault();
        int far = bowls.Where(p => Math.Abs(p.Value.HomePosition.X - 22.8) < .05).Select(p => p.Key).SingleOrDefault();
        if (near == 0 || far == 0 || near == far ||
            CapturedSupplyHomeGuard(near) is not { } nearHome || CapturedSupplyHomeGuard(far) is not { } farHome ||
            !EmptyOriginalBowl(near, nearHome) || !EmptyOriginalBowl(far, farHome)) return null;

        if (!bowlAssignments.TryGetValue(near, out int nearIndex) || !bowlAssignments.TryGetValue(far, out int farIndex) ||
            !bowlFlavors.TryGetValue(near, out int nearFlavor) || !bowlFlavors.TryGetValue(far, out int farFlavor) ||
            farIndex >= nearIndex || nearFlavor == farFlavor ||
            !UnissuedBowlIndex(nearIndex, nearFlavor) || !UnissuedBowlIndex(farIndex, farFlavor)) return null;
        int first = Pending().Where(p => IsDonut(p.Recipe) && !basketAssignments.Values.Contains(p.Index) && !plating.Contains(p.Index))
            .Select(p => p.Index).DefaultIfEmpty(-1).Min();
        if (farIndex != first) return null;

        int board = Board("upper-right", false), pass = Counter(25.2, -13.2);
        var identities = new HashSet<int> { near, far, I(nearHome["home"]), I(farHome["home"]), board, pass };
        if (!Free(identities.ToArray()) || !EmptyAttachment(board) || !EmptyAttachment(pass) ||
            bakeryLeases.Values.Any(l => identities.Contains(l.Bowl) || identities.Contains(l.Home)) ||
            nearReadyDoughTransfers.Values.Any(l => identities.Contains(l.Bowl)) ||
            preparedFlavorDeliveries.Values.Any(l => identities.Contains(l.Bowl)) ||
            sharedPantryChops.Values.Any(l => l.Supply.Supplier == 1) ||
            counterSupplies.Any(p => identities.Contains(p.Key) || identities.Contains(p.Value.Vessel))) return null;
        // An issued loose ingredient may be in flight or awaiting a central
        // receiver after its supplier Work ends. Preserve that existing intent.
        if (new[] { CarnivalRecipes.Flour, CarnivalRecipes.Egg, CarnivalRecipes.Chocolate, CarnivalRecipes.Raspberry }
            .Any(i => LooseIngredientCount(i.Id) != 0)) return null;

        var work = workers.OfType<Work>().Concat(cannonInterruptions.Values.Select(i => i.Original))
            .Concat(interceptedSupplies.Select(i => i.Original));
        if (safeServiceHeat is { } heat) work = work.Append(heat.Original);
        if (work.Any(w => w.Resources.Any(identities.Contains) || w.OwnedResources.Any(identities.Contains) ||
            w.Actions.Any(a => BowlActionReferences(a, identities)) ||
            w.Active is { } active && BowlActionReferences(active.Specification, identities))) return null;
        return new(near, far, nearIndex, farIndex, nearFlavor, farFlavor, nearHome, farHome);
    }

    private bool EmptyOriginalBowl(int bowl, JsonObject home)
    {
        var e = Entity(bowl);
        return bowlHomes.GetValueOrDefault(bowl) == I(home["home"]) &&
            Station(bowl)?.Role == "bowl" && e is not null && KitchenModel.Components(e).Contains("MixableContainer") &&
            e["mixingProgress"] is { } clock && double.IsFinite(N(clock)) && N(clock) == 0 &&
            e["mixingTime"] is { } duration && N(duration) == 12 &&
            e["composition"] is JsonObject food && food["type"]?.ToString() == "MixedCompositeAssembledNode" &&
            food["state"]?.ToString() == "Unmixed" && food["progress"] is { } progress && N(progress) == 0 &&
            food["children"] is JsonArray children && children.Count == 0 && !HeldByAnyone(bowl);
    }

    private bool UnissuedBowlIndex(int index, int flavor) => WithinOrdinaryWindow(index) &&
        IsDonut(recipes[index]) && !plating.Contains(index) && !basketAssignments.Values.Contains(index) &&
        bowlAssignments.Values.Count(i => i == index) == 1 &&
        recipes[index].RequiredInputs.Single(i => i == CarnivalRecipes.Chocolate || i == CarnivalRecipes.Raspberry).Id == flavor;

    private static bool BowlActionReferences(JsonNode? node, HashSet<int> identities)
    {
        if (node is JsonArray array) return array.Any(n => BowlActionReferences(n, identities));
        if (node is not JsonObject obj) return false;
        foreach (var (key, value) in obj)
        {
            if (key is "station" or "targetEntityId" or "vessel" or "home" or "bowl" or "mixer" &&
                int.TryParse(value?.ToString(), out int id) && identities.Contains(id)) return true;
            if (value is JsonObject or JsonArray && BowlActionReferences(value, identities)) return true;
        }
        return false;
    }

    private bool TryReprioritizeEmptyBowls()
    {
        if (InspectFifoEmptyBowls() is not { } swap) return false;
        // All validation precedes this synchronous metadata-only transaction.
        bowlAssignments[swap.Near] = swap.FarIndex; bowlAssignments[swap.Far] = swap.NearIndex;
        bowlFlavors[swap.Near] = swap.FarFlavor; bowlFlavors[swap.Far] = swap.NearFlavor;
        Log("fifoEmptyBowlsReassigned", new JsonObject { ["frame"] = Frame, ["supplierRegion"] = Region(1),
            ["nearBowl"] = swap.Near, ["farBowl"] = swap.Far, ["nearPreviousIndex"] = swap.NearIndex,
            ["farPreviousIndex"] = swap.FarIndex, ["nearIndex"] = swap.FarIndex, ["farIndex"] = swap.NearIndex,
            ["nearFlavor"] = swap.FarFlavor, ["farFlavor"] = swap.NearFlavor,
            ["nearNativeHome"] = swap.NearHome, ["farNativeHome"] = swap.FarHome,
            ["reason"] = "earliest pending unissued donut moved to original near lane; both native bowls empty/reset and all commitments absent" });
        return true;
    }
}
