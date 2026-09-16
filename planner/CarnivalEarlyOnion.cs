using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum EarlyOnionPhase { Loading, Heating, Approach, Waiting, Rescuing, Verifying, Parked, Combining, Restoring, DirectHarvest, PlatedFinish }
    private sealed class EarlyOnionLease(int index, int pan, int home, int counter, int ordinal, int owner, int frame)
    {
        public readonly int Index = index, Pan = pan, Home = home, Counter = counter, Ordinal = ordinal, StartFrame = frame;
        public int Owner = owner, PhaseFrame = frame, ProofFrame, StableSamples, LastProofFrame;
        public double ParkedProgress, ProofTimer;
        public string ParkedFood = "";
        public EarlyOnionPhase Phase;
        public int[] Resources => [Pan, Home, Counter];
    }

    private EarlyOnionLease? earlyOnion;
    private EarlyOnionLease? secondEarlyOnion;
    private IEnumerable<EarlyOnionLease> EarlyOnionLeases()
    {
        if (earlyOnion is not null) yield return earlyOnion;
        if (secondEarlyOnion is not null) yield return secondEarlyOnion;
    }
    private bool HasEarlyOnionLease(int mealIndex) => EarlyOnionLeases().Any(l => l.Index == mealIndex);
    private bool IsEarlyOnionParticipant(int player) => EarlyOnionLeases().Any(l => l.Owner >= 0 && l.Owner == player);
    private bool EarlyCookAvailable(int player) => workers[player] is null && !IsSauceParticipant(player) &&
        !IsEarlyOnionParticipant(player) && !IsBakeryParticipant(player) && !IsFryerParticipant(player) && !IsPotParticipant(player) && Held(player) == 0 && Region(player) == "center" && B(chefs[player]["controlsEnabled"]);
    private static bool NativeCookingStation(JsonObject? entity) => entity is not null &&
        KitchenModel.Components(entity).Any(c => c is "ServerCookingStation" or "CookingStation" or "ServerCookingRegion" or "CookingRegion");
    private JsonObject OnionAction(string type, int station) => VesselAction(type, station, 240);
    private bool PureOnion(int pan) => Food(pan).IngredientIds is [var id] && id == CarnivalRecipes.Onion.Id && !Food(pan).IsRuined;
    private bool NativeOnionCooked(int pan) => PureOnion(pan) && Cooked(pan, CarnivalRecipes.Onion.Id) &&
        Food(pan).CookingStepId == CarnivalRecipes.PanCookingStepId &&
        N(Entity(pan)?["cookingProgress"]) > N(Entity(pan)?["cookingTime"]);
    private string OnionFoodFingerprint(int pan) => JsonSerializer.Serialize(Food(pan));

    private bool TryStartEarlyOnion(int player)
    {
        if (!options.EarlyOnionHeat || earlyOnion is not null || player is not (0 or 3) ||
            !EarlyCookAvailable(0) || !EarlyCookAvailable(3) ||
            Stations("pan").Any(p => Food(p.EntityId).IngredientIds.Contains(CarnivalRecipes.Onion.Id))) return false;
        int index = Pending().Where(p => p.Index <= delivered + 2 && p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) &&
                (!mealFoods.TryGetValue(p.Index, out int food) || !Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id)))
            .Select(p => p.Index).DefaultIfEmpty(-1).First();
        if (index < 0) return false;
        int board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(id => Free(id, Attached(id)) &&
            Chopped(Attached(id), CarnivalRecipes.Onion.Id) && PureOnion(Attached(id)));
        if (board == 0) return false;
        foreach (var panStation in Stations("pan").OrderBy(p => p.EntityId))
        {
            int pan = panStation.EntityId, home = AttachmentParent(pan);
            if (!Free(pan, home) || !EmptyFood(pan) || HeldByAnyone(pan) || home == 0 ||
                !NativeCookingStation(Entity(home)) || N(Entity(pan)?["cookingTime"]) != 12 || N(Entity(pan)?["cookingProgress"]) != 0) continue;
            int counter = EmptyCenterCounter(panStation.Position);
            if (counter == 0 || NativeCookingStation(Entity(counter))) continue;
            // Admission proves a static same-platform route to the pan and its
            // parking counter. Dynamic blockage must still complete natively.
            var approach = Navigation.ToStation(model, Position(player), panStation);
            if (!approach.Success || !Navigation.ToStation(model, approach.Points[^1], Station(counter)!).Success) continue;
            var lease = new EarlyOnionLease(index, pan, home, counter, Entity(pan)?["observedOrdinal"]?.GetValue<int>() ?? -1, player, Frame);
            earlyOnion = lease;
            foreach (int resource in lease.Resources) reserved.Add(resource);
            if (!Start(player, "early-onion-load-" + (index + 1), [OnionAction("take", board), OnionAction("place", pan)],
                [board, Attached(board)], () =>
                {
                    RequireEarlyOnionIdentity(lease);
                    if (Held(player) != 0 || Attached(board) != 0 || Attached(home) != pan || !PureOnion(pan))
                        throw new InvalidOperationException("Early onion loading lacks the exact native pan contents and emptied source board.");
                    SetEarlyOnionPhase(lease, EarlyOnionPhase.Heating, -1);
                }))
            {
                foreach (int resource in lease.Resources) reserved.Remove(resource);
                earlyOnion = null;
                return false;
            }
            Log("earlyOnionLeaseAcquired", EarlyOnionStatus());
            return true;
        }
        return false;
    }

    private IEnumerable<int> ParallelOnionCandidates() => Pending()
        .Where(p => p.Index <= delivered + 2 && p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) && !HasEarlyOnionLease(p.Index) &&
            (!mealFoods.TryGetValue(p.Index, out int food) || !Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id)))
        .Select(p => p.Index);

    private bool TryPrioritizedParallelOnion(int player)
    {
        int onion = ParallelOnionCandidates().DefaultIfEmpty(int.MaxValue).First();
        int missingBase = Pending().Where(p => !IsDonut(p.Recipe) && !mealFoods.ContainsKey(p.Index))
            .Select(p => p.Index).DefaultIfEmpty(int.MaxValue).First();
        // Earlier incomplete meals retain their base assembly priority. The
        // onion's own base or a later unrelated base can overlap native heat.
        return onion != int.MaxValue && onion <= missingBase && TryStartParallelOnion(player);
    }

    private void RegisterParallelOnion(EarlyOnionLease lease)
    {
        if (EarlyOnionLeases().Any(l => l.Index == lease.Index || l.Resources.Intersect(lease.Resources).Any()) ||
            !Free(lease.Resources) || EarlyOnionLeases().Count() >= 2)
            throw new InvalidOperationException("Parallel onions require distinct meal, pan, stove and offheat counter ownership.");
        if (earlyOnion is null) earlyOnion = lease;
        else secondEarlyOnion = lease;
        foreach (int resource in lease.Resources) reserved.Add(resource);
    }

    private int ParallelOnionParking(int player, int pan, int home)
    {
        if (home == 0 || !Free(pan, home) || HeldByAnyone(pan) || !NativeCookingStation(Entity(home)) ||
            N(Entity(pan)?["cookingTime"]) != 12 || Station(pan) is not { } station) return 0;
        int counter = EmptyCenterCounter(station.Position);
        if (counter == 0 || NativeCookingStation(Entity(counter))) return 0;
        var approach = Navigation.ToStation(model, Position(player), station);
        return approach.Success && Navigation.ToStation(model, approach.Points[^1], Station(counter)!).Success ? counter : 0;
    }

    private bool TryStartParallelOnion(int player)
    {
        if (!options.ParallelOnionHeat || player is not (0 or 3) || !EarlyCookAvailable(player) ||
            new[] { 0, 3 }.Any(p => Region(p) != "center" || !B(chefs[p]["controlsEnabled"])) ||
            EarlyOnionLeases().Count() >= 2 || !Stations("pan").Any(p => EmptyFood(p.EntityId) && Free(p.EntityId))) return false;
        // Ordinary cooking has no persistent meal assignment. Before admitting
        // another pan, explicitly own each existing free onion pan for a real
        // observed plain hotdog. Do not infer ownership from recipe counts.
        foreach (var panStation in Stations("pan").Where(p => Food(p.EntityId).IngredientIds.Contains(CarnivalRecipes.Onion.Id) &&
                     !EarlyOnionLeases().Any(l => l.Pan == p.EntityId)).OrderBy(p => p.EntityId))
        {
            if (EarlyOnionLeases().Count() >= 2 || !PureOnion(panStation.EntityId)) return false;
            int index = ParallelOnionCandidates().Where(i => mealFoods.TryGetValue(i, out int food) && AttachmentParent(food) != 0 &&
                CarnivalRecipes.MatchRecipe(Entity(food), 296560, false).ReadyToDeliver).DefaultIfEmpty(-1).First();
            int pan = panStation.EntityId, home = AttachmentParent(pan);
            // A mature ordinary onion should be harvested by its existing path;
            // adoption is restricted to the observed pre-cooked heat window.
            double progress = N(Entity(pan)?["cookingProgress"]);
            if (index < 0 || progress < 0 || progress >= 12) return false;
            int counter = ParallelOnionParking(player, pan, home);
            if (counter == 0) return false;
            var adopted = new EarlyOnionLease(index, pan, home, counter, Entity(pan)?["observedOrdinal"]?.GetValue<int>() ?? -1, -1, Frame)
                { Phase = EarlyOnionPhase.Heating };
            RegisterParallelOnion(adopted);
            Log("parallelOnionOrdinaryPanAdopted", EarlyOnionStatus(adopted));
        }
        // Adoption can reveal an already urgent pan. Give its native rescue
        // first claim on available chefs before considering another loader.
        AdvanceEarlyOnion(false);
        if (IsEarlyOnionParticipant(player) || workers[player] is not null) return true;
        if (EarlyOnionLeases().Count() >= 2 || !EarlyCookAvailable(player)) return false;
        int next = ParallelOnionCandidates().DefaultIfEmpty(-1).First();
        if (next < 0) return false;
        int board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(id => Free(id, Attached(id)) &&
            Chopped(Attached(id), CarnivalRecipes.Onion.Id) && PureOnion(Attached(id)));
        if (board == 0) return false;
        foreach (var panStation in Stations("pan").OrderBy(p => p.EntityId))
        {
            int pan = panStation.EntityId, home = AttachmentParent(pan);
            if (!EmptyFood(pan) || N(Entity(pan)?["cookingProgress"]) != 0) continue;
            int counter = ParallelOnionParking(player, pan, home);
            if (counter == 0) continue;
            var lease = new EarlyOnionLease(next, pan, home, counter, Entity(pan)?["observedOrdinal"]?.GetValue<int>() ?? -1, player, Frame);
            RegisterParallelOnion(lease);
            if (!Start(player, "early-onion-load-" + (next + 1), [OnionAction("take", board), OnionAction("place", pan)],
                [board, Attached(board)], () =>
                {
                    RequireEarlyOnionIdentity(lease);
                    if (Held(player) != 0 || Attached(board) != 0 || Attached(home) != pan || !PureOnion(pan))
                        throw new InvalidOperationException("Parallel onion loading lacks the exact native pan contents and emptied source board.");
                    SetEarlyOnionPhase(lease, EarlyOnionPhase.Heating, -1);
                }))
                throw new InvalidOperationException("Parallel onion's reserved available loader could not start its native job.");
            Log("parallelOnionLeaseAcquired", EarlyOnionStatus(lease));
            return true;
        }
        return false;
    }

    private void SetEarlyOnionPhase(EarlyOnionLease lease, EarlyOnionPhase phase, int owner)
    {
        lease.Phase = phase; lease.PhaseFrame = Frame; lease.Owner = owner;
        Log("earlyOnionPhase", EarlyOnionStatus(lease));
    }
    private void RequireEarlyOnionIdentity(EarlyOnionLease lease)
    {
        if (Entity(lease.Pan) is not { } pan || (lease.Ordinal >= 0 && I(pan["observedOrdinal"]) != lease.Ordinal) ||
            lease.Resources.Any(id => !reserved.Contains(id)) || !NativeCookingStation(Entity(lease.Home)) || NativeCookingStation(Entity(lease.Counter)))
            throw new InvalidOperationException("Early onion lost its exact native pan, stove, or exclusive parking reservation.");
    }
    private void RequireParkedOnion(EarlyOnionLease lease, bool unchanged)
    {
        if (Attached(lease.Counter) != lease.Pan || AttachmentParent(lease.Pan) != lease.Counter ||
            Attached(lease.Home) != 0 || HeldByAnyone(lease.Pan) || !NativeOnionCooked(lease.Pan) ||
            unchanged && (N(Entity(lease.Pan)?["cookingProgress"]) != lease.ParkedProgress || OnionFoodFingerprint(lease.Pan) != lease.ParkedFood))
            throw new InvalidOperationException("Early onion is not the same cooked pan off heat with unchanged native contents and progress.");
    }
    private void StartEarlyOnionChild(EarlyOnionLease lease, string name, IEnumerable<JsonObject> actions, Action complete)
    {
        if (!Start(lease.Owner, "early-onion-" + name + "-" + (lease.Index + 1), actions, [], complete))
            throw new InvalidOperationException("Early onion rescue chef was assigned a conflicting job.");
    }

    private void AdvanceEarlyOnion() => AdvanceEarlyOnion(true);
    private void AdvanceEarlyOnion(bool admitNewOwners)
    {
        // Highest observed heat has the earliest native rescue deadline. Offheat
        // leases cannot take a chef away from another pan's cooked barrier.
        foreach (var lease in EarlyOnionLeases().OrderByDescending(l => NativeCookingStation(Entity(AttachmentParent(l.Pan)))
                     ? N(Entity(l.Pan)?["cookingProgress"]) : -1).ThenBy(l => l.Index).ToArray())
            AdvanceEarlyOnion(lease, admitNewOwners);
    }

    private void AdvanceEarlyOnion(EarlyOnionLease lease, bool admitNewOwners = true)
    {
        RequireEarlyOnionIdentity(lease);
        double progress = N(Entity(lease.Pan)?["cookingProgress"]), cookTime = N(Entity(lease.Pan)?["cookingTime"]);
        if (cookTime != 12 || Food(lease.Pan).IsRuined)
            throw new InvalidOperationException("Early onion cooking configuration changed or native food is ruined.");
        // Native burning is >2*cookTime. Every planner iteration precedes one
        // ordinary frame; reject while at least three native heating seconds
        // remain. The neutral error-cleanup frame also stays below burning.
        // This is a failed candidate, never a claim that blocked rescue passed.
        if (NativeCookingStation(Entity(AttachmentParent(lease.Pan))) && progress >= 1.75 * cookTime)
            throw new TimeoutException("Early onion could not prove offheat rescue before its native heating deadline.");
        if (lease.Owner >= 0 && (!B(chefs[lease.Owner]["controlsEnabled"]) || Region(lease.Owner) != "center" || IsSauceParticipant(lease.Owner)))
            throw new InvalidOperationException("Early onion rescue chef lost native control or exclusive center availability.");
        int limit = lease.Phase switch { EarlyOnionPhase.Loading => 480, EarlyOnionPhase.Approach => 240,
            EarlyOnionPhase.Waiting => 240, EarlyOnionPhase.Rescuing => 480, EarlyOnionPhase.Verifying => 30,
            EarlyOnionPhase.Combining => 720, EarlyOnionPhase.Restoring => 480, EarlyOnionPhase.DirectHarvest => 720, EarlyOnionPhase.PlatedFinish => 900, _ => int.MaxValue };
        if (Frame - lease.PhaseFrame > limit) throw new TimeoutException("Early onion phase exceeded its bounded native action window: " + lease.Phase);
        switch (lease.Phase)
        {
            case EarlyOnionPhase.PlatedFinish:
                ObservePlatedOnionFinish(lease);
                return;
            case EarlyOnionPhase.DirectHarvest:
                ObserveDirectEarlyOnion(lease);
                return;
            case EarlyOnionPhase.Loading:
                return;
            case EarlyOnionPhase.Heating:
                if (Attached(lease.Home) != lease.Pan || Attached(lease.Counter) != 0 || !PureOnion(lease.Pan))
                    throw new InvalidOperationException("Early onion heating lost the loaded home pan or empty parking counter.");
                if (progress < .75 * cookTime) return;
                if (!admitNewOwners) return;
                int owner = new[] { 0, 3 }.Where(EarlyCookAvailable).OrderBy(p => Position(p).Distance(Station(lease.Pan)!.Position)).DefaultIfEmpty(-1).First();
                if (owner < 0) return;
                TryAdmitEarlyOnionRescue(lease, owner);
                return;
            case EarlyOnionPhase.Approach:
                return;
            case EarlyOnionPhase.Waiting:
                if (Held(lease.Owner) != 0 || Attached(lease.Home) != lease.Pan || Attached(lease.Counter) != 0)
                    throw new InvalidOperationException("Early onion waiting barrier requires an emptyhanded chef and its unchanged reserved stations.");
                if (!NativeOnionCooked(lease.Pan)) return;
                SetEarlyOnionPhase(lease, EarlyOnionPhase.Rescuing, lease.Owner);
                StartEarlyOnionChild(lease, "park-cooked-pan", [OnionAction("take", lease.Pan), OnionAction("place", lease.Counter)], () =>
                {
                    RequireParkedOnion(lease, false);
                    if (Held(lease.Owner) != 0) throw new InvalidOperationException("Early onion parking did not empty its rescue chef's hands.");
                    lease.ParkedProgress = N(Entity(lease.Pan)?["cookingProgress"]); lease.ParkedFood = OnionFoodFingerprint(lease.Pan);
                    lease.ProofFrame = lease.LastProofFrame = Frame; lease.ProofTimer = N(state["timer"]); lease.StableSamples = 0;
                    SetEarlyOnionPhase(lease, EarlyOnionPhase.Verifying, lease.Owner);
                });
                return;
            case EarlyOnionPhase.Rescuing:
                return;
            case EarlyOnionPhase.Verifying:
                RequireParkedOnion(lease, true);
                if (Frame > lease.LastProofFrame) { lease.StableSamples++; lease.LastProofFrame = Frame; }
                if (lease.StableSamples < 2 || Frame - lease.ProofFrame < 2 || N(state["timer"]) >= lease.ProofTimer) return;
                Log("earlyOnionOffheatProved", EarlyOnionStatus(lease));
                SetEarlyOnionPhase(lease, EarlyOnionPhase.Parked, -1);
                return;
            case EarlyOnionPhase.Parked:
                RequireParkedOnion(lease, true);
                return;
            case EarlyOnionPhase.Combining:
                if (Attached(lease.Counter) != lease.Pan || Attached(lease.Home) != 0 ||
                    (!EmptyFood(lease.Pan) && (!NativeOnionCooked(lease.Pan) || N(Entity(lease.Pan)?["cookingProgress"]) != lease.ParkedProgress)))
                    throw new InvalidOperationException("Parked onion changed before native consumption into its reserved hotdog.");
                return;
            case EarlyOnionPhase.Restoring:
                if (!EmptyFood(lease.Pan) || (Attached(lease.Home) != 0 && Attached(lease.Home) != lease.Pan))
                    throw new InvalidOperationException("Only the emptied onion pan may return to its reserved stove.");
                return;
        }
    }

    private bool TryAdmitEarlyOnionRescue(EarlyOnionLease lease, int owner)
    {
        if (lease.Phase != EarlyOnionPhase.Heating || lease.Owner >= 0 || !EarlyCookAvailable(owner) ||
            Attached(lease.Home) != lease.Pan || N(Entity(lease.Pan)?["cookingProgress"]) < 9) return false;
        RequireEarlyOnionIdentity(lease);
        if (TryPlatedOnionFinish(lease, owner)) return true;
        if (TryDirectEarlyOnionHarvest(lease, owner)) return true;
        SetEarlyOnionPhase(lease, EarlyOnionPhase.Approach, owner);
        StartEarlyOnionChild(lease, "approach-rescue", [OnionAction("navigate", lease.Pan)],
            () => SetEarlyOnionPhase(lease, EarlyOnionPhase.Waiting, owner));
        return true;
    }

    private bool TryCombineEarlyOnion(int player)
    {
        foreach (var lease in EarlyOnionLeases().Where(l => l.Phase == EarlyOnionPhase.Parked).OrderBy(l => l.Index))
            if (TryCombineEarlyOnion(player, lease)) return true;
        return false;
    }

    private bool TryCombineEarlyOnion(int player, EarlyOnionLease lease)
    {
        if (!EarlyCookAvailable(player) ||
            !mealFoods.TryGetValue(lease.Index, out int food) || assembling.Contains(lease.Index) ||
            !CarnivalRecipes.MatchRecipe(Entity(food), 296560, false).ReadyToDeliver) return false;
        int source = AttachmentParent(food);
        if (source == 0 || !Free(food, source)) return false;
        RequireParkedOnion(lease, true);
        if (!Start(player, "finish-early-onion-" + (lease.Index + 1),
            [OnionAction("take", source), OnionAction("combine", lease.Pan), OnionAction("place", source)], [food, source], () =>
            {
                if (!EmptyFood(lease.Pan) || Attached(source) != food || Held(player) != 0 ||
                    !CarnivalRecipes.MatchRecipe(Entity(food), 472326, false).ReadyToDeliver)
                    throw new InvalidOperationException("Early onion native combination lacks its emptied pan and exact completed hotdog.");
                assembling.Remove(lease.Index);
                SetEarlyOnionPhase(lease, EarlyOnionPhase.Restoring, player);
                StartEarlyOnionChild(lease, "restore-empty-pan", [OnionAction("take", lease.Pan), OnionAction("place", lease.Home)], () =>
                {
                    if (!EmptyFood(lease.Pan) || Attached(lease.Home) != lease.Pan || Attached(lease.Counter) != 0 || Held(player) != 0)
                        throw new InvalidOperationException("Early onion cleanup lacks its empty pan restored to its original native stove.");
                    Log("earlyOnionComplete", EarlyOnionStatus(lease));
                    foreach (int id in lease.Resources) reserved.Remove(id);
                    if (earlyOnion == lease) earlyOnion = null;
                    else if (secondEarlyOnion == lease) secondEarlyOnion = null;
                    else throw new InvalidOperationException("Completed onion lease is not registered to either native pan.");
                });
            })) return false;
        assembling.Add(lease.Index);
        SetEarlyOnionPhase(lease, EarlyOnionPhase.Combining, player);
        return true;
    }

    private JsonObject EarlyOnionStatus(EarlyOnionLease? selected = null) => (selected ?? EarlyOnionLeases().OrderBy(l => l.Index).FirstOrDefault()) is not { } lease ? new JsonObject() : new JsonObject
    {
        ["mealIndex"] = lease.Index, ["pan"] = lease.Pan, ["homeStove"] = lease.Home, ["parkingCounter"] = lease.Counter,
        ["panObservedOrdinal"] = lease.Ordinal, ["owner"] = lease.Owner, ["phase"] = lease.Phase.ToString(),
        ["startFrame"] = lease.StartFrame, ["phaseFrame"] = lease.PhaseFrame, ["gameplayFrame"] = Frame,
        ["cookingProgress"] = Entity(lease.Pan)?["cookingProgress"]?.DeepClone(), ["cookingTime"] = Entity(lease.Pan)?["cookingTime"]?.DeepClone(),
        ["nativeAttachmentParent"] = AttachmentParent(lease.Pan), ["parkedProgress"] = lease.ParkedProgress,
        ["stableOffheatSamples"] = lease.StableSamples, ["offheatProofStartFrame"] = lease.ProofFrame
    };

    /// <summary>Offline ownership/deadline/food lifecycle checks; never issues game requests.</summary>
    public static int EarlyOnionSelfTest(JsonObject initialSnapshot)
    {
        int count = 0;
        void Check(bool value, string description)
        { if (!value) throw new InvalidOperationException("Early onion regression: " + description); count++; }
        void Reject(Action action, string description)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, description);
        }
        var plain = CarnivalRecipes.GetRecipe(296560);
        var complete = CarnivalRecipes.GetRecipe(472326);
        var cooked = complete.ExpectedFood.Children.Single(f => f.CookingStepId == CarnivalRecipes.PanCookingStepId);
        JsonObject NativeFood(FoodObservation food) => new()
        {
            ["type"] = food.Kind switch { FoodNodeKind.Ingredient => "IngredientAssembledNode",
                FoodNodeKind.Cooked => "CookedCompositeAssembledNode", _ => "CompositeAssembledNode" },
            ["id"] = food.IngredientId, ["state"] = food.Preparation.ToString(), ["cookingStepId"] = food.CookingStepId,
            ["progress"] = food.Progress, ["children"] = new JsonArray(food.Children.Select(c => (JsonNode?)NativeFood(c)).ToArray())
        };
        int PutFood(CarnivalPlanner p, int parent, FoodObservation food)
        {
            int id = p.entities.Keys.Max() + 1;
            p.entities[id] = new JsonObject { ["id"] = id, ["active"] = true, ["components"] = new JsonArray("CarryableItem"),
                ["composition"] = NativeFood(food), ["position"] = p.Entity(parent)!["position"]!.DeepClone() };
            p.Entity(parent)!["attachedEntityId"] = id;
            return id;
        }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline early onion fixture attempted I/O."), null)
            { response = initialSnapshot.DeepClone().AsObject(), options = new(EarlyOnionHeat: enabled), recipes = [plain, plain, complete] };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline early onion action attempted I/O."), null);
            p.delivered = 0;
            PutFood(p, p.Board("upper-left", true), cooked.Children.Single());
            return p;
        }
        void FinishWork(CarnivalPlanner p, int owner)
        {
            var work = p.workers[owner] ?? throw new InvalidOperationException("Missing fixture work.");
            foreach (int id in work.Resources) p.reserved.Remove(id);
            p.workers[owner] = null; work.Complete?.Invoke();
        }
        void PanFood(CarnivalPlanner p, double progress)
        {
            int id = p.earlyOnion!.Pan;
            p.Entity(id)!["cookingProgress"] = progress;
            p.Entity(id)!["composition"] = NativeFood(cooked with { Preparation = progress > 12 ? FoodPreparation.Cooked : FoodPreparation.Raw });
        }
        CarnivalPlanner Heating()
        {
            var p = Make(); Check(p.TryStartEarlyOnion(0), "valid observed scene admits one bounded early pan");
            PanFood(p, .05); p.Entity(p.Board("upper-left", true))!["attachedEntityId"] = 0;
            FinishWork(p, 0); return p;
        }
        var disabled = Make(false);
        Check(!disabled.TryStartEarlyOnion(0) && disabled.reserved.Count == 0, "default-off admission changes no jobs or reservations");
        var distant = Make(); distant.recipes = [plain, plain, plain, complete];
        Check(!distant.TryStartEarlyOnion(0), "onion more than two FIFO positions ahead remains chopped off heat");
        var busy = Make(); busy.Start(3, "fixture-existing-work", [busy.A("navigate", busy.Board("upper-left", true))], []);
        Check(!busy.TryStartEarlyOnion(0), "admission requires both central chefs currently available");
        var full = Make();
        foreach (var station in full.Stations("counter")) full.reserved.Add(station.EntityId);
        Check(!full.TryStartEarlyOnion(0), "no early heating without an actually empty unreserved parking counter");
        var extra = Make();
        int otherPan = extra.Stations("pan").Last().EntityId;
        extra.Entity(otherPan)!["composition"] = NativeFood(cooked);
        Check(!extra.TryStartEarlyOnion(0), "existing onion stock prevents an additional speculative pan");

        var p = Heating(); var lease = p.earlyOnion!;
        Check(lease.Resources.All(p.reserved.Contains) && p.Attached(lease.Home) == lease.Pan && p.Attached(lease.Counter) == 0,
            "exact pan, original native stove and empty counter remain leased across loading completion");
        Check(lease.Counter != p.Counter(20.4, -16.8) && !NativeCookingStation(p.Entity(lease.Counter)),
            "parking preserves FIFO workspace and excludes cooking stations");
        Check(!p.TryStartEarlyOnion(3) && !p.Free(lease.Pan, lease.Counter), "second cook cannot take or replace the leased pan and counter");
        Check(lease.Phase == EarlyOnionPhase.Heating && lease.Owner == -1 && p.workers[0] is null,
            "passive native cooking releases the loader for independent work");
        PanFood(p, 8.9); p.AdvanceEarlyOnion();
        Check(lease.Phase == EarlyOnionPhase.Heating && p.workers.All(w => w is null), "rescue does not idle a chef for the whole native fry");
        PanFood(p, 9.01); p.AdvanceEarlyOnion(); int owner = lease.Owner;
        Check(lease.Phase == EarlyOnionPhase.Approach && p.IsEarlyOnionParticipant(owner) &&
            p.workers[owner]!.Actions.Single()["type"]!.ToString() == "navigate", "three-second approach window takes exclusive ownership of a free cook");
        Check(p.workers[owner]!.Actions.All(a => !B(a["dash"]) && !B(a["shortDash"]) && I(a["timeoutFrames"]) == 240),
            "fine rescue actions stay walking and have finite per-action bounds");
        FinishWork(p, owner);
        Check(lease.Phase == EarlyOnionPhase.Waiting && !p.EarlyCookAvailable(owner), "approached rescue chef cannot be reassigned while awaiting native cooked food");
        PanFood(p, 12); p.AdvanceEarlyOnion();
        Check(lease.Phase == EarlyOnionPhase.Waiting, "native food boundary is strictly greater than twelve seconds");
        PanFood(p, 12.02); p.AdvanceEarlyOnion();
        Check(lease.Phase == EarlyOnionPhase.Rescuing && p.workers[owner]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }) &&
            I(p.workers[owner]!.Actions.Last()["station"]) == lease.Counter, "native cooked pan is legally taken whole and parked at its reserved counter");
        Reject(() => p.workers[owner]!.Complete!(), "job completion alone cannot certify offheat rescue");
        p.Entity(lease.Home)!["attachedEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = lease.Pan;
        FinishWork(p, owner);
        Check(lease.Phase == EarlyOnionPhase.Verifying && p.IsEarlyOnionParticipant(owner), "chef remains reserved until advancing-frame proof completes");
        p.AdvanceEarlyOnion(); p.AdvanceEarlyOnion();
        Check(lease.StableSamples == 0, "duplicate same-frame observations cannot fabricate offheat proof");
        double timer = N(p.state["timer"]); int start = p.Frame;
        p.state["gameplayFrame"] = start + 1; p.state["timer"] = timer - 1d / 60; p.AdvanceEarlyOnion();
        Check(lease.Phase == EarlyOnionPhase.Verifying && lease.StableSamples == 1, "one advancing frame is insufficient");
        p.state["gameplayFrame"] = start + 2; p.state["timer"] = timer - 2d / 60; p.AdvanceEarlyOnion();
        Check(lease.Phase == EarlyOnionPhase.Parked && lease.Owner == -1 && lease.StableSamples == 2,
            "unchanged cooked food/progress with a decreasing native timer releases a proved offheat pan for its meal");
        p.Entity(lease.Pan)!["cookingProgress"] = 12.03;
        Reject(p.AdvanceEarlyOnion, "even small further native heating invalidates an offheat claim");
        p.Entity(lease.Pan)!["cookingProgress"] = lease.ParkedProgress;
        int source = p.EmptyCenterCounter(new(19.2, -10.8)), meal = PutFood(p, source, plain.ExpectedFood);
        p.mealFoods[lease.Index] = meal;
        Check(p.TryCombineEarlyOnion(0) && lease.Phase == EarlyOnionPhase.Combining && p.assembling.Contains(lease.Index),
            "only the assigned native plain hotdog claims the parked onion");
        Check(I(p.workers[0]!.Actions.ElementAt(1)["station"]) == lease.Pan && p.reserved.Contains(source),
            "combination reserves exact food/source and targets the leased parked pan identity");
        Reject(() => p.workers[0]!.Complete!(), "unconsumed pan cannot be declared empty or restored to heat");
        p.Entity(meal)!["composition"] = NativeFood(complete.ExpectedFood);
        p.Entity(lease.Pan)!["composition"] = NativeFood(new(FoodNodeKind.Composite, FoodPreparation.Unknown, 0, "", 0, 0, []));
        p.Entity(lease.Pan)!["cookingProgress"] = 0;
        FinishWork(p, 0);
        Check(lease.Phase == EarlyOnionPhase.Restoring && !p.assembling.Contains(lease.Index) && p.workers[0] is not null,
            "verified hotdog becomes available while its owner restores the emptied pan");
        Check(p.workers[0]!.Actions.Select(a => I(a["station"])).SequenceEqual(new[] { lease.Pan, lease.Home }),
            "cleanup returns only the exact empty pan to its recorded native home");
        Reject(() => p.workers[0]!.Complete!(), "restoration cannot finish while pan remains parked");
        p.Entity(lease.Counter)!["attachedEntityId"] = 0; p.Entity(lease.Home)!["attachedEntityId"] = lease.Pan;
        FinishWork(p, 0);
        Check(p.earlyOnion is null && lease.Resources.All(id => !p.reserved.Contains(id)),
            "observed empty-pan restoration releases every long-lived resource lease");
        var blocked = Heating(); PanFood(blocked, 21);
        Reject(blocked.AdvanceEarlyOnion, "blocked onheat rescue rejects before native twenty-four-second burning threshold");
        Check(blocked.earlyOnion!.Phase == EarlyOnionPhase.Heating && blocked.earlyOnion.StableSamples == 0,
            "deadline failure never fabricates an offheat success");
        var lost = Heating(); lost.reserved.Remove(lost.earlyOnion!.Counter);
        Reject(lost.AdvanceEarlyOnion, "missing parking ownership fails before another action");

        // A different pan can finish while this meal's own onion is heating or
        // safely parked. The plain bun/sausage base must still progress, but
        // only the leased pan may supply this meal's onion.
        foreach (var phase in new[] { EarlyOnionPhase.Heating, EarlyOnionPhase.Parked })
        {
            var owned = Heating(); var ownedLease = owned.earlyOnion!;
            owned.delivered = ownedLease.Index;
            owned.recipes = [plain, plain, complete, complete];
            if (phase == EarlyOnionPhase.Parked)
            {
                PanFood(owned, 12.02);
                owned.Entity(ownedLease.Home)!["attachedEntityId"] = 0;
                owned.Entity(ownedLease.Counter)!["attachedEntityId"] = ownedLease.Pan;
                ownedLease.Phase = phase;
                ownedLease.ParkedProgress = N(owned.Entity(ownedLease.Pan)?["cookingProgress"]);
                ownedLease.ParkedFood = owned.OnionFoodFingerprint(ownedLease.Pan);
            }
            int ordinaryPan = owned.Stations("pan").Single(s => s.EntityId != ownedLease.Pan).EntityId;
            owned.Entity(ordinaryPan)!["composition"] = NativeFood(cooked);
            owned.Entity(ordinaryPan)!["cookingProgress"] = 12.02;
            int bunBoard = owned.Board("upper-left", false), pot = owned.Stations("pot").First().EntityId;
            int bun = PutFood(owned, bunBoard, plain.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id));
            owned.Entity(pot)!["composition"] = NativeFood(plain.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked));
            Check(owned.BuildUnplatedHotdog(0), phase + " lease permits its bun and sausage base to be prepared");
            var baseWork = owned.workers[0]!;
            Check(baseWork.Actions.Where(a => a["type"]?.ToString() == "combine").Select(a => I(a["station"])).SequenceEqual(new[] { pot }) &&
                !baseWork.Resources.Contains(ordinaryPan), phase + " lease prevents another cooked pan being consumed during base preparation");
            int baseCounter = I(baseWork.Actions.Last()["station"]);
            owned.Entity(bunBoard)!["attachedEntityId"] = 0;
            owned.Entity(baseCounter)!["attachedEntityId"] = bun;
            owned.Entity(bun)!["composition"] = NativeFood(plain.ExpectedFood);
            FinishWork(owned, 0);
            Check(owned.mealFoods.GetValueOrDefault(ownedLease.Index) == bun && owned.earlyOnion == ownedLease &&
                ownedLease.Resources.All(owned.reserved.Contains), phase + " base completion preserves the exact meal and onion lease");
            if (phase == EarlyOnionPhase.Heating)
            {
                Check(!owned.AddUnplatedOnions(0) && owned.workers[0] is null && owned.Free(ordinaryPan),
                    "heating lease cannot be completed by a different ready onion pan");
                int otherSource = owned.EmptyCenterCounter(new(21.6, -13.2));
                owned.mealFoods[ownedLease.Index + 1] = PutFood(owned, otherSource, plain.ExpectedFood);
                Check(owned.AddUnplatedOnions(3) && I(owned.workers[3]!.Actions.First()["station"]) == otherSource &&
                    I(owned.workers[3]!.Actions.ElementAt(1)["station"]) == ordinaryPan,
                    "the separate cooked pan remains usable by another pending meal while the leased onion heats");
            }
            else
            {
                Check(owned.AddUnplatedOnions(0) && ownedLease.Phase == EarlyOnionPhase.Combining &&
                    I(owned.workers[0]!.Actions.ElementAt(1)["station"]) == ownedLease.Pan && owned.Free(ordinaryPan),
                    "a parked lease combines through its exact reserved pan instead of another ready pan");
            }
        }
        return count;
    }
}
