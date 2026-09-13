using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum SausagePhase { Supplying, Handoff, Parking, Parked, Loading }
    private sealed record SausageHeadGuard(int Head, int Source, int Ordinal, int Index, int Order, int Recipe, int TipBand);
    private sealed class SausageLease(int pass, int counter, int frame, int passOrdinal, int counterOrdinal)
    {
        public readonly int Pass = pass, Counter = counter, StartFrame = frame, PassOrdinal = passOrdinal, CounterOrdinal = counterOrdinal;
        public readonly HashSet<int> Owned = [pass, counter];
        public int Food, FoodOrdinal = -1, Owner = 2, PhaseFrame = frame, Pot, Home, PotOrdinal, HomeOrdinal;
        public SausagePhase Phase;
        public Work? Work;
        public SausageTransfer? Transfer;
        public SausageHeadGuard? HeadGuard;
    }
    private readonly List<SausageLease> sausageBuffers = [];
    private readonly Dictionary<int, (int Home, int PotOrdinal, int HomeOrdinal)> sausagePotHomes = [];
    private const int SausageSupplyFrames = 120;

    private void CaptureSausagePotHomes()
    {
        if (options.SausageBufferSize == 0) return;
        foreach (var station in Stations("pot"))
        {
            int pot = station.EntityId, home = AttachmentParent(pot);
            if (home == 0 || !NativeCookingStation(Entity(home)) || Entity(pot)?["observedOrdinal"] is null || Entity(home)?["observedOrdinal"] is null)
                throw new InvalidOperationException("Sausage buffer initialization requires each native pot at its observed stove.");
            sausagePotHomes.Add(pot, (home, I(Entity(pot)!["observedOrdinal"]), I(Entity(home)!["observedOrdinal"])));
        }
        if (sausagePotHomes.Count != 2) throw new InvalidOperationException("Sausage buffer requires exactly two observed native pots.");
    }

    private bool PureRawSausage(int food) => Entity(food) is { } entity && B(entity["active"]) &&
        Food(food) is { Kind: FoodNodeKind.Ingredient, Preparation: FoodPreparation.Raw } observation &&
        observation.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) &&
        !IsPlate(entity) && !KitchenModel.Components(entity).Contains("CookableContainer");

    private bool BothNativePotsOccupied() => Stations("pot") is { Length: 2 } pots && pots.All(s =>
        Entity(s.EntityId)?["composition"] is not null && !EmptyFood(s.EntityId) && !Food(s.EntityId).IsRuined &&
        AttachmentParent(s.EntityId) != 0 && NativeCookingStation(Entity(AttachmentParent(s.EntityId))));

    private bool SausageBufferDemand() => ComponentDemand(CarnivalRecipes.Frankfurter.Id) >
        ComponentStockCount(CarnivalRecipes.Frankfurter.Id) + sausageBuffers.Count(l => l.Food == 0);

    private JsonObject SausageAction(string type, int station, int timeout)
    {
        var action = A(type, station); action["timeoutFrames"] = timeout; return action;
    }

    private bool TryAdmitSausageBuffer(bool beforeService)
    {
        if (options.SausageBufferSize == 0 || sausageBuffers.Count >= options.SausageBufferSize ||
            workers[2] is not null || Held(2) != 0 || Region(2) != "upper-left" ||
            !B(chefs[2]["controlsEnabled"]) || NativeCannonFlight(2) || !BothNativePotsOccupied() || !SausageBufferDemand() ||
            N(state["timer"]) <= SausageSupplyFrames / 60d + CarnivalRecipes.BoilSeconds + PreServiceTravelSeconds + PreServiceSafetySeconds)
            return false;
        SausageHeadGuard? guard = null;
        if (beforeService)
        {
            if (preServiceUsed || preServiceStock is not null || !AvailablePreServiceHead(out int head, out int source)) return false;
            if (preServiceReadyIndex != delivered) { preServiceReadyIndex = delivered; preServiceReadyFrame = Frame; }
            var order = PreServiceNativeOrder();
            if (Frame - preServiceReadyFrame + SausageSupplyFrames > PreServiceDelayFrames ||
                !PreServiceTipWindow(order, recipes[delivered].Id, SausageSupplyFrames / 60d)) return false;
            guard = new(head, source, I(Entity(head)?["observedOrdinal"]), delivered, I(order!["id"]), recipes[delivered].Id,
                CarnivalRecipes.TipForRemainingFraction(N(order["remaining"]) / N(order["lifetime"])));
        }
        else if (mealPlates.ContainsKey(delivered)) return false;
        int pass = Counter(15.6, -13.2);
        if (Entity(pass)?["attachedEntityId"] is null || !EmptyAttachment(pass) || !Free(pass) || counterSupplies.ContainsKey(pass)) return false;
        int counter = EmptyCenterCounter(new Point2(18.8, -13.2));
        int crate = model.Stations.Single(s => s.Ingredient == CarnivalRecipes.Frankfurter.Name).EntityId;
        if (counter == 0 || !Free(crate) || NativeCookingStation(Entity(counter)) ||
            Entity(pass)?["observedOrdinal"] is null || Entity(counter)?["observedOrdinal"] is null ||
            guard is not null && (guard.Source == pass || guard.Source == counter)) return false;
        var lease = new SausageLease(pass, counter, Frame, I(Entity(pass)!["observedOrdinal"]), I(Entity(counter)!["observedOrdinal"])) { HeadGuard = guard };
        if (guard is not null) { lease.Owned.Add(guard.Head); lease.Owned.Add(guard.Source); }
        foreach (int resource in lease.Owned) reserved.Add(resource);
        sausageBuffers.Add(lease);
        if (!Start(2, "sausage-buffer-supply", [SausageAction("take", crate, SausageSupplyFrames), SausageAction("place", pass, SausageSupplyFrames)],
            [crate], () => CompleteSausageSupply(lease)))
        {
            foreach (int resource in lease.Owned) reserved.Remove(resource);
            sausageBuffers.Remove(lease); return false;
        }
        lease.Work = workers[2];
        if (beforeService) preServiceUsed = true;
        Log("sausageBufferAdmitted", SausageStatus(lease)); return true;
    }

    private void CaptureSausagePickup(SausageLease lease)
    {
        if (lease.Food != 0 || lease.Work?.CompletedBoundaryAction is not { } action || action["type"]?.ToString() != "take") return;
        int crate = model.Stations.Single(s => s.Ingredient == CarnivalRecipes.Frankfurter.Name).EntityId;
        int food = Held(2);
        if (!StationIs(action, crate) || !PureRawSausage(food) || Entity(food)?["observedOrdinal"] is null || !Free(food))
            throw new InvalidOperationException("Sausage buffer pickup lacks the native crate's exact fresh held raw ingredient.");
        lease.Food = food; lease.FoodOrdinal = I(Entity(food)!["observedOrdinal"]);
        lease.Owned.Add(food); reserved.Add(food);
        Log("sausageBufferIngredientObserved", SausageStatus(lease));
    }

    private void RequireSausageLease(SausageLease lease)
    {
        if (!SameObservedEntity(lease.Counter, lease.CounterOrdinal) ||
            lease.Owned.Contains(lease.Pass) && !SameObservedEntity(lease.Pass, lease.PassOrdinal) ||
            lease.Owned.Any(r => !reserved.Contains(r)) || NativeCookingStation(Entity(lease.Counter)))
            throw new InvalidOperationException("Sausage buffer lost its exclusive native counter or resource lease.");
        if (lease.HeadGuard is { } guard)
        {
            var order = PreServiceNativeOrder();
            if (delivered != guard.Index || !SameObservedEntity(guard.Head, guard.Ordinal) || !IsHeadMeal(guard.Head) ||
                AttachmentParent(guard.Head) != guard.Source || order is null || I(order["id"]) != guard.Order ||
                !PreServiceTipWindow(order, guard.Recipe, 0, guard.TipBand))
                throw new InvalidOperationException("Pre-service sausage buffer lost its exact FIFO meal or protected native order/tip window.");
            if (Frame - preServiceReadyFrame > PreServiceDelayFrames)
                throw new TimeoutException("Pre-service sausage buffer exceeded its total ready-head delay budget.");
        }
    }

    private void RequireSausageFood(SausageLease lease)
    {
        if (lease.Food == 0 || !SameObservedEntity(lease.Food, lease.FoodOrdinal) || !PureRawSausage(lease.Food))
            throw new InvalidOperationException("Sausage buffer lost or changed its exact reserved raw ingredient.");
    }

    private void CompleteSausageSupply(SausageLease lease)
    {
        RequireSausageLease(lease); RequireSausageFood(lease);
        if (Held(2) != 0 || Attached(lease.Pass) != lease.Food || !EmptyAttachment(lease.Counter) ||
            Frame - lease.PhaseFrame > SausageSupplyFrames)
            throw new InvalidOperationException("Sausage supply did not complete its bounded native raw counter handoff.");
        if (lease.HeadGuard is { } guard)
        {
            ReleaseSausageResource(lease, guard.Head); ReleaseSausageResource(lease, guard.Source); lease.HeadGuard = null;
        }
        SetSausagePhase(lease, SausagePhase.Handoff, -1);
    }

    private void SetSausagePhase(SausageLease lease, SausagePhase phase, int owner)
    {
        lease.Phase = phase; lease.Owner = owner; lease.PhaseFrame = Frame; lease.Work = owner < 0 ? null : workers[owner];
        Log("sausageBufferPhase", SausageStatus(lease));
    }

    private void ReleaseSausageResource(SausageLease lease, int resource)
    {
        if (!lease.Owned.Remove(resource) || !reserved.Remove(resource))
            throw new InvalidOperationException("Sausage buffer attempted to release a resource it no longer owns.");
    }

    private void AdvanceSausageBuffers()
    {
        if (options.SausageBufferSize == 0) return;
        // Observe a ready meal even while P2 finishes an older supply. Enabling
        // this buffer alone must not reset the shared ready-head delay budget.
        if (preServiceStock is null && !sausageBuffers.Any(l => l.HeadGuard is not null) &&
            Region(2) == "upper-left" && AvailablePreServiceHead(out _, out _) && preServiceReadyIndex != delivered)
        { preServiceReadyIndex = delivered; preServiceReadyFrame = Frame; }
        foreach (var lease in sausageBuffers.ToArray())
        {
            RequireSausageLease(lease);
            if (lease.Phase == SausagePhase.Supplying) CaptureSausagePickup(lease);
            if (lease.Phase is SausagePhase.Supplying or SausagePhase.Parking or SausagePhase.Loading)
            {
                if (lease.Owner < 0 || workers[lease.Owner] != lease.Work ||
                    lease.Phase == SausagePhase.Supplying && Frame - lease.PhaseFrame > SausageSupplyFrames)
                    throw new TimeoutException("Sausage buffer active native transfer exceeded its finite job bound or lost its worker.");
                if (lease.Phase is SausagePhase.Parking or SausagePhase.Loading) RequireSausageTransfer(lease, true);
                if (lease.Phase == SausagePhase.Loading && (Attached(lease.Home) != lease.Pot ||
                    !SameObservedEntity(lease.Pot, lease.PotOrdinal) || !SameObservedEntity(lease.Home, lease.HomeOrdinal)))
                    throw new InvalidOperationException("Sausage buffer loading lost its selected native empty pot/stove identity.");
                continue;
            }
            RequireSausageFood(lease);
            int source = lease.Phase == SausagePhase.Handoff ? lease.Pass : lease.Counter;
            if (Attached(source) != lease.Food || HeldByAnyone(lease.Food))
                throw new InvalidOperationException("Sausage stock moved outside its exact reserved counter transfer.");
            // Full pots are an ordinary waiting condition. Only an active input
            // job has a timeout; unchanged raw off-heat stock remains reserved.
        }
    }

    private bool SausageCookAvailable(int player) => player is 0 or 3 && EarlyCookAvailable(player) &&
        B(chefs[player]["directlyControlled"]) && !cannonInterruptions.ContainsKey(player) &&
        (trafficYield is null || trafficYield.Helper != player && trafficYield.Winner != player);

    private bool TryLoadBufferedSausage(int player)
    {
        if (!SausageCookAvailable(player)) return false;
        foreach (var lease in sausageBuffers.Where(l => l.Phase is SausagePhase.Parked or SausagePhase.Handoff).OrderBy(l => l.StartFrame))
        {
            RequireSausageLease(lease); RequireSausageFood(lease);
            int source = lease.Phase == SausagePhase.Handoff ? lease.Pass : lease.Counter;
            foreach (var potStation in Stations("pot").OrderBy(s => s.Position.Distance(Position(player))))
            {
                int pot = potStation.EntityId, home = AttachmentParent(pot);
                if (!sausagePotHomes.TryGetValue(pot, out var original) || home != original.Home ||
                    !SameObservedEntity(pot, original.PotOrdinal) || !SameObservedEntity(home, original.HomeOrdinal) ||
                    home == 0 || !Free(pot, home) || !NativeCookingStation(Entity(home)) || HeldByAnyone(pot) ||
                    Entity(pot)?["composition"] is null || !EmptyFood(pot) || Entity(pot)?["cookingProgress"] is null ||
                    N(Entity(pot)?["cookingProgress"]) != 0 || N(Entity(pot)?["cookingTime"]) != CarnivalRecipes.BoilSeconds ||
                    Entity(pot)?["observedOrdinal"] is null || Entity(home)?["observedOrdinal"] is null ||
                    counterSupplies.Values.Any(a => a.Vessel == pot)) continue;
                if (Attached(source) != lease.Food || HeldByAnyone(lease.Food))
                    throw new InvalidOperationException("Sausage buffer refill has no exact native source ingredient.");
                var transfer = PlanSausageTransfer(player, source, pot);
                if (transfer is null) continue;
                lease.Pot = pot; lease.Home = home; lease.PotOrdinal = I(Entity(pot)!["observedOrdinal"]); lease.HomeOrdinal = I(Entity(home)!["observedOrdinal"]);
                if (!Start(player, "sausage-buffer-load-pot", [SausageAction("take", source, transfer.Budget), SausageAction("place", pot, transfer.Budget)],
                    [pot, home], () => CompleteSausageLoad(lease, player, source))) return false;
                lease.Transfer = transfer;
                SetSausagePhase(lease, SausagePhase.Loading, player); return true;
            }
        }
        return false;
    }

    private bool TryParkBufferedSausage(int player)
    {
        if (!SausageCookAvailable(player)) return false;
        var lease = sausageBuffers.FirstOrDefault(l => l.Phase == SausagePhase.Handoff);
        if (lease is null) return false;
        RequireSausageLease(lease); RequireSausageFood(lease);
        if (Attached(lease.Pass) != lease.Food || !EmptyAttachment(lease.Counter))
            throw new InvalidOperationException("Sausage buffer cannot park without its exact raw handoff and reserved empty storage.");
        var transfer = PlanSausageTransfer(player, lease.Pass, lease.Counter);
        if (transfer is null) return false;
        if (!Start(player, "sausage-buffer-park", [SausageAction("take", lease.Pass, transfer.Budget), SausageAction("place", lease.Counter, transfer.Budget)], [], () =>
            {
                RequireSausageLease(lease); RequireSausageFood(lease);
                RequireSausageTransfer(lease, false);
                if (Held(player) != 0 || !EmptyAttachment(lease.Pass) || Attached(lease.Counter) != lease.Food ||
                    !lease.Transfer!.PickedUp)
                    throw new InvalidOperationException("Sausage parking did not preserve its exact native raw ingredient and release the pantry handoff.");
                ReleaseSausageResource(lease, lease.Pass); SetSausagePhase(lease, SausagePhase.Parked, -1);
            })) return false;
        lease.Transfer = transfer;
        SetSausagePhase(lease, SausagePhase.Parking, player); return true;
    }

    private void CompleteSausageLoad(SausageLease lease, int player, int source)
    {
        RequireSausageLease(lease);
        RequireSausageTransfer(lease, false);
        var food = Food(lease.Pot);
        if (Held(player) != 0 || !EmptyAttachment(source) || !SameObservedEntity(lease.Pot, lease.PotOrdinal) ||
            !SameObservedEntity(lease.Home, lease.HomeOrdinal) || Attached(lease.Home) != lease.Pot ||
            food.Kind != FoodNodeKind.Cooked || food.CookingStepId != CarnivalRecipes.PotCookingStepId || food.IsRuined ||
            !food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) ||
            SameObservedEntity(lease.Food, lease.FoodOrdinal) && B(Entity(lease.Food)?["active"]) ||
            !lease.Transfer!.PickedUp)
            throw new InvalidOperationException("Sausage buffer refill lacks exact emptied-source, consumed-ingredient and native pot contents proof.");
        Log("sausageBufferConsumed", SausageStatus(lease));
        foreach (int resource in lease.Owned.ToArray()) ReleaseSausageResource(lease, resource);
        sausageBuffers.Remove(lease);
    }

    private JsonObject SausageStatus(SausageLease lease) => new()
    {
        ["phase"] = lease.Phase.ToString(), ["owner"] = lease.Owner, ["pass"] = lease.Pass, ["counter"] = lease.Counter,
        ["food"] = lease.Food, ["foodOrdinal"] = lease.FoodOrdinal, ["pot"] = lease.Pot, ["home"] = lease.Home,
        ["startFrame"] = lease.StartFrame, ["phaseFrame"] = lease.PhaseFrame, ["frame"] = Frame,
        ["transferDeadlineFrame"] = lease.Transfer?.Deadline, ["transferBudgetFrames"] = lease.Transfer?.Budget,
        ["transferEvidence"] = lease.Transfer?.Evidence.DeepClone(),
        ["preServiceHead"] = lease.HeadGuard?.Head, ["resources"] = new JsonArray(lease.Owned.Order().Select(id => (JsonNode?)JsonValue.Create(id)).ToArray())
    };
    private JsonArray SausageBufferStatus() => new(sausageBuffers.Select(l => (JsonNode?)SausageStatus(l)).ToArray());
}
