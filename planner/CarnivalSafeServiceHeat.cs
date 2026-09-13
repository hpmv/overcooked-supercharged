using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // The first supported case is the observed V14 complete, near-ready bowl.
    // Its existing transfer is reserved before this new, bounded firing Work.
    // No already active action is interrupted and no heat clock is rewritten.
    private sealed record SafeServiceClock(string Role, int Vessel, int Home, int Ordinal, int HomeOrdinal,
        double Progress, double ReadyAt, double Guard)
    { public double Remaining => Guard - Progress; }
    private sealed class SafeServiceHeat(int player, Work original, NearReadyDoughTransfer dough,
        int cannon, int button, int plate, int source, int index, int frame, int timeout,
        Point2 buttonApproach, Dictionary<int, int> identities, JsonObject evidence)
    {
        public readonly int Player = player, Cannon = cannon, Button = button, Plate = plate, Source = source,
            Index = index, Started = frame, Timeout = timeout;
        public readonly Work Original = original;
        public readonly NearReadyDoughTransfer Dough = dough;
        public readonly Point2 ButtonApproach = buttonApproach;
        public readonly Dictionary<int, int> Identities = identities;
        public readonly string Queue = JsonSerializer.Serialize(original.Actions);
        public readonly JsonObject Evidence = evidence;
        public Work FireWork = null!;
    }
    private SafeServiceHeat? safeServiceHeat;
    private const double SafeServiceMarginSeconds = 1;
    private const int SafeServiceMaximumFireFrames = 180;
    private bool IsSafeServiceSuspendedWork(int player, Work work) => safeServiceHeat is { } s &&
        s.Player == player && ReferenceEquals(s.Original, work) && ReferenceEquals(workers[player], s.FireWork);

    private SafeServiceClock[] SafeServiceUnownedClocks(int selected)
    {
        var result = new List<SafeServiceClock>();
        foreach (string role in new[] { "pot", "basket", "pan", "bowl" })
        foreach (var station in Stations(role).OrderBy(s => s.EntityId))
        {
            int id = station.EntityId, home = AttachmentParent(id); bool mixing = role == "bowl";
            var entity = Entity(id); var owner = Entity(home);
            if (EmptyFood(id) || owner is null || (mixing ? !KitchenModel.Components(owner).Contains("MixingStation") : !NativeCookingStation(owner)) ||
                id != selected && HeatHasOwner(id)) continue;
            double duration = N(entity?[mixing ? "mixingTime" : "cookingTime"]), progress = N(entity?[mixing ? "mixingProgress" : "cookingProgress"]);
            double guard = role == "pot" ? 23 : role == "basket" ? 19 : 21;
            if (entity is null || !B(entity["active"]) || !B(owner["active"]) || entity["observedOrdinal"] is null || owner["observedOrdinal"] is null ||
                duration != (role == "basket" ? 10 : 12) || !double.IsFinite(progress) || progress < 0 || progress >= guard || Food(id).IsRuined)
                return [];
            result.Add(new(role, id, home, I(entity["observedOrdinal"]), I(owner["observedOrdinal"]), progress, duration, guard));
        }
        return result.ToArray();
    }

    private bool SafeServiceBudget(int player, int bowl, int mixer, int basket, Point2 buttonApproach,
        double fireSeconds, out JsonObject evidence)
    {
        evidence = new();
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        if (!(speed > 0) || !double.IsFinite(speed) || Station(mixer) is not { } source || Station(basket) is not { } target) return false;
        var obstacles = TrafficObstacles(player);
        var approach = Navigation.ToStation(model, buttonApproach, source, obstacles);
        if (!approach.Success) return false;
        var transfer = Navigation.ToStation(model, approach.Points[^1], target, obstacles);
        if (!transfer.Success) return false;
        var restore = Navigation.ToStation(model, transfer.Points[^1], source, obstacles);
        if (!restore.Success) return false;
        var clocks = SafeServiceUnownedClocks(bowl);
        var selected = clocks.SingleOrDefault(c => c.Vessel == bowl);
        if (selected is null) return false;
        // Keep the original native wait conservative: firing time does not
        // subtract from it. Include all three transfer/restore walking legs.
        double queueSeconds = (approach.Length + transfer.Length + restore.Length) / speed + Math.Max(0, selected.ReadyAt - selected.Progress) + 3;
        // The proven whole-bowl pickup stops native mixing. Its own deadline
        // therefore ends at detachment; transfer and empty-bowl restoration
        // still count in full before considering another vessel's rescue.
        double detachSeconds = approach.Length / speed + Math.Max(0, selected.ReadyAt - selected.Progress) + 2;
        var following = clocks.Where(c => c.Vessel != bowl).OrderBy(c => c.Remaining).ThenBy(c => c.Vessel).ToArray();
        if (following.Length > 2 || following.Any(c => c.Role != "pot" || c.Progress >= 11 || !NativeSausagePot(c.Vessel) ||
            !potHomes.TryGetValue(c.Vessel, out var home) || home.Home != c.Home || home.PotOrdinal != c.Ordinal || home.HomeOrdinal != c.HomeOrdinal)) return false;
        var parking = Stations("counter").Where(s => s.Regions.SequenceEqual(new[] { "center" }) &&
            s.Position.X > 18.5 && s.Position.X < 22.1 && s.Position.Distance(new Point2(20.4, -16.8)) > .1 &&
            EmptyAttachment(s.EntityId) && Free(s.EntityId) && B(Entity(s.EntityId)?["active"]) &&
            Entity(s.EntityId)?["observedOrdinal"] is not null && KitchenModel.Components(Entity(s.EntityId)!).Contains("AttachStation")).ToArray();
        var usedParking = new HashSet<int>();
        var deadlines = new JsonArray();
        double selectedNeeded = fireSeconds + detachSeconds;
        bool fits = selected.Remaining - selectedNeeded > SafeServiceMarginSeconds;
        deadlines.Add(new JsonObject { ["role"] = selected.Role, ["vessel"] = selected.Vessel, ["home"] = selected.Home,
            ["ordinal"] = selected.Ordinal, ["homeOrdinal"] = selected.HomeOrdinal, ["progress"] = selected.Progress,
            ["remainingSeconds"] = selected.Remaining, ["estimatedSeconds"] = selectedNeeded,
            ["slackSeconds"] = selected.Remaining - selectedNeeded });
        double elapsed = fireSeconds + queueSeconds;
        Point2 nextStart = restore.Points[^1];
        foreach (var clock in following)
        {
            // Use one serial cook, never count the second cook as available.
            // Whole-pot detachment stops native cooking; an actual free
            // ordinary parking path then empties the hands before the next
            // pot. The distinct counters are feasibility witnesses, not new
            // persistent leases or an instruction to skip native rescue.
            if (Station(clock.Home) is not { } station) return false;
            var additional = Navigation.ToStation(model, nextStart, station, obstacles);
            if (!additional.Success) return false;
            var park = parking.Where(s => !usedParking.Contains(s.EntityId))
                .Select(s => (Station: s, Path: Navigation.ToStation(model, additional.Points[^1], s, obstacles)))
                .Where(p => p.Path.Success).OrderBy(p => p.Path.Length).ThenBy(p => p.Station.EntityId).FirstOrDefault();
            if (park.Station is null) return false;
            usedParking.Add(park.Station.EntityId);
            double needed = elapsed + additional.Length / speed;
            needed += Math.Max(0, clock.ReadyAt - clock.Progress - needed) + 2;
            double slack = clock.Remaining - needed;
            fits &= double.IsFinite(slack) && slack > SafeServiceMarginSeconds;
            elapsed = needed + park.Path.Length / speed + 1;
            nextStart = park.Path.Points[^1];
            deadlines.Add(new JsonObject { ["role"] = clock.Role, ["vessel"] = clock.Vessel, ["home"] = clock.Home,
                ["ordinal"] = clock.Ordinal, ["homeOrdinal"] = clock.HomeOrdinal, ["progress"] = clock.Progress,
                ["remainingSeconds"] = clock.Remaining, ["estimatedSeconds"] = needed, ["slackSeconds"] = slack,
                ["subsequentApproach"] = JsonSerializer.SerializeToNode(additional),
                ["parkingWitness"] = park.Station.EntityId, ["parkingOrdinal"] = Entity(park.Station.EntityId)?["observedOrdinal"]?.DeepClone(),
                ["parkingPath"] = JsonSerializer.SerializeToNode(park.Path), ["serialSecondsAfterParking"] = elapsed });
        }
        evidence = new JsonObject { ["fireTimeoutSeconds"] = fireSeconds, ["fullTransferQueueSeconds"] = queueSeconds, ["selectedBowlDetachSeconds"] = detachSeconds,
            ["safetyMarginSeconds"] = SafeServiceMarginSeconds, ["approach"] = JsonSerializer.SerializeToNode(approach),
            ["transfer"] = JsonSerializer.SerializeToNode(transfer), ["restore"] = JsonSerializer.SerializeToNode(restore),
            ["deadlinePolicy"] = "selected bowl detachment, complete original queue, then serial whole-pot pickup and distinct ordinary parking",
            ["deadlines"] = deadlines };
        return fits;
    }

    private bool TryServeBeforeSafeHeat(int[] available, HeatObligation[] pending)
    {
        if (!options.ServeBeforeSafeHeat || !options.ReleaseFiringChefOnLaunch || safeServiceHeat is not null ||
            available.Length != 1 || pending.Length != 1 || pending[0].Kind != "bowl" || cannonInterruptions.Count != 0 ||
            trafficYield is not null || delivered >= recipes.Length || !HasAccessibleHead("lower-right")) return false;
        int player = available[0], bowl = pending[0].Vessel, mixer = pending[0].Home;
        int index = bowlAssignments.GetValueOrDefault(bowl, -1), cannon = CannonId("left");
        int button = I(Entity(cannon)?["cannonButtonEntityId"]), plate = mealPlates.GetValueOrDefault(delivered), source = AttachmentParent(plate);
        if (!HeatCookAvailable(player) || Held(2) != 0 || workers[2] is not null || !Free(cannon, button, plate, source) ||
            !CanReserveCannonFlight(cannon, 2) || !B(Entity(cannon)?["cannonReady"]) || B(Entity(cannon)?["cannonFlying"]) ||
            Entity(cannon)?["cannonState"]?.ToString() != "Load" || B(chefs[2]["controlsEnabled"]) ||
            I(Entity(cannon)?["cannonLoadedEntityId"]) != I(chefs[2]["entityId"]) || I(chefs[2]["interactingEntityId"]) != cannon ||
            source == 0 || Attached(source) != plate || HeldByAnyone(plate) || !IsHeadMeal(plate) ||
            Station(source) is not { } plateStation || !plateStation.Regions.Contains("lower-right") || Station(button) is not { } buttonStation ||
            new[] { cannon, button, plate, source }.Any(id => Entity(id)?["observedOrdinal"] is null || !B(Entity(id)?["active"]))) return false;
        var chef = chefs[player];
        if (!B(chef["directlyControlled"]) || !B(chef["canAcceptInput"]) || B(chef["inputSuppressed"]) || B(chef["respawning"]) ||
            B(chef["aimingThrow"]) || chef["dashTimer"] is null || N(chef["dashTimer"]) > 0 ||
            chef["lastVelocity"] is null || KitchenModel.Position(chef["lastVelocity"]).Distance(default) > .05) return false;
        var emitted = (response["inputs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(i => I(i["player"]) == player);
        if (emitted is null || B(emitted["use"]) || B(emitted["pickup"]) || B(emitted["dash"]) || N(emitted["x"]) != 0 || N(emitted["y"]) != 0) return false;
        var landing = KitchenModel.Position(Entity(cannon)!["cannonTarget"]);
        if (model.RegionAt(landing) != "lower-right") return false;
        var collect = Navigation.ToStation(model, landing, plateStation, TrafficObstacles(2));
        if (!collect.Success) return false;
        var serve = Navigation.ToStation(model, collect.Points[^1], Station(Single("delivery"))!, TrafficObstacles(2));
        if (!serve.Success) return false;
        var buttonPath = Navigation.ToStation(model, Position(player), buttonStation, TrafficObstacles(player));
        var originalAdmission = PlanNearReadyDough(player, bowl, index, mixer);
        if (!buttonPath.Success || originalAdmission is null) return false;
        var afterButton = PlanNearReadyDough(player, bowl, index, mixer, buttonPath.Points[^1]);
        if (afterButton is null || afterButton.Basket != originalAdmission.Basket || afterButton.Fryer != originalAdmission.Fryer) return false;
        double speed = N(chef["runSpeed"]) * N(chef["surfaceSpeedMultiplier"]);
        double requiredFireSeconds = buttonPath.Length / speed + 1; // native target, press/release and settling allowance
        int minimum = (int)Math.Ceiling(requiredFireSeconds * 60);
        if (!SafeServiceBudget(player, bowl, mixer, afterButton.Basket, buttonPath.Points[^1], 0, out var noFireBudget)) return false;
        // Adding fire duration can increase any estimated deadline cost by at
        // most that duration. Derive a conservative limit once, then validate
        // it with the actual wait overlap; do not run a path search per frame
        // of possible timeout during admission.
        double headroom = noFireBudget["deadlines"]!.AsArray().Min(d => N(d?["slackSeconds"]) - SafeServiceMarginSeconds);
        int timeout = Math.Min(SafeServiceMaximumFireFrames, (int)Math.Floor(headroom * 60 - .0001));
        if (timeout < minimum || !SafeServiceBudget(player, bowl, mixer, afterButton.Basket, buttonPath.Points[^1], timeout / 60d, out var budget)) return false;
        double serviceSpeed = N(chefs[2]["runSpeed"]) * N(chefs[2]["surfaceSpeedMultiplier"]);
        var order = (state["orders"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        if (order is null || I(order["recipeId"]) != recipes[delivered].Id || !(serviceSpeed > 0) ||
            N(order["remaining"]) <= timeout / 60d + 2 + (collect.Length + serve.Length) / serviceSpeed + 2) return false;
        if (!TryTransferNearReadyDough(player, bowl, index, mixer)) return false;
        var original = workers[player]!; var dough = nearReadyDoughTransfers[original];
        if (dough.Basket != afterButton.Basket || dough.Fryer != afterButton.Fryer || original.Active is not null)
            throw new InvalidOperationException("Safe service preflight disagrees with its exact untouched native dough reservation.");
        var identities = new[] { cannon, button, plate, source }.Concat(dough.Resources)
            .Concat(SafeServiceUnownedClocks(bowl).SelectMany(c => new[] { c.Vessel, c.Home })).Distinct()
            .ToDictionary(id => id, id => I(Entity(id)?["observedOrdinal"]));
        var plan = new SafeServiceHeat(player, original, dough, cannon, button, plate, source, delivered, Frame, timeout, buttonPath.Points[^1], identities, budget);
        safeServiceHeat = plan; reserved.UnionWith(new[] { plate, source }); workers[player] = null;
        Log("plannerJobPaused", new JsonObject { ["frame"] = Frame, ["player"] = player, ["name"] = original.Name,
            ["resources"] = JsonSerializer.SerializeToNode(original.OwnedResources), ["remainingActions"] = original.Actions.Count,
            ["reason"] = "new untouched heat queue reserved behind bounded ready service cannon" });
        var action = Cannon("fire-cannon", "left"); action["passengerPlayer"] = 2; action["destinationRegion"] = "lower-right";
        action["dash"] = false; action["shortDash"] = false; action["timeoutFrames"] = timeout; action["maxNoPathFrames"] = Math.Min(timeout, 30);
        if (!StartCannonFire(player, "safe-heat-fire-left", action, cannon, button, () => ResumeSafeServiceHeat(plan)))
            throw new InvalidOperationException("Safe service lost its checked firing resources after reserving the native transfer.");
        plan.FireWork = workers[player]!;
        var description = SafeServiceHeatStatus(plan); description["buttonPath"] = JsonSerializer.SerializeToNode(buttonPath);
        description["estimatedFireSeconds"] = requiredFireSeconds; description["serviceCollectPath"] = JsonSerializer.SerializeToNode(collect);
        description["serviceDeliveryPath"] = JsonSerializer.SerializeToNode(serve);
        Log("nativeServiceBeforeSafeHeatAdmitted", description); return true;
    }

    private void RequireSafeServiceHeat(SafeServiceHeat plan, bool beforeLaunch)
    {
        if (!ReferenceEquals(safeServiceHeat, plan) || delivered != plan.Index || mealPlates.GetValueOrDefault(plan.Index) != plan.Plate ||
            Attached(plan.Source) != plan.Plate || !IsHeadMeal(plan.Plate) || HeldByAnyone(plan.Plate) ||
            plan.Identities.Any(p => !SameObservedEntity(p.Key, p.Value) || !B(Entity(p.Key)?["active"])) ||
            !reserved.Contains(plan.Plate) || !reserved.Contains(plan.Source) ||
            plan.Original.Active is not null || JsonSerializer.Serialize(plan.Original.Actions) != plan.Queue ||
            !plan.Original.OwnedResources.SetEquals(plan.Dough.Resources) || plan.Dough.Resources.Any(id => !reserved.Contains(id)) ||
            workers.OfType<Work>().Any(w => w != plan.Original && w.OwnedResources.Overlaps(plan.Dough.Resources.Append(plan.Plate).Append(plan.Source))) ||
            beforeLaunch && !ReferenceEquals(workers[plan.Player], plan.FireWork))
            throw new InvalidOperationException("Safe service lost its exact FIFO plate, native identities, untouched transfer queue or exclusive reservations.");
        if (Frame - plan.Started > plan.Timeout + 1)
            throw new TimeoutException("Safe service exhausted its heat-derived firing deadline.");
        double remainingFire = beforeLaunch ? Math.Max(0, (plan.Timeout - (Frame - plan.Started)) / 60d) : 0;
        if (!SafeServiceBudget(plan.Player, plan.Dough.Bowl, plan.Dough.Mixer, plan.Dough.Basket,
            beforeLaunch ? plan.ButtonApproach : Position(plan.Player), remainingFire, out _))
            throw new TimeoutException("Safe service no longer fits every observed unowned native processing deadline with its margin.");
    }
    private void ObserveSafeServiceHeat()
    {
        if (safeServiceHeat is { } plan) RequireSafeServiceHeat(plan, true);
    }
    private void ResumeSafeServiceHeat(SafeServiceHeat plan)
    {
        RequireSafeServiceHeat(plan, false);
        if (workers[plan.Player] is not null || Held(plan.Player) != 0 ||
            !cannonFlights.TryGetValue(plan.Cannon, out var flight) || flight.Phase != CannonFlightPhase.Flying || flight.Firer != plan.Player)
            throw new InvalidOperationException("Safe service transfer cannot resume without the existing verified native launch receipt.");
        reserved.Remove(plan.Plate); reserved.Remove(plan.Source); workers[plan.Player] = plan.Original;
        plan.Original.NeutralBoundaryFrame = -1; safeServiceHeat = null;
        Log("plannerJobResumed", new JsonObject { ["player"] = plan.Player, ["name"] = plan.Original.Name, ["frame"] = Frame,
            ["pausedAtFrame"] = plan.Started, ["pausedFrames"] = Frame - plan.Started,
            ["resources"] = JsonSerializer.SerializeToNode(plan.Original.OwnedResources), ["remainingActions"] = plan.Original.Actions.Count });
        Log("nativeServiceBeforeSafeHeatResumed", SafeServiceHeatStatus(plan));
    }
    private JsonObject SafeServiceHeatStatus(SafeServiceHeat plan) => new() { ["frame"] = Frame, ["player"] = plan.Player,
        ["cannon"] = plan.Cannon, ["button"] = plan.Button, ["passengerPlayer"] = 2, ["fifoIndex"] = plan.Index,
        ["plate"] = plan.Plate, ["source"] = plan.Source, ["heatBowl"] = plan.Dough.Bowl, ["heatMixer"] = plan.Dough.Mixer,
        ["basket"] = plan.Dough.Basket, ["fryer"] = plan.Dough.Fryer, ["startedFrame"] = plan.Started, ["timeoutFrames"] = plan.Timeout,
        ["suspendedWork"] = plan.Original.Name, ["identities"] = JsonSerializer.SerializeToNode(plan.Identities), ["budget"] = plan.Evidence.DeepClone() };
}
