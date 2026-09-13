using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record HeatObligation(string Kind, int Vessel, int Home, int Target, double Progress, double Guard, double ReadyAt)
    { public double Remaining => Guard - Progress; }
    private readonly HashSet<int> heatSafetyBlocked = [];
    private string lastHeatBlock = "";
    private bool HeatSafetyBlocks(int player) => heatSafetyBlocked.Contains(player);
    private bool HeatCookAvailable(int player) => player is 0 or 3 && workers[player] is null && Held(player) == 0 && Region(player) == "center" &&
        !IsSauceParticipant(player) && !IsEarlyOnionParticipant(player) && !IsBakeryParticipant(player) && !IsFryerParticipant(player) && !IsPotParticipant(player) &&
        !cannonInterruptions.ContainsKey(player) && trafficYield?.Helper != player && trafficYield?.Winner != player &&
        B(chefs[player]["controlsEnabled"]) && !NativeCannonFlight(player);
    private bool HeatHasOwner(int vessel) => workers.Any(w => w is not null && w.OwnedResources.Contains(vessel)) ||
        EarlyOnionLeases().Any(l => l.Pan == vessel && l.Owner >= 0) || bakeryLeases.Values.Any(l => l.Bowl == vessel && l.Owner >= 0) ||
        fryerRescues.Values.Any(l => l.Basket == vessel && l.Owner >= 0) || PotHeatHasOwner(vessel) ||
        safeServiceHeat?.Original.OwnedResources.Contains(vessel) == true;
    private bool PotHeatHasOwner(int vessel) => potRescues.TryGetValue(vessel, out var lease) && lease.Owner >= 0;

    private IEnumerable<HeatObligation> NativeHeatObligations()
    {
        foreach (string role in new[] { "pot", "basket", "pan", "bowl" })
        foreach (var station in Stations(role).OrderBy(s => s.EntityId))
        {
            int id = station.EntityId, home = AttachmentParent(id); var entity = Entity(id); bool mixing = role == "bowl";
            if (entity is null || !B(entity["active"]) || EmptyFood(id) || home == 0 ||
                (mixing ? Entity(home) is not { } mixer || !KitchenModel.Components(mixer).Contains("MixingStation") : !NativeCookingStation(Entity(home)))) continue;
            double duration = N(entity[mixing ? "mixingTime" : "cookingTime"]), progress = N(entity[mixing ? "mixingProgress" : "cookingProgress"]);
            double expected = role == "basket" ? 10 : 12, guard = role == "pot" ? 23 : role == "basket" ? 19 : 21;
            if (duration != expected || progress < 0) throw new InvalidOperationException("Native heat arbitration observed an unsupported vessel clock: " + id);
            if (progress >= guard) throw new TimeoutException("Native " + role + " " + id + " remained on its processing station beyond the " + guard + "-second safety deadline.");
            if (progress < (role == "pot" ? 11 : 9) || HeatHasOwner(id)) continue;
            string kind = role; int target = home;
            if (mixing)
            {
                int assigned = bowlAssignments.GetValueOrDefault(id, -1);
                if (assigned < 0) kind = "unassigned-mixer";
                else if (!AssignedDoughIngredients(id, assigned))
                {
                    kind = "partial-mixer-flavor";
                    int board = ExactMissingMixerSupply(id);
                    if (board != 0) kind = "partial-mixer-ingredient";
                    else board = ExactMissingFlavorBoard(id);
                    if (board == 0) { board = ExactRawFlavorBoard(id); if (board != 0) kind = "partial-mixer-chop"; }
                    if (board != 0) target = board;
                }
            }
            else if (role == "pan" && !EarlyOnionLeases().Any(l => l.Pan == id))
            {
                int source = OrdinaryOnionHarvestSource(id);
                if (source != 0) target = source;
            }
            yield return new(kind, id, home, target, progress, guard, duration);
        }
    }

    private JsonObject HeatDescription(HeatObligation heat) => new() { ["kind"] = heat.Kind, ["vessel"] = heat.Vessel, ["home"] = heat.Home,
        ["target"] = heat.Target, ["nativeProgress"] = heat.Progress, ["guardSeconds"] = heat.Guard, ["remainingSeconds"] = heat.Remaining,
        ["observedOrdinal"] = Entity(heat.Vessel)?["observedOrdinal"]?.DeepClone() };

    private void DispatchNativeHeatSafety()
    {
        heatSafetyBlocked.Clear();
        AssignBowls();
        // Only new assignments are selected here. Existing Work/Active objects
        // and their owned leases never move, pause or restart in this arbiter.
        while (true)
        {
            var available = new[] { 0, 3 }.Where(HeatCookAvailable).ToArray();
            var pending = NativeHeatObligations().OrderBy(h => h.Remaining).ThenBy(h => h.Vessel).ToArray();
            if (available.Length == 0 || pending.Length == 0) { lastHeatBlock = ""; return; }
            if (TryServeBeforeSafeHeat(available, pending)) continue;
            var next = pending[0];
            var choices = available.Select(player =>
            {
                var path = HeatInteractionPath(player, next);
                double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
                double seconds = path.Success && speed > 0 && double.IsFinite(speed) ? path.Length / speed + 2 : double.PositiveInfinity;
                // A native cook/mix wait may precede detachment. A partial
                // flavor insertion has no requirement to wait for Mixed.
                if (!next.Kind.StartsWith("partial-mixer", StringComparison.Ordinal)) seconds += Math.Max(0, next.ReadyAt - next.Progress);
                if (next.Kind == "partial-mixer-chop" && bowlFlavors.TryGetValue(next.Vessel, out int flavor)) seconds += CarnivalRecipes.Ingredients[flavor].ChopSeconds;
                return (Player: player, Path: path, Seconds: seconds, Slack: next.Remaining - seconds);
            }).OrderBy(c => c.Seconds).ThenBy(c => c.Player).ToArray();
            bool started = false;
            foreach (var choice in choices.Where(c => c.Path.Success && c.Slack > 0))
            {
                if (!TryStartHeatObligation(choice.Player, next)) continue;
                var e = HeatDescription(next); e["frame"] = Frame; e["player"] = choice.Player;
                e["approachPath"] = JsonSerializer.SerializeToNode(choice.Path); e["estimatedInteractionSeconds"] = choice.Seconds;
                e["estimatedSlackSeconds"] = choice.Slack; e["orderedPendingVessels"] = JsonSerializer.SerializeToNode(pending.Select(h => h.Vessel));
                Log("nativeHeatSafetyDispatched", e); started = true; break;
            }
            if (started) continue;
            // An unhandled earlier deadline must not accidentally fall through
            // to onion loading, another pot refill, chopping or distant plates.
            foreach (int player in available) heatSafetyBlocked.Add(player);
            string signature = next.Kind + "/" + next.Vessel + "/" + string.Join(",", available);
            if (signature != lastHeatBlock)
            {
                var e = HeatDescription(next); e["frame"] = Frame; e["blockedPlayers"] = JsonSerializer.SerializeToNode(available);
                e["reason"] = "earliest native heat obligation lacks a feasible exact action, lease, or approach budget";
                e["candidateApproaches"] = JsonSerializer.SerializeToNode(choices.Select(c => new { player = c.Player, path = c.Path, seconds = double.IsFinite(c.Seconds) ? c.Seconds : (double?)null }));
                Log("nativeHeatSafetyBlockedElectives", e); lastHeatBlock = signature;
            }
            return;
        }
    }

    private bool TryStartHeatObligation(int player, HeatObligation heat)
    {
        if (!HeatCookAvailable(player)) return false;
        return heat.Kind switch
        {
            "pot" => TryRescuePot(player, heat.Vessel),
            "basket" => TryRescueFryer(player, heat.Vessel),
            "pan" => EarlyOnionLeases().FirstOrDefault(l => l.Pan == heat.Vessel) is { } pan
                ? TryAdmitEarlyOnionRescue(pan, player) : AddUnplatedOnions(player, heat.Vessel) || TryAdoptDeadlineOnion(player, heat.Vessel),
            "partial-mixer-flavor" => TryAddDeadlineFlavor(player, heat.Vessel),
            "partial-mixer-ingredient" => TryAddDeadlineMixerSupply(player, heat.Vessel),
            "partial-mixer-chop" => TryChopDeadlineFlavor(player, heat.Vessel),
            "bowl" => bakeryLeases.TryGetValue(heat.Vessel, out var bakery) ? TryAdmitBakeryParking(bakery, player) :
                PrepareDonut(player, heat.Vessel) || TryParkOrdinaryCompleteDough(player, heat.Vessel),
            _ => false
        };
    }

    private NavigationPath HeatInteractionPath(int player, HeatObligation heat)
    {
        var target = Station(heat.Target);
        if (target is null) return new(false, "Unknown heat interaction station", null, [], 0, 0, []);
        var obstacles = TrafficObstacles(player);
        var first = Navigation.ToStation(model, Position(player), target, obstacles);
        bool twoLegs = heat.Kind.StartsWith("partial-mixer", StringComparison.Ordinal) && heat.Target != heat.Home ||
            heat.Kind == "pan" && heat.Target != heat.Home;
        if (!first.Success || !twoLegs) return first;
        var destination = Station(heat.Vessel);
        if (destination is null) return first with { Success = false, Error = "Heat-changing destination is not observed" };
        var second = Navigation.ToStation(model, first.Points[^1], destination, obstacles);
        return new(first.Success && second.Success, second.Error, first.Region, first.Points.Concat(second.Points.Skip(1)).ToArray(),
            first.Length + second.Length, first.ExpandedNodes + second.ExpandedNodes, first.RequiredTransitions.Concat(second.RequiredTransitions).ToArray());
    }

    private int OrdinaryOnionHarvestSource(int pan)
    {
        if (!Free(pan)) return 0;
        foreach (var (index, recipe) in Pending().Where(p => p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) && !HasEarlyOnionLease(p.Index)))
        {
            int food = mealFoods.GetValueOrDefault(index), source = AttachmentParent(food);
            if (food != 0 && source != 0 && Free(food, source) && !Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id)) return source;
        }
        return 0;
    }

    private bool TryAdoptDeadlineOnion(int player, int pan)
    {
        // Ordinary onion cooking normally starts after a matching plain base
        // exists, but that base may be owned elsewhere at the safety boundary.
        // The already proven whole-pan parking route removes that dependency.
        // Adoption does not load new food or require optional early cooking.
        int home = AttachmentParent(pan);
        if (!HeatCookAvailable(player) || !PureOnion(pan) || !Free(pan, home) ||
            EarlyOnionLeases().Any(l => l.Pan == pan) || EarlyOnionLeases().Count() >= 2) return false;
        int index = Pending().Where(p => p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) && !HasEarlyOnionLease(p.Index) &&
                (!mealFoods.TryGetValue(p.Index, out int food) || !Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id)))
            .Select(p => p.Index).DefaultIfEmpty(-1).First();
        int counter = ParallelOnionParking(player, pan, home);
        if (index < 0 || counter == 0) return false;
        var lease = new EarlyOnionLease(index, pan, home, counter, I(Entity(pan)?["observedOrdinal"]), -1, Frame)
            { Phase = EarlyOnionPhase.Heating, PhaseFrame = Frame };
        RegisterParallelOnion(lease);
        if (!TryAdmitEarlyOnionRescue(lease, player))
            throw new InvalidOperationException("Deadline onion adoption lost its checked available rescue chef.");
        Log("ordinaryOnionOffheatSafetyLease", EarlyOnionStatus(lease));
        return true;
    }

    private bool CanStageExactPotHarvestOnSource(int player, int pot, int board, int food)
    {
        // This temporary output is available only to the selected urgent
        // native pot. It reuses the board emptied by this exact bun pickup;
        // normal future production still requires ordinary storage space.
        return HeatCookAvailable(player) && PendingPotRescues().Contains(pot) &&
            Attached(board) == food && IsLooseChoppedBun(food) && Free(board, food, pot) &&
            Station(board) is { } source && source.Regions.Contains("center") &&
            KitchenModel.Components(Entity(board)!).Contains("Workstation") &&
            Entity(board)?["observedOrdinal"] is not null && Entity(food)?["observedOrdinal"] is not null &&
            DirectPotHarvestHasTime(player, pot);
    }

    private int ExactMissingFlavorBoard(int bowl)
    {
        int index = bowlAssignments.GetValueOrDefault(bowl, -1);
        if (index < 0 || index >= recipes.Length || !IsDonut(recipes[index]) || !bowlFlavors.TryGetValue(bowl, out int flavor)) return 0;
        if (!CompatiblePartialMixer(bowl, out _, out var actual, out _) || actual.Contains(flavor)) return 0;
        return Stations("chop").Where(s => Chopped(Attached(s.EntityId), flavor)).OrderBy(s => s.EntityId).Select(s => s.EntityId).FirstOrDefault();
    }
    private int ExactRawFlavorBoard(int bowl)
    {
        int index = bowlAssignments.GetValueOrDefault(bowl, -1);
        if (index < 0 || index >= recipes.Length || !IsDonut(recipes[index]) || !bowlFlavors.TryGetValue(bowl, out int flavor)) return 0;
        if (!CompatiblePartialMixer(bowl, out _, out var actual, out _) || actual.Contains(flavor)) return 0;
        return Stations("chop").Where(s => Entity(Attached(s.EntityId)) is { } raw && B(raw["active"]) && N(raw["workProgress"]) >= 0 &&
            KitchenModel.Components(raw).Contains("ServerWorkableItem") &&
            CarnivalRecipes.ClassifyEntity(raw).Food is { Kind: FoodNodeKind.Ingredient, Preparation: FoodPreparation.Raw } food &&
            food.IngredientIds.SequenceEqual(new[] { flavor })).OrderBy(s => s.EntityId).Select(s => s.EntityId).FirstOrDefault();
    }
    private bool TryChopDeadlineFlavor(int player, int bowl)
    {
        int board = ExactRawFlavorBoard(bowl), raw = Attached(board);
        if (board == 0 || raw == 0 || !Free(board, raw) || !HeatCookAvailable(player)) return false;
        int flavor = bowlFlavors[bowl], ordinal = I(Entity(board)?["observedOrdinal"]);
        return Start(player, "heat-deadline-chop-exact-flavor-" + (bowlAssignments[bowl] + 1), [BakeryAction("chop", board)], [board, raw, bowl], () =>
        {
            if (I(Entity(board)?["observedOrdinal"]) != ordinal || !Chopped(Attached(board), flavor) || Held(player) != 0)
                throw new InvalidOperationException("Heat prerequisite chopping did not produce its exact native prepared flavor.");
            Log("nativeHeatFlavorChopped", new JsonObject { ["frame"] = Frame, ["bowl"] = bowl, ["board"] = board,
                ["preparedIngredient"] = Attached(board), ["nativeMixProgressStillAdvancing"] = Entity(bowl)?["mixingProgress"]?.DeepClone() });
        });
    }
    private bool TryAddDeadlineFlavor(int player, int bowl)
    {
        int board = ExactMissingFlavorBoard(bowl), item = Attached(board), home = AttachmentParent(bowl);
        if (board == 0 || item == 0 || !Free(bowl, board, item) || !HeatCookAvailable(player)) return false;
        int index = bowlAssignments[bowl], ordinal = I(Entity(bowl)?["observedOrdinal"]);
        int homeOrdinal = I(Entity(home)?["observedOrdinal"]), boardOrdinal = I(Entity(board)?["observedOrdinal"]);
        int[] expected = Food(bowl).IngredientIds.Append(bowlFlavors[bowl]).Order().ToArray();
        double before = N(Entity(bowl)?["mixingProgress"]);
        return Start(player, "heat-deadline-add-exact-flavor-" + (index + 1), [BakeryAction("take", board), BakeryAction("place", bowl)], [bowl, board, item], () =>
        {
            if (I(Entity(bowl)?["observedOrdinal"]) != ordinal || I(Entity(home)?["observedOrdinal"]) != homeOrdinal ||
                I(Entity(board)?["observedOrdinal"]) != boardOrdinal || AttachmentParent(bowl) != home || Held(player) != 0 ||
                Attached(board) != 0 || bowlAssignments.GetValueOrDefault(bowl, -1) != index || Food(bowl).IsRuined ||
                !Food(bowl).IngredientIds.Order().SequenceEqual(expected))
                throw new InvalidOperationException("Deadline flavor transfer did not preserve its exact identities and native one-flavor delta.");
            Log("nativeHeatFlavorAccepted", new JsonObject { ["frame"] = Frame, ["bowl"] = bowl, ["orderIndex"] = index,
                ["previousObservedMixProgress"] = before, ["currentNativeMixProgress"] = Entity(bowl)?["mixingProgress"]?.DeepClone(),
                ["composition"] = Entity(bowl)?["composition"]?.DeepClone() });
        });
    }

    private bool TryParkOrdinaryCompleteDough(int player, int bowl)
    {
        int index = bowlAssignments.GetValueOrDefault(bowl, -1), home = AttachmentParent(bowl);
        if (bakeryLeases.ContainsKey(bowl) || !AssignedDoughIngredients(bowl, index) || !Free(bowl, home) || home == 0 ||
            !bowlHomes.TryGetValue(bowl, out int original) || home != original || !HeatCookAvailable(player)) return false;
        int counter = EmptyCenterCounter(Station(bowl)!.Position);
        if (counter == 0 || NativeCookingStation(Entity(counter))) return false;
        var lease = new BakeryLease(bowl, index, home, counter, I(Entity(bowl)?["observedOrdinal"]), I(Entity(home)?["observedOrdinal"]), I(Entity(counter)?["observedOrdinal"]), Frame);
        foreach (int id in lease.Resources) reserved.Add(id); bakeryLeases.Add(bowl, lease);
        if (TryAdmitBakeryParking(lease, player)) { Log("ordinaryDoughOffmixSafetyLease", BakeryStatus(lease)); return true; }
        foreach (int id in lease.Resources) reserved.Remove(id); bakeryLeases.Remove(bowl); return false;
    }

    private bool WaitForSpeculativeFlavor(int bowl, int flavor)
    {
        if (!bakeryLeases.TryGetValue(bowl, out var lease) || WithinOrdinaryWindow(lease.Index) || !EmptyFood(bowl) ||
            Stations("chop").Any(s => Chopped(Attached(s.EntityId), flavor))) return false;
        int board = Board("upper-right", false);
        if (EmptyAttachment(board) && Free(board) && LooseIngredientCount(flavor) == 0)
            Supply(1, CarnivalRecipes.Ingredients[flavor], board, false);
        // The same ordinary shared-board chopping/transfer paths finish the
        // flavor. Do not start speculative flour+egg clocks before it is ready.
        return true;
    }
}
