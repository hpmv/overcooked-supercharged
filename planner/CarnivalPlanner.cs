using System.Globalization;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed record CarnivalPlannerOptions(int Seed = 0, int TargetScore = 5000, int Lookahead = 8,
    int ServiceBatch = 3, int StopAfterDeliveries = 0, int MaximumFrames = 16320,
    bool DirectVesselThrows = true, bool UseDash = false, bool CooperativeSauces = false, bool UseShortDash = false,
    bool BufferChoppedBuns = false, bool EarlyOnionHeat = false, bool PantryChopping = false, bool ParallelOnionHeat = false,
    bool StagedResourceRelease = false, bool ConditionalFarPotThrows = false, int BakeryLookahead = 0, bool PreServiceStock = false,
    bool CannonBoundaryPreemption = false, bool ServiceSideHead = false, bool SharedPantryChopping = false, int SausageBufferSize = 0,
    bool WaitForImminentHead = false, bool ReleaseFiringChefOnLaunch = false, bool NearSauceStaging = false,
    bool ShortDashVessels = false, bool ServeBeforeSafeHeat = false, bool DirectPreparedFlavorThrows = false,
    bool NearReadyPotHarvest = false, bool WasherSidePlating = false, bool DirectCleanPassAssembly = false, bool NearReadyFryerHarvest = false,
    bool NearestCentralTaskPreference = false, bool ContinuousWaypoints = false, bool WaitForFinalSauceHead = false,
    bool PredictiveBakeryReturn = false, bool PlatedOnionFinish = false, bool FifoEmptyBowls = false, bool PlatedHotdogBase = false,
    bool StationaryTargetTransfers = false);

/// <summary>
/// A candidate adaptive Carnival 3-4 dispatcher. It emits ordinary inputs through
/// RouteRunner, and does not change native timers, food, orders, or positions.
/// A completed execution is evidence; this planner's target is not a score claim.
/// </summary>
public sealed partial class CarnivalPlanner(Func<JsonObject, Task<JsonObject>> call, TraceWriter? trace)
{
    private sealed class Work
    {
        public Work(string name, IEnumerable<JsonObject> actions, IEnumerable<int> resources, Action? complete)
        {
            Name = name; Actions = new(actions); Resources = resources.Where(x => x != 0).Distinct().ToArray();
            OwnedResources = new(Resources); Complete = complete;
        }
        public readonly string Name;
        public readonly Queue<JsonObject> Actions;
        public readonly int[] Resources;
        public readonly HashSet<int> OwnedResources;
        public readonly Action? Complete;
        public RouteActionHandle? Active;
        public int NeutralBoundaryFrame = -1;
        public JsonObject? CompletedBoundaryAction;
        public int CompletedBoundaryHeld, CompletedBoundaryTarget;
        public PantrySupply? PantrySupply;
        public UnplatedTransfer? ReleasableTransfer;
    }

    private enum SaucePhase { Preparing, ApplyFirst, SwitchSecond, ApplySecond, StageMeal }
    private sealed class SauceLease(int index, NativeRecipe recipe, int owner, int helper, int plate, int food,
        int source, int output, int dispenser, int button, int frame, int[] resources)
    {
        public readonly int Index = index, Owner = owner, Helper = helper, Plate = plate, Food = food,
            Source = source, Output = output, Dispenser = dispenser, Button = button, StartFrame = frame;
        public readonly NativeRecipe Recipe = recipe;
        public readonly int[] Resources = resources;
        public SaucePhase Phase;
        public int PhaseFrame = frame;
        public bool OwnerDone, HelperDone;
    }

    private sealed class TrafficYield(int winner, int helper, RouteActionHandle winnerAction,
        RouteActionHandle? heldAction, RouteActionHandle move, Point2 target, int frame, Work? idleWork = null)
    {
        public readonly int Winner = winner, Helper = helper, Started = frame;
        public readonly RouteActionHandle WinnerAction = winnerAction, Move = move;
        public readonly RouteActionHandle? HeldAction = heldAction;
        public readonly Work? IdleWork = idleWork;
        public readonly Point2 Target = target;
        public bool Arrived;
    }

    private readonly Work?[] workers = new Work?[4];
    private readonly HashSet<int> reserved = [];
    private readonly Dictionary<int, int> mealPlates = [];
    // Native delivery leaves the consumed plate visible for its animation.
    // Pair the recyclable network ID with its observed registration sequence,
    // falling back to telemetry's object ordinal when the registration lies
    // outside the retained event window. Neither is Unity creation order.
    private sealed record RetiredPlateIdentity(long RegistrationSequence, int ObservedOrdinal);
    private readonly Dictionary<int, RetiredPlateIdentity> retiredPlates = [];
    private readonly Dictionary<int, int> mealFoods = [];
    private readonly HashSet<int> assembling = [];
    private readonly HashSet<int> plating = [];
    private readonly Dictionary<int, int> bowlFlavors = [];
    private readonly Dictionary<int, int> bowlHomes = [];
    private readonly Dictionary<int, int> bowlAssignments = [];
    private readonly Dictionary<int, int> basketAssignments = [];
    // A handoff is an addressed transfer. Its eventual cook must not choose a
    // different compatible bowl merely because that bowl happens to be empty.
    private readonly Dictionary<int, (int Vessel, int Ingredient)> counterSupplies = [];
    private int bufferedBun, bunBufferCounter;
    private readonly int[] playerCompletedAt = new int[4];
    private RouteRunner runner = null!;
    private CarnivalPlannerOptions options = null!;
    private NativeRecipe[] recipes = [];
    private JsonObject response = null!, state = null!;
    private Dictionary<int, JsonObject> entities = [];
    private JsonObject[] chefs = [];
    private KitchenModel model = null!;
    private int delivered, lastDelivered, lastDeliveryFrame, serviceVisitStart, startFrame;
    private bool initialized;
    private SauceLease? sauceLease;
    private bool cooperativeAttemptFailed;
    private TrafficYield? trafficYield;
    private int nextTrafficCheck;

    public async Task<JsonObject> RunAsync(CarnivalPlannerOptions configuration)
    {
        if (cooperativeAttemptFailed) throw new InvalidOperationException("A failed cooperative attempt requires a fresh native restart and new planner instance.");
        options = configuration;
        ValidateBakeryOption(options);
        ValidateSharedPantryOption(options);
        ValidatePredictiveBakeryOption(options);
        if (options.Lookahead is < 2 or > 16 || options.ServiceBatch is < 1 or > 8 || options.MaximumFrames < 1 || options.SausageBufferSize is < 0 or > 2)
            throw new ArgumentException("Invalid Carnival planner bounds.");
        response = await call(Json.Request("inspect"));
        Refresh();
        ValidateKitchen();
        if (!initialized) await Initialize();
        startFrame = Frame;
        try
        {
            while (Frame - startFrame < options.MaximumFrames)
            {
                Refresh();
                if (N(state["timer"]) <= 0 && !B(state["serverRoundActive"]) && !B(state["clientRoundActive"]))
                    return await FinishAsync("native-round-ended");
                if (options.StopAfterDeliveries > 0 && delivered >= options.StopAfterDeliveries)
                    return await FinishAsync("requested-delivery-checkpoint");
                if (N(state["timer"]) <= 0)
                {
                    response = await call(Inputs.Step(1));
                    continue;
                }
                if (I(state["deductions"]) != 0)
                    throw new InvalidOperationException("An order expired; the FIFO preview cursor no longer establishes the planned order sequence.");
                RequireNoActiveRuinedFood();
                ObserveMeals();
                ObserveSharedPantryChopping();
                ObservePreparedFlavorDeliveries();
                ObserveDirectCleanPasses();
                ObserveSauceStaging();
                ObserveSafeServiceHeat();
                AdvanceCannonFlights();
                ObserveNearReadyDoughTransfers();
                ObserveNearReadyPotHarvests();
                ObservePlatedHotdogBases();
                ObserveNearReadyFryerHarvests();
                AdvanceInterceptedThrows();
                AdvanceWasherPlating();
                CompleteWork();
                ObservePredictiveBakeryVisit();
                ReleaseCompletedUnplatedResources();
                AdvancePreServiceStock();
                AdvanceSausageBuffers();
                AdvanceTrafficYield();
                AdvanceCooperativeSauces();
                AdvanceFryerRescues();
                AdvancePotRescues();
                AdvanceEarlyOnion(false);
                AdvanceBakeryLookahead(false);
                DispatchNativeHeatSafety();
                TryBeginCannonBoundaryPreemption();
                TryRecoverWasherPlate();
                TryBeginWasherPlating();
                // All candidates inspect the same completed native frame. Locks
                // acquired by an earlier choice exclude conflicting later jobs.
                foreach (int player in new[] { 2, 1, 0, 3 })
                {
                    if (workers[player] is not null || IsSauceParticipant(player) || IsEarlyOnionParticipant(player) || IsBakeryParticipant(player) || IsFryerParticipant(player) || IsPotParticipant(player) || HeatSafetyBlocks(player)) continue;
                    string? region = Region(player);
                    if (player == 2) PantryAndService(region);
                    else if (player == 1) BakeryAndWash(region);
                    else Central(player);
                }
                TrySharePantryChopping();
                for (int player = 0; player < 4; player++)
                {
                    var work = workers[player];
                    if (work is null) continue;
                    if (work.Active is null && work.Actions.Count > 0)
                    {
                        var action = work.Actions.Dequeue();
                        action["player"] = player;
                        action["timeoutFrames"] ??= 600;
                        work.Active = runner.CreateAction(action);
                    }
                }
                if (trafficYield is null && Frame >= nextTrafficCheck) TryBeginTrafficYield();
                var inputs = Inputs.AllNeutral();
                for (int player = 0; player < 4; player++)
                {
                    if (trafficYield is { } yielding && yielding.Helper == player)
                    {
                        if (!yielding.Arrived) inputs[player] = runner.Tick(yielding.Move, response);
                    }
                    else if (trafficYield is { Arrived: false } moving && moving.Winner == player) { }
                    else if (workers[player]?.Active is { } active) inputs[player] = runner.Tick(active, response);
                }
                if (sauceLease is { } lease)
                    foreach (int player in new[] { lease.Owner, lease.Helper })
                        if (workers[player]?.Active?.Error is { } error)
                            throw new InvalidOperationException("Cooperative sauce action failed for chef " + player + ": " + error);
                if (Frame % 120 == 0) Log("plannerStatus", Status());
                response = await call(Inputs.Step(1, inputs));
            }
            throw new TimeoutException("Carnival planner exceeded its frame budget before native round completion.");
        }
        catch (Exception error)
        {
            CancelPredictiveBakeryVisit("attempt-aborted: " + error.Message);
            Log("plannerFailure", new JsonObject { ["error"] = error.Message, ["planner"] = Status(), ["state"] = state.DeepClone() });
            AbortCooperativeSauces(error.Message);
            await call(Inputs.Step(1));
            throw;
        }
    }

    private async Task Initialize()
    {
        var request = Json.Request("preview"); request["seed"] = options.Seed; request["count"] = 96;
        var previewResponse = await call(request);
        var preview = previewResponse["preview"] as JsonObject ?? throw new InvalidOperationException("Native recipe preview is absent.");
        foreach (string field in new[] { "ambientRngRestored", "isolatedRngRestored", "isolatedRegistryRestored", "observationsRestored", "liveInstanceUnchanged", "frameUnchanged" })
            if (!B(preview[field])) throw new InvalidOperationException("Recipe preview failed its read-only assertion: " + field);
        recipes = (preview["recipes"] as JsonArray)?.OfType<JsonObject>().Select(r => CarnivalRecipes.GetRecipe(I(r["recipeId"]))).ToArray()
            ?? throw new InvalidOperationException("Native preview returned no recipe sequence.");
        runner = new RouteRunner(call, trace);
        runner.ContinuousWaypoints = options.ContinuousWaypoints;
        runner.StationaryTargetTransfers = options.StationaryTargetTransfers;
        CaptureSausagePotHomes();
        foreach (var bowl in Stations("bowl"))
        {
            var home = Stations("mix-station").OrderBy(s => s.Position.Distance(bowl.Position)).First();
            bowlHomes.Add(bowl.EntityId, home.EntityId);
        }
        CaptureFryerHomes();
        CapturePotHomes();
        CaptureSupplyTopology();
        lastDelivered = delivered; serviceVisitStart = delivered;
        lastDeliveryFrame = Frame;
        initialized = true;
        int required = 1;
        while (required < recipes.Length && CarnivalRecipes.PredictFreshOrderedScore(recipes.Take(required).Select(r => r.Id)) < options.TargetScore) required++;
        Log("plannerInitialized", new JsonObject { ["seed"] = options.Seed, ["target"] = options.TargetScore,
            ["predictedFreshDeliveries"] = required, ["predictedFreshScore"] = CarnivalRecipes.PredictFreshOrderedScore(recipes.Take(required).Select(r => r.Id)),
            ["preview"] = preview.DeepClone(), ["configuration"] = System.Text.Json.JsonSerializer.SerializeToNode(options),
            ["qualification"] = "Adaptive candidate; target and timing budgets are not a verified high-score run." });
    }

    private void ValidateKitchen()
    {
        if (state["scene"]?.ToString() != "s_Day_3_4" || chefs.Length != 4 || !chefs.Select(c => I(c["playerId"])).Order().SequenceEqual(new[] { 0, 1, 2, 3 }))
            throw new InvalidOperationException("Carnival planner requires the measured scene and all four local chefs.");
        var session = response["session"] is JsonObject o ? o : JsonNode.Parse(response["session"]?.ToString() ?? "{}")?.AsObject();
        if (I(session?["dlc"]) != 8 || I(session?["variantPlayers"]) != 4 || session?["stage"]?.ToString() != "kitchen_ready")
            throw new InvalidOperationException("Native session must validate DLC8, four-player variant, and kitchen_ready before planning.");
        if (!B(state["gameEventsInstalled"]) || I(state["gameEventsDropped"]) != 0)
            throw new InvalidOperationException("Native scoring event observations must be installed without dropped events.");
    }

    private void Refresh()
    {
        state = response["state"] as JsonObject ?? throw new InvalidOperationException("Missing world snapshot.");
        entities = (state["entities"] as JsonArray)!.OfType<JsonObject>().Where(e => e["active"]?.GetValue<bool>() != false).ToDictionary(e => I(e["id"]));
        chefs = (state["chefs"] as JsonArray)!.OfType<JsonObject>().OrderBy(c => I(c["playerId"])).ToArray();
        model = KitchenModel.Build(state);
        delivered = I(state["delivered"]);
        if (delivered != lastDelivered) { lastDeliveryFrame = Frame; lastDelivered = delivered; }
    }

    private void ObserveMeals()
    {
        if (delivered >= recipes.Length) throw new InvalidOperationException("Native deliveries exhausted the captured recipe preview.");
        var first = (state["orders"] as JsonArray)?.OfType<JsonObject>().OrderBy(o => I(o["id"])).FirstOrDefault();
        if (first is not null && I(first["recipeId"]) != recipes[delivered].Id)
            throw new InvalidOperationException("Live oldest order disagrees with the native seeded preview at delivery " + delivered + ".");
        foreach (var retired in retiredPlates.ToArray())
        {
            var entity = Entity(retired.Key);
            int ordinal = entity?["observedOrdinal"]?.GetValue<int>() ?? -1;
            long registration = PlateRegistrationSequence(retired.Key);
            bool changed = retired.Value.RegistrationSequence >= 0 && registration >= 0
                ? retired.Value.RegistrationSequence != registration
                : retired.Value.ObservedOrdinal >= 0 && ordinal >= 0 && retired.Value.ObservedOrdinal != ordinal;
            if (entity is not null && !changed) continue;
            retiredPlates.Remove(retired.Key);
            Log("plannerRetiredPlateReleased", new JsonObject { ["plate"] = retired.Key, ["observedOrdinal"] = retired.Value.ObservedOrdinal,
                ["registrationSequence"] = retired.Value.RegistrationSequence,
                ["frame"] = Frame, ["reason"] = entity is null ? "native-entity-absent" : "native-id-reused-by-another-observed-object" });
        }
        foreach (var old in mealPlates.Keys.Where(i => i < delivered).ToArray())
        {
            int plate = mealPlates[old];
            if (Entity(plate) is { } entity)
            {
                retiredPlates[plate] = new(PlateRegistrationSequence(plate), entity["observedOrdinal"]?.GetValue<int>() ?? -1);
                Log("plannerPlateRetired", new JsonObject { ["index"] = old, ["plate"] = plate,
                    ["observedOrdinal"] = retiredPlates[plate].ObservedOrdinal,
                    ["registrationSequence"] = retiredPlates[plate].RegistrationSequence, ["frame"] = Frame, ["nativeDelivered"] = delivered });
            }
            mealPlates.Remove(old);
        }
        foreach (var old in mealFoods.Keys.Where(i => i < delivered).ToArray()) mealFoods.Remove(old);
        foreach (var old in basketAssignments.Keys.Where(id => EmptyFood(id) && Free(id)).ToArray()) basketAssignments.Remove(old);
        foreach (var pair in mealPlates)
            if (!entities.ContainsKey(pair.Value) && !assembling.Contains(pair.Key))
                throw new InvalidOperationException("An undelivered reserved meal disappeared: " + pair.Key);
        foreach (var pair in mealFoods)
            if (!entities.ContainsKey(pair.Value) && !plating.Contains(pair.Key))
                throw new InvalidOperationException("An unplated meal disappeared outside its reserved plate assembly: " + pair.Key);
        // Resume ordinary paused play by recognizing already completed plates.
        foreach (var plate in entities.Values.Where(IsPlate).Where(p => !mealPlates.Values.Contains(Id(p)) && !reserved.Contains(Id(p)) &&
            !retiredPlates.ContainsKey(Id(p)) && CanAdoptPlate(p)))
        {
            for (int i = delivered; i < Math.Min(recipes.Length, delivered + options.Lookahead); i++)
            {
                if (mealPlates.ContainsKey(i) || assembling.Contains(i) || !CarnivalRecipes.MatchRecipe(plate, recipes[i].Id).ReadyToDeliver) continue;
                mealPlates.Add(i, Id(plate)); break;
            }
        }
    }

    private long PlateRegistrationSequence(int plate)
    {
        if (state["entityRegistration"] is not JsonObject audit || !B(audit["installed"]) || N(audit["errorCount"]) != 0)
            return -1;
        var last = (audit["events"] as JsonArray)?.OfType<JsonObject>()
            .Where(e => I(e["entity"]?["entityId"]) == plate)
            .OrderByDescending(e => e["sequence"]?.GetValue<long>() ?? -1).FirstOrDefault();
        return last?["kind"]?.ToString() == "register" ? last["sequence"]?.GetValue<long>() ?? -1 : -1;
    }

    private bool CanAdoptPlate(JsonObject plate)
    {
        // A newly constructed planner has no earlier meal bookkeeping. Native
        // StartDeliverySequence detaches the served plate and disables its
        // colliders while its visual object remains alive. Preserve carried and
        // counter-attached meals; never infer this condition from missing data.
        int id = Id(plate);
        if (HeldByAnyone(id) || AttachmentParent(id) != 0) return true;
        var physical = (plate["colliders"] as JsonArray)?.OfType<JsonObject>()
            .Where(c => c["trigger"]?.GetValue<bool>() == false).ToArray() ?? [];
        return physical.Length == 0 || !physical.All(c => c["enabled"]?.GetValue<bool>() == false || c["active"]?.GetValue<bool>() == false);
    }

    private void CompleteWork()
    {
        for (int player = 0; player < 4; player++)
        {
            var work = workers[player];
            if (work?.Active?.IsDone != true) continue;
            if (work.Active.Error is { } error) throw new InvalidOperationException($"Chef {player}, {work.Name}: {error}");
            if (InterceptedSupplierPending(work)) continue;
            ObserveBakeryPortalArrival(player, work.Active.Specification);
            ObserveUnplatedActionCompletion(work, work.Active.Specification);
            work.NeutralBoundaryFrame = Frame;
            work.CompletedBoundaryAction = work.Active.Specification;
            work.CompletedBoundaryHeld = work.Active.Action.InitialHeld;
            work.CompletedBoundaryTarget = work.Active.RuntimeEntityId ?? 0;
            work.Active = null;
            if (work.Actions.Count != 0) continue;
            ReleaseRemainingWorkResources(work);
            workers[player] = null; playerCompletedAt[player] = Frame;
            work.Complete?.Invoke();
            Log("plannerJobComplete", new JsonObject { ["player"] = player, ["name"] = work.Name, ["frame"] = Frame });
        }
    }

    private void AdvanceTrafficYield()
    {
        if (trafficYield is not { } yielding) return;
        if (Frame - yielding.Started > 240)
            throw new TimeoutException("A chef yield did not clear its reserved crossing within four seconds.");
        if (yielding.IdleWork is { } idle ? workers[yielding.Helper] != idle || idle.Active is not null || idle.Actions.Count != 0 || idle.OwnedResources.Count != 0
            : workers[yielding.Helper]?.Active != yielding.HeldAction)
            throw new InvalidOperationException("The paused traffic action lost its job or reservation owner.");
        if (yielding.Move.Error is { } error) throw new InvalidOperationException("Chef yield failed: " + error);
        if (!yielding.Arrived && yielding.Move.IsDone)
        {
            if (Position(yielding.Helper).Distance(yielding.Target) > .101)
                throw new InvalidOperationException("Chef yield completed without reaching its observed clear position.");
            yielding.Arrived = true;
            Log("plannerTrafficYieldArrived", new JsonObject { ["player"] = yielding.Helper, ["winner"] = yielding.Winner, ["frame"] = Frame });
        }
        if (!yielding.Arrived || !yielding.WinnerAction.IsDone) return;
        // The original action has not received ticks or button edges while the
        // helper moved. Drop its stale path, then let normal target/food checks
        // resume it. No game state or native action timer is changed.
        if (yielding.HeldAction is { } held) held.Action.Motion = null;
        else workers[yielding.Helper] = null;
        Log("plannerTrafficYieldComplete", new JsonObject { ["player"] = yielding.Helper, ["winner"] = yielding.Winner,
            ["frame"] = Frame, ["elapsedFrames"] = Frame - yielding.Started });
        trafficYield = null;
        nextTrafficCheck = Frame + 30;
    }

    private bool TryBeginTrafficYield()
    {
        nextTrafficCheck = Frame + 15;
        if (TryBeginIdleTrafficYield()) return true;
        bool Waiting(int player)
        {
            var active = workers[player]?.Active;
            var motion = active?.Action.Motion;
            return active is { IsDone: false, Stage: "navigate" } && motion is { NoPathSince: >= 0 } &&
                active.ElapsedFrames - motion.NoPathSince >= 30 && !IsSauceParticipant(player) &&
                B(chefs[player]["controlsEnabled"]) && !B(chefs[player]["inputSuppressed"]) &&
                N(chefs[player]["dashTimer"]) <= 0 && KitchenModel.Position(chefs[player]["lastVelocity"]).Distance(default) < .05;
        }
        foreach (int winner in Enumerable.Range(0, 4).Where(Waiting))
        foreach (int helper in Enumerable.Range(winner + 1, 3 - winner).Where(Waiting))
        {
            var winnerAction = workers[winner]!.Active!;
            var helperAction = workers[helper]!.Active!;
            var winnerStation = Station(winnerAction.RuntimeEntityId ?? 0);
            var helperStation = Station(helperAction.RuntimeEntityId ?? 0);
            if (winnerStation is null || helperStation is null || Region(winner) != Region(helper)) continue;
            // Both routes must work with static native geometry. Yielding cannot
            // hide a bad collision model, unreachable station, or platform gap.
            if (!Navigation.ToStation(model, Position(winner), winnerStation).Success ||
                !Navigation.ToStation(model, Position(helper), helperStation).Success) continue;
            var obstacles = TrafficObstacles(helper);
            var options = new List<(Point2 Target, NavigationPath Path, double Cost)>();
            foreach (double distance in new[] { 1.2, 1.8, 2.4 })
            foreach (int direction in Enumerable.Range(0, 12))
            {
                double angle = direction * Math.PI / 6;
                var target = new Point2(Position(helper).X + distance * Math.Cos(angle), Position(helper).Z + distance * Math.Sin(angle));
                if (model.RegionAt(target) != Region(helper) || !Navigation.IsWalkable(model, target, Region(helper)!, obstacles)) continue;
                var path = Navigation.FindPath(model, Position(helper), target, .15, obstacles);
                if (!path.Success) continue;
                var relocated = TrafficObstacles(winner, helper, target);
                var resumed = Navigation.ToStation(model, Position(winner), winnerStation, relocated);
                if (!resumed.Success) continue;
                options.Add((target, path, path.Length + resumed.Length));
            }
            if (options.Count == 0) continue;
            var choice = options.OrderBy(p => p.Cost).ThenBy(p => p.Target.X).ThenBy(p => p.Target.Z).First();
            var spec = new JsonObject { ["type"] = "navigate", ["player"] = helper,
                ["target"] = new JsonObject { ["x"] = choice.Target.X, ["z"] = choice.Target.Z },
                ["timeoutFrames"] = 180, ["dash"] = false, ["shortDash"] = false };
            trafficYield = new TrafficYield(winner, helper, winnerAction, helperAction, runner.CreateAction(spec), choice.Target, Frame);
            Log("plannerTrafficYieldStart", new JsonObject { ["player"] = helper, ["winner"] = winner, ["frame"] = Frame,
                ["pausedJob"] = workers[helper]!.Name, ["winningJob"] = workers[winner]!.Name,
                ["target"] = System.Text.Json.JsonSerializer.SerializeToNode(choice.Target),
                ["path"] = System.Text.Json.JsonSerializer.SerializeToNode(choice.Path),
                ["reason"] = "Reciprocal blocked station routes; static routes exist and a measured clear yield lets the lower player ID finish." });
            return true;
        }
        return false;
    }

    private KitchenObstacle[] TrafficObstacles(int exclude, int relocatedPlayer = -1, Point2 relocated = default)
        => chefs.Where(c => I(c["playerId"]) != exclude).Select(c =>
        {
            int player = I(c["playerId"]); var p = player == relocatedPlayer ? relocated : KitchenModel.Position(c["position"]);
            double r = model.ChefRadius;
            return new KitchenObstacle("chef:" + player, I(c["entityId"]), new(p.X - r, p.X + r, p.Z - r, p.Z + r), "Observed chef capsule", true, CircleRadius: r);
        }).ToArray();

    private bool Start(int player, string name, IEnumerable<JsonObject> actions, IEnumerable<int> resources, Action? complete = null)
    {
        var work = new Work(name, actions, resources, complete);
        if (workers[player] is not null || work.Resources.Any(reserved.Contains)) return false;
        foreach (int id in work.Resources) reserved.Add(id);
        workers[player] = work;
        Log("plannerJobStart", new JsonObject { ["player"] = player, ["name"] = name, ["frame"] = Frame,
            ["resources"] = new JsonArray(work.Resources.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["actions"] = new JsonArray(work.Actions.Select(a => (JsonNode?)a.DeepClone()).ToArray()) });
        return true;
    }

    private IEnumerable<(int Index, NativeRecipe Recipe)> Pending() => Enumerable.Range(delivered, Math.Min(options.Lookahead, recipes.Length - delivered))
        .Where(i => !mealPlates.ContainsKey(i) && !assembling.Contains(i)).Select(i => (i, recipes[i]));

    private void PantryAndService(string? region)
    {
        if (!B(chefs[2]["controlsEnabled"]) || NativeCannonFlight(2)) return;
        int held = Held(2);
        if (region == "lower-right")
        {
            ObserveImminentHeadArrival();
            if (held != 0)
            {
                if (!IsHeadMeal(held)) throw new InvalidOperationException("Service chef carries a meal that is not the native FIFO head.");
                Start(2, "serve-fifo-" + delivered, [A("place", Single("delivery"))], [held, Single("delivery")]);
                return;
            }
            if (mealPlates.TryGetValue(delivered, out int plate) && RegionOf(plate) == "lower-right" && !reserved.Contains(plate))
            { Start(2, "collect-fifo-" + delivered, [A("take", plate)], [plate]); return; }
            if (mealPlates.TryGetValue(delivered, out plate))
            {
                int source = AttachmentParent(plate);
                if (source != 0 && Station(source)?.Regions.Contains("lower-right") == true && !reserved.Contains(plate))
                { Start(2, "collect-fifo-" + delivered, [A("take", source)], [plate, source]); return; }
            }
            if (delivered - serviceVisitStart >= options.ServiceBatch || LeftSupplyNeeded() && !HasAccessibleHead("lower-right"))
            {
                if (TryWaitForImminentHead()) return;
                Start(2, "service-return-to-pantry", [Portal("upper-left")], [], () => { serviceVisitStart = delivered; ResetPreServiceVisit(); ResetImminentHeadVisit(); }); return;
            }
            return;
        }
        if (region != "upper-left") return; // Boarded/airborne chef remains neutral.
        if (held != 0)
        {
            if (IsHeadMeal(held)) { Start(2, "board-service-with-fifo", [Cannon("board-cannon", "left")], [held, CannonId("left")]); return; }
            SupplyHeld(2, held); return;
        }
        if (NeedsAim("left", "lower-right")) { Start(2, "aim-service-cannon", [Cannon("aim-cannon", "left")], [CannonId("left")]); return; }
        if (TryPreServiceStock()) return;
        if (TryAdmitSausageBuffer(true)) return;
        // Ready head meals beat optional stock; native 4-plate circulation cannot
        // absorb an arbitrarily large pantry batch before its first delivery.
        if (mealPlates.TryGetValue(delivered, out int ready) && !reserved.Contains(ready))
        {
            int source = AttachmentParent(ready);
            if (source != 0 && Station(source)?.Regions.Contains("upper-left") == true)
            { Start(2, "take-cannon-fifo", [A("take", source)], [ready, source]); return; }
            if (HasAccessibleHead("lower-right"))
            { serviceVisitStart = delivered; Start(2, "board-empty-for-service-wave", [Cannon("board-cannon", "left")], [CannonId("left")]); return; }
        }
        var pending = Pending().Select(p => p.Recipe).ToArray();
        if (pending.Any(r => !IsDonut(r)))
        {
            var emptyPot = Stations("pot").OrderBy(s => s.Position.X).FirstOrDefault(s => Empty(s.EntityId) && Free(s.EntityId));
            int outstanding = ComponentDemand(CarnivalRecipes.Frankfurter.Id);
            if (emptyPot is not null && Stations("pot").Count(s => !Empty(s.EntityId)) < outstanding &&
                Supply(2, CarnivalRecipes.Frankfurter, emptyPot.EntityId, true)) return;
            int onionBoard = Board("upper-left", true), bunBoard = Board("upper-left", false);
            int onionNeed = ComponentDemand(CarnivalRecipes.Onion.Id);
            if (onionNeed > ComponentStockCount(CarnivalRecipes.Onion.Id) && EmptyAttachment(onionBoard) && Free(onionBoard) &&
                Supply(2, CarnivalRecipes.Onion, onionBoard, false)) return;
            if (LooseIngredientCount(CarnivalRecipes.Bun.Id) < Math.Min(2, ComponentDemand(CarnivalRecipes.Bun.Id)) && EmptyAttachment(bunBoard) && Free(bunBoard) &&
                Supply(2, CarnivalRecipes.Bun, bunBoard, false)) return;
        }
        TryAdmitSausageBuffer(false);
    }

    private void BakeryAndWash(string? region)
    {
        if (!B(chefs[1]["controlsEnabled"]) || NativeCannonFlight(1)) return;
        int held = Held(1), dirtyPass = Counter(15.6, -19.2), cleanPass = Counter(15.6, -20.4);
        if (region == "lower-left")
        {
            int sink = Single("sink"), drying = Single("drying");
            if (held != 0)
            {
                if (IsPlate(Entity(held)) && EmptyFood(held))
                {
                    if (EmptyAttachment(cleanPass) && Free(cleanPass))
                        Start(1, "hand-clean-plate-to-center", [A("place", cleanPass)], [held, cleanPass]);
                    return;
                }
                else if (IsDirty(held)) Start(1, "load-dirty-stack", [A("place", sink)], [held, sink]);
                else throw new InvalidOperationException("Wash chef carries an unsupported item: " + held);
                return;
            }
            if (TryReturnForActiveMixer()) return;
            if (I(Entity(drying)?["plateCount"]) > 0 && EmptyAttachment(cleanPass) && Free(drying, cleanPass))
            { Start(1, "return-clean-plate", [A("take", drying), A("place", cleanPass)], [drying, cleanPass]); return; }
            if (I(Entity(sink)?["plateCount"]) > 0 && Free(sink, drying))
            { var wash = A("wash", sink); wash["count"] = 1; Start(1, "wash-one-native-plate", [wash], [sink, drying]); return; }
            int dirty = Attached(dirtyPass);
            if (dirty != 0 && IsDirty(dirty) && Free(dirtyPass, sink))
            { Start(1, "receive-dirty-stack", [A("take", dirtyPass), A("place", sink)], [dirtyPass, sink, dirty]); return; }
            if (TryUrgentFifoBakeryReturn()) return;
            if (TryPredictiveBakeryReturn()) return;
            if (RightSupplyNeeded() && AvailablePlates().Length >= 2 && I(Entity(drying)?["plateCount"]) == 0 && DirtyCount() == 0 &&
                (Frame - lastDeliveryFrame >= 420 || AvailablePlates().Length >= 3))
                Start(1, "wash-return-to-bakery", [Portal("upper-right")], []);
            return;
        }
        if (region != "upper-right") return;
        if (ContinuePredictiveBakeryVisit()) return;
        if (held != 0) { SupplyHeld(1, held); return; }
        if (NeedsAim("right", "lower-left")) { Start(1, "aim-wash-cannon", [Cannon("aim-cannon", "right")], [CannonId("right")]); return; }
        AssignBowls();
        if (TrySupplyUrgentMixerPrerequisite()) return;
        if (DirtyCount() > 0 && (!RightSupplyNeeded() || DirtyCount() >= 2 || FreeCleanCount() == 0))
        { if (!TryFinishActiveBakeryBeforeWash()) Start(1, "board-wash-cannon", [Cannon("board-cannon", "right")], [CannonId("right")]); return; }
        foreach (var bowl in Stations("bowl").OrderByDescending(s => s.Position.X))
        {
            if (!bowlFlavors.TryGetValue(bowl.EntityId, out int flavor) || !Free(bowl.EntityId) || HeldByAnyone(bowl.EntityId)) continue;
            var ingredients = Food(bowl.EntityId).IngredientIds;
            if (WaitForSpeculativeFlavor(bowl.EntityId, flavor)) return;
            if (!ingredients.Contains(CarnivalRecipes.Flour.Id)) { Supply(1, CarnivalRecipes.Flour, bowl.EntityId, true); return; }
            if (!ingredients.Contains(CarnivalRecipes.Egg.Id)) { Supply(1, CarnivalRecipes.Egg, bowl.EntityId, true); return; }
            if (ingredients.Contains(flavor)) continue;
            int board = Board("upper-right", false);
            if (EmptyAttachment(board) && Free(board) && !Stations("chop").Any(s => Attached(s.EntityId) is int id && id != 0 && Food(id).IngredientIds.Contains(flavor)))
            {
                if (!TrySupplyPreparedFlavor(bowl.EntityId, CarnivalRecipes.Ingredients[flavor]))
                    Supply(1, CarnivalRecipes.Ingredients[flavor], board, false);
                return;
            }
            // Finish the near bowl before throwing across it toward the far
            // bowl: a compatible nearer catcher can consume the projectile.
            return;
        }
        if (DirtyCount() > 0 && !TryFinishActiveBakeryBeforeWash()) Start(1, "board-wash-cannon", [Cannon("board-cannon", "right")], [CannonId("right")]);
    }

    private void Central(int player)
    {
        if (Region(player) != "center") throw new InvalidOperationException("Central cook left the measured center platform.");
        if (Held(player) != 0)
        {
            int held = Held(player);
            if (IsPlate(Entity(held)))
            {
                int target = MealOutput(held, mealPlates.FirstOrDefault(p => p.Value == held).Key);
                if (target != 0) Start(player, "stage-resumed-plate", [A("place", target)], [held, target]);
                return;
            }
            if (bowlHomes.TryGetValue(held, out int home) && EmptyFood(held))
            { Start(player, "restore-empty-bowl", [A("place", home)], [held, home]); return; }
            throw new InvalidOperationException("Idle central cook holds an unassigned item; resume after completing or placing it: " + held);
        }
        // Native OverDoing begins above 1.3 * cookingTime; burning begins above
        // 2 * cookingTime. A completed matching bun is reserved before an onion
        // enters heat, so its legal harvest can take priority at this threshold.
        if (Stations("pan").Any(s => Free(s.EntityId) && N(Entity(s.EntityId)?["cookingTime"]) > 0 &&
            N(Entity(s.EntityId)?["cookingProgress"]) >= 1.3 * N(Entity(s.EntityId)?["cookingTime"])) && AddUnplatedOnions(player)) return;
        if (FireLoaded(player)) return;
        if (TryAssemble(player, true)) return;
        if (TryDirectCleanPassAssembly(player)) return;
        // Free the one-item clean handoff before the washer tries to return its
        // next plate. Dirty stacks are relayed before speculative preparation
        // when the washer has no work or the clean plate pool is exhausted.
        if (RelayClean(player)) return;
        if ((AvailablePlates().Length == 0 || Region(1) == "lower-left" && I(Entity(Single("sink"))?["plateCount"]) == 0) && RelayDirty(player)) return;
        if (AddUnplatedOnions(player)) return;
        if (TryLoadBufferedSausage(player)) return;
        // Parallel onions start their native clock before unrelated future bun
        // construction. Ready FIFO plates and urgent shared logistics stay ahead.
        if (options.ParallelOnionHeat && TryPrioritizedParallelOnion(player)) return;
        if (BuildUnplatedHotdog(player)) return;
        if (player == 3 && PrepareDonut(player)) return;
        if (TryAssemble(player)) return;
        if (MoveChoppedOnion(player)) return;
        if (ChopAvailable(player)) return;
        if (PrepareDonut(player)) return;
        if (RelayRawIntoVessel(player)) return;
        if (BufferChoppedBun(player)) return;
        if (TryParkBufferedSausage(player)) return;
        if (RelayDirty(player)) return;
    }

    private bool FireLoaded(int player)
    {
        foreach (string side in new[] { player == 0 ? "left" : "right", player == 0 ? "right" : "left" })
        {
            int cannon = CannonId(side), passenger = side == "left" ? 2 : 1;
            if (I(Entity(cannon)?["cannonLoadedEntityId"]) != I(chefs[passenger]["entityId"]) ||
                B(chefs[passenger]["controlsEnabled"]) || Entity(cannon)?["cannonState"]?.ToString() != "Load" ||
                B(Entity(cannon)?["cannonFlying"]) || !Free(cannon) || !CanReserveCannonFlight(cannon, passenger)) continue;
            int button = I(Entity(cannon)?["cannonButtonEntityId"]);
            if (PreferOtherCentral(CentralPreferenceKind.Fire,player,button,cannon,button)) continue;
            var action = Cannon("fire-cannon", side); action["passengerPlayer"] = passenger;
            action["destinationRegion"] = side == "left" ? "lower-right" : "lower-left";
            return StartCannonFire(player, "fire-" + side, action, cannon, button);
        }
        return false;
    }

    private bool TryAssemble(int player, bool headOnly = false, int? exactIndex = null, int? exactPlate = null, int? exactBasket = null, bool waitForNativeFryer = false, bool inspectTwoSauceOnly = false, bool inspectOnly = false)
    {
        // The clean-pass shortcut may inspect only an exact earlier two-sauce
        // meal. Reuse this entire native admission path without creating Work,
        // changing locks/recipe bookkeeping, or emitting helper diagnostics.
        if(inspectTwoSauceOnly&&(!exactIndex.HasValue||!exactPlate.HasValue||waitForNativeFryer||exactBasket.HasValue||
            exactIndex.Value<0||exactIndex.Value>=recipes.Length||
            recipes[exactIndex.Value].RequiredInputs.Count(i=>i==CarnivalRecipes.Mustard||i==CarnivalRecipes.Ketchup)!=2))
            throw new ArgumentException("Assembly inspection is restricted to an exact ordinary two-sauce meal and plate.");
        if(inspectOnly&&(!exactIndex.HasValue||!exactPlate.HasValue||waitForNativeFryer||exactBasket.HasValue))
            throw new ArgumentException("Assembly inspection requires an exact ordinary meal and plate without a new waiting or vessel override.");
        bool inspecting=inspectTwoSauceOnly||inspectOnly;
        foreach (var (index, recipe) in Pending())
        {
            if (headOnly && index != delivered || exactIndex.HasValue && index != exactIndex.Value || !CanAllocatePlate(index)) continue;
            int plate = exactPlate.HasValue ? AvailablePlates().Where(e=>Id(e)==exactPlate.Value).Select(Id).FirstOrDefault() : AvailablePlate(player);
            if (plate == 0) return false;
            var actions = new List<JsonObject>(); var resources = new List<int> { plate };
            int basket = 0, unplated = mealFoods.GetValueOrDefault(index), unplatedSource = 0;
            if (IsDonut(recipe))
            {
                basket = Stations("basket").Select(s => s.EntityId).FirstOrDefault(id => (!exactBasket.HasValue || id == exactBasket.Value) && FryerAvailableForPlating(id, index) &&
                    (!basketAssignments.TryGetValue(id, out int assignment) || assignment == index) &&
                    (CarnivalRecipes.MatchRecipe(Entity(id), recipe.Id, false).ReadyToDeliver || waitForNativeFryer && NativeNearReadyFryer(id, index)));
                // MatchRecipe(requirePlate:false) still requires complete native preparation evidence.
                if (basket == 0) continue;
                if (!fryerRescues.ContainsKey(basket)) resources.Add(basket);
            }
            else
            {
                unplatedSource = AttachmentParent(unplated);
                if (unplated != 0 && entities.ContainsKey(unplated) && unplatedSource != 0 && Free(unplated, unplatedSource))
                { resources.Add(unplated); resources.Add(unplatedSource); }
                else continue;
                int baseRecipe = recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) ? 472326 : 296560;
                if (!CarnivalRecipes.MatchRecipe(Entity(unplated), baseRecipe, false).ReadyToDeliver) continue;
            }
            int output = MealOutput(plate, index);
            if (output == 0) continue;
            resources.Add(output);
            NearReadyFryer? nearFryer = null;
            if (waitForNativeFryer)
            {
                if (!exactBasket.HasValue || !exactIndex.HasValue || basket == 0 || !IsDonut(recipe) ||
                    (nearFryer = PlanNearReadyFryer(player, index, plate, basket, output)) is null) continue;
                resources.Add(nearFryer.Home);
            }
            var sauces = recipe.RequiredInputs.Where(i => i == CarnivalRecipes.Mustard || i == CarnivalRecipes.Ketchup).OrderBy(i => i == CarnivalRecipes.Mustard ? 0 : 1).ToArray();
            int temp = 0;
            SauceStagingSelection? staging = null;
            if (sauces.Length > 0)
            {
                resources.Add(Single("condiment")); resources.Add(Single("condiment-switch"));
                if (sauces.Length == 2 && options.CooperativeSauces && TryStartCooperativeSauces(player, index, recipe, plate,
                    unplated, unplatedSource, output, resources, inspecting)) return true;
                if (sauces.Length == 2)
                {
                    // Assemble consumes the unplated food and recovers the
                    // plate, leaving this already-reserved surface vacant.
                    if (options.NearSauceStaging)
                    {
                        staging = SelectNearSauceStaging(player, index, unplatedSource, unplated, plate, output,
                            recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) ? 472326 : 296560, resources, emitRefusal:!inspecting);
                        temp = staging?.Counter ?? 0;
                    }
                    else
                    {
                        temp = unplatedSource;
                        if (temp == 0) temp = EmptyCenterCounter(new Point2(20.4, -12), output, true);
                    }
                    if (temp == 0) continue;
                    resources.Add(temp);
                }
                actions.Add(SauceSwitch(sauces[0]));
            }
            actions.Add(A("take", plate));
            if (nearFryer is not null) { actions.Add(A("navigate", nearFryer.Home)); actions.Add(FryerAction("cook", basket)); }
            if (basket != 0) actions.Add(A("combine", basket));
            else
            {
                // Native plate-under-food consumes the food entity. Its stable
                // parent counter must remain the referral used for recovery.
                actions.Add(A("assemble", unplatedSource));
            }
            for (int i = 0; i < sauces.Length; i++)
            {
                if (i > 0) { actions.Add(A("place", temp)); actions.Add(SauceSwitch(sauces[i])); actions.Add(A("take", temp)); }
                var apply = A("apply", Single("condiment")); apply["expectedIngredientId"] = sauces[i].Id; actions.Add(apply);
            }
            actions.Add(A("place", output));
            if (!Free(resources.ToArray())) continue;
            if(inspecting)return workers[player] is null;
            assembling.Add(index);
            plating.Add(index);
            bool started = Start(player, "assemble-meal-" + (index + 1) + "-" + recipe.Name, actions, resources, () =>
            {
                CompleteSauceStaging(staging, recipe.Id);
                if (!CarnivalRecipes.MatchRecipe(Entity(plate), recipe.Id).ReadyToDeliver)
                    throw new InvalidOperationException("Completed meal failed native recipe shape validation: " + recipe.Name);
                CompleteDirectCleanPass(player,plate,recipe.Id);
                if (nearFryer is not null) CompleteNearReadyFryer(nearFryer);
                mealPlates[index] = plate; mealFoods.Remove(index); assembling.Remove(index); plating.Remove(index);
                if (basket != 0) BeginFryerRestoration(basket, player);
            });
            if (!started) { assembling.Remove(index); plating.Remove(index); }
            else
            {
                if (staging is not null) BeginSauceStaging(staging, workers[player]!);
                if (nearFryer is not null) RegisterNearReadyFryer(nearFryer, workers[player]!);
                if (basket != 0) BeginFryerPlating(basket, player);
            }
            return started;
        }
        return false;
    }

    private bool IsSauceParticipant(int player) => sauceLease is { } lease && (player == lease.Owner || player == lease.Helper);

    private bool TryStartCooperativeSauces(int owner, int index, NativeRecipe recipe, int plate, int food, int source, int output, List<int> resources, bool inspectOnly = false)
    {
        int helper = owner == 0 ? 3 : 0;
        if (owner is not (0 or 3) || sauceLease is not null || workers[owner] is not null || workers[helper] is not null ||
            Held(owner) != 0 || Held(helper) != 0 || !B(chefs[owner]["controlsEnabled"]) || !B(chefs[helper]["controlsEnabled"]) ||
            Region(owner) != "center" || Region(helper) != "center" || IsEarlyOnionParticipant(owner) || IsEarlyOnionParticipant(helper) ||
            IsBakeryParticipant(owner) || IsBakeryParticipant(helper) || IsFryerParticipant(owner) || IsFryerParticipant(helper) || IsPotParticipant(owner) || IsPotParticipant(helper) ||
            source == 0 || food == 0 || !Free(resources.ToArray()))
            return false;
        if(inspectOnly)return true;
        var lease = new SauceLease(index, recipe, owner, helper, plate, food, source, output, Single("condiment"),
            Single("condiment-switch"), Frame, resources.Where(id => id != 0).Distinct().ToArray());
        // A single coordinator owns these locks. Child Work objects deliberately
        // own no duplicate locks, so either completion cannot release the other's
        // station/food protection before the native meal has been staged.
        foreach (int id in lease.Resources) reserved.Add(id);
        assembling.Add(index); plating.Add(index); sauceLease = lease;
        Log("cooperativeSauceLeaseAcquired", SauceStatus());
        StartSauceChild(lease, owner, "prepare-held-plate", [A("take", plate), A("assemble", source), A("navigate", lease.Dispenser)],
            () => lease.OwnerDone = true);
        StartSauceChild(lease, helper, "prepare-mustard-switch", [A("navigate", lease.Button), SauceSwitch(CarnivalRecipes.Mustard)],
            () => lease.HelperDone = true);
        return true;
    }

    private void StartSauceChild(SauceLease lease, int player, string name, IEnumerable<JsonObject> actions, Action complete)
    {
        if (!Start(player, "cooperative-sauce-" + (lease.Index + 1) + "-" + name, actions, [], complete))
            throw new InvalidOperationException("Cooperative sauce participant was assigned a conflicting job.");
    }

    private void RequireSaucePlate(SauceLease lease, int expectedRecipe)
    {
        if (Held(lease.Owner) != lease.Plate || !CarnivalRecipes.MatchRecipe(Entity(lease.Plate), expectedRecipe).ReadyToDeliver)
            throw new InvalidOperationException("Cooperative sauce barrier lacks the exact held native plate/recipe shape: " + expectedRecipe);
    }

    private void RequireSauceIndex(SauceLease lease, int expected)
    {
        if (Entity(lease.Dispenser)?["switchIndex"] is not { } observed || I(observed) != expected)
            throw new InvalidOperationException("Cooperative sauce barrier observed the wrong native dispenser index; expected " + expected);
    }

    private void SaucePhaseChanged(SauceLease lease, SaucePhase next)
    {
        lease.Phase = next; lease.PhaseFrame = Frame;
        lease.OwnerDone = lease.HelperDone = false;
        Log("cooperativeSauceBarrier", SauceStatus());
    }

    private void AdvanceCooperativeSauces()
    {
        if (sauceLease is not { } lease) return;
        if (Frame - lease.StartFrame > 1200 || Frame - lease.PhaseFrame > 600)
            throw new TimeoutException("Cooperative sauce session exceeded its logical frame timeout.");
        if (Held(lease.Helper) != 0 || !B(chefs[lease.Owner]["controlsEnabled"]) || !B(chefs[lease.Helper]["controlsEnabled"]) ||
            Region(lease.Owner) != "center" || Region(lease.Helper) != "center")
            throw new InvalidOperationException("Cooperative sauce participants lost their native empty-helper/control/center constraints.");
        if (lease.Resources.Any(id => !reserved.Contains(id)))
            throw new InvalidOperationException("Cooperative sauce shared resource lease was released before native completion.");
        switch (lease.Phase)
        {
            case SaucePhase.Preparing:
                if (!lease.OwnerDone || !lease.HelperDone) return;
                RequireSaucePlate(lease, 296560);
                RequireSauceIndex(lease, CarnivalRecipes.MustardSwitchIndex);
                SaucePhaseChanged(lease, SaucePhase.ApplyFirst);
                var mustard = A("apply", lease.Dispenser); mustard["expectedIngredientId"] = CarnivalRecipes.Mustard.Id;
                StartSauceChild(lease, lease.Owner, "apply-mustard", [mustard], () => lease.OwnerDone = true);
                return;
            case SaucePhase.ApplyFirst:
                RequireSauceIndex(lease, CarnivalRecipes.MustardSwitchIndex);
                if (!lease.OwnerDone) return;
                RequireSaucePlate(lease, 158500);
                SaucePhaseChanged(lease, SaucePhase.SwitchSecond);
                StartSauceChild(lease, lease.Helper, "switch-ketchup", [SauceSwitch(CarnivalRecipes.Ketchup)], () => lease.HelperDone = true);
                return;
            case SaucePhase.SwitchSecond:
                RequireSaucePlate(lease, 158500);
                if (!lease.HelperDone) return;
                RequireSauceIndex(lease, CarnivalRecipes.KetchupSwitchIndex);
                SaucePhaseChanged(lease, SaucePhase.ApplySecond);
                var ketchup = A("apply", lease.Dispenser); ketchup["expectedIngredientId"] = CarnivalRecipes.Ketchup.Id;
                StartSauceChild(lease, lease.Owner, "apply-ketchup", [ketchup], () => lease.OwnerDone = true);
                return;
            case SaucePhase.ApplySecond:
                RequireSauceIndex(lease, CarnivalRecipes.KetchupSwitchIndex);
                if (!lease.OwnerDone) return;
                RequireSaucePlate(lease, lease.Recipe.Id);
                SaucePhaseChanged(lease, SaucePhase.StageMeal);
                StartSauceChild(lease, lease.Owner, "stage-complete-meal", [A("place", lease.Output)], () => lease.OwnerDone = true);
                return;
            case SaucePhase.StageMeal:
                if (!lease.OwnerDone) return;
                if (Held(lease.Owner) != 0 || Held(lease.Helper) != 0 || Attached(lease.Output) != lease.Plate ||
                    !CarnivalRecipes.MatchRecipe(Entity(lease.Plate), lease.Recipe.Id).ReadyToDeliver ||
                    workers[lease.Owner] is not null || workers[lease.Helper] is not null)
                    throw new InvalidOperationException("Cooperative sauce completion lacks its exact native staged meal and neutral participants.");
                mealPlates[lease.Index] = lease.Plate; mealFoods.Remove(lease.Index);
                Log("cooperativeSauceComplete", SauceStatus());
                ReleaseSauceLease(lease);
                return;
        }
    }

    private void ReleaseSauceLease(SauceLease lease)
    {
        foreach (int id in lease.Resources) reserved.Remove(id);
        assembling.Remove(lease.Index); plating.Remove(lease.Index);
        sauceLease = null;
    }

    private void AbortCooperativeSauces(string reason)
    {
        if (sauceLease is not { } lease) return;
        var details = SauceStatus(); details["reason"] = reason;
        Log("cooperativeSauceAborted", details);
        workers[lease.Owner] = workers[lease.Helper] = null;
        ReleaseSauceLease(lease);
        cooperativeAttemptFailed = true;
    }

    private JsonObject SauceStatus() => sauceLease is not { } lease ? new JsonObject() : new JsonObject
    {
        ["mealIndex"] = lease.Index, ["recipeId"] = lease.Recipe.Id, ["owner"] = lease.Owner, ["helper"] = lease.Helper,
        ["plate"] = lease.Plate, ["food"] = lease.Food, ["source"] = lease.Source, ["output"] = lease.Output,
        ["dispenser"] = lease.Dispenser, ["button"] = lease.Button, ["phase"] = lease.Phase.ToString(),
        ["gameplayFrame"] = Frame, ["startFrame"] = lease.StartFrame, ["phaseFrame"] = lease.PhaseFrame,
        ["ownerDone"] = lease.OwnerDone, ["helperDone"] = lease.HelperDone,
        ["observedSwitchIndex"] = Entity(lease.Dispenser)?["switchIndex"]?.DeepClone(),
        ["heldPlateIngredientIds"] = new JsonArray(Food(lease.Plate).IngredientIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
        ["resources"] = new JsonArray(lease.Resources.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray())
    };

    private bool BuildUnplatedHotdog(int player, int? exactPot = null, bool waitForExactPot = false)
    {
        foreach (var (index, recipe) in Pending().Where(p => !IsDonut(p.Recipe) && !mealFoods.ContainsKey(p.Index)))
        {
            int board = BufferedBunSource();
            bool usesBuffer = board != 0;
            if (board == 0) board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && Chopped(Attached(id), CarnivalRecipes.Bun.Id));
            int pot = Stations("pot").Select(s => s.EntityId).FirstOrDefault(id => (!exactPot.HasValue || id == exactPot.Value) &&
                PotAvailableForHotdog(id) && (Cooked(id, CarnivalRecipes.Frankfurter.Id) ||
                    waitForExactPot && exactPot == id && NativeNearReadyPot(id)));
            bool onions = recipe.RequiredInputs.Contains(CarnivalRecipes.Onion);
            // An early-onion job owns this meal's onion, while its plain base
            // remains independent work for either central chef.
            int pan = onions && !HasEarlyOnionLease(index) ? Stations("pan").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && Cooked(id, CarnivalRecipes.Onion.Id)) : 0;
            if (board == 0 || pot == 0) continue;
            int food = Attached(board);
            if (TryPlatedHotdogBase(player,index,board,food,pot,usesBuffer,waitForExactPot&&exactPot==pot)) return true;
            int destination = usesBuffer ? board : EmptyCenterCounter(new Point2(18.8, -13.2), 0, index == delivered);
            bool safetyBoardFallback = destination == 0 && exactPot.HasValue &&
                (waitForExactPot ? NearReadyPotSource(board, food) : CanStageExactPotHarvestOnSource(player, pot, board, food));
            if (safetyBoardFallback) destination = board;
            // The FIFO head may briefly reuse its source board if every storage
            // counter is occupied. Ordinary later production keeps its limit;
            // only the checked exact-pot safety branch above can also do so.
            if (destination == 0 && index == delivered) destination = board;
            if (destination == 0) return false;
            var nearPot = waitForExactPot ? PlanNearReadyPot(player, index, board, food, pot, pan, destination) : null;
            if (waitForExactPot && nearPot is null) continue;
            var actions = new List<JsonObject> { A("take", board) };
            if (nearPot is not null) { actions.Add(A("navigate", pot)); actions.Add(PotAction("cook", pot)); }
            actions.Add(A("combine", pot));
            if (pan != 0) actions.Add(A("combine", pan));
            actions.Add(A("place", destination));
            bool rescuedPot = potRescues.ContainsKey(pot);
            int sourceOrdinal = I(Entity(board)?["observedOrdinal"]), foodOrdinal = I(Entity(food)?["observedOrdinal"]);
            int[] workResources = [board, food, rescuedPot ? 0 : pot, pan, destination, nearPot?.Home ?? 0];
            if (!Free(workResources)) continue;
            assembling.Add(index);
            bool started = Start(player, "unplated-hotdog-" + (index + 1), actions, workResources, () =>
            {
                int baseRecipe = pan != 0 ? 472326 : 296560;
                if (!CarnivalRecipes.MatchRecipe(Entity(food), baseRecipe, false).ReadyToDeliver)
                    throw new InvalidOperationException("Unplated hotdog failed its native boiled/pan-fried recipe validation.");
                if (nearPot is not null) CompleteNearReadyPot(nearPot);
                if (safetyBoardFallback && (!SameObservedEntity(board, sourceOrdinal) || !SameObservedEntity(food, foodOrdinal) ||
                    Attached(board) != food || Held(player) != 0))
                    throw new InvalidOperationException("Exact-pot safety harvest did not stage the same native hotdog on its retained source board.");
                if (usesBuffer)
                {
                    if (bufferedBun != food || bunBufferCounter != destination || Attached(destination) != food || Held(player) != 0)
                        throw new InvalidOperationException("Buffered bun harvest lost its reserved native item or storage counter.");
                    bufferedBun = bunBufferCounter = 0;
                    Log("plannerBunBufferHarvested", new JsonObject { ["food"] = food, ["counter"] = destination, ["mealIndex"] = index, ["frame"] = Frame });
                }
                mealFoods[index] = food; assembling.Remove(index);
                if (rescuedPot)
                {
                    if (Attached(destination) != food || Held(player) != 0)
                        throw new InvalidOperationException("Rescued-pot hotdog was not staged on its exact reserved output.");
                    BeginPotRestoration(pot, player, index);
                }
            });
            if (!started) assembling.Remove(index);
            else
            {
                if (nearPot is not null) RegisterNearReadyPot(nearPot, workers[player]!);
                if (rescuedPot) BeginPotConsumption(pot, player, index);
                if (safetyBoardFallback)
                    Log("nativeHeatSourceBoardReserved", new JsonObject { ["frame"] = Frame, ["player"] = player,
                        ["pot"] = pot, ["board"] = board, ["food"] = food, ["orderIndex"] = index,
                        ["boardOrdinal"] = sourceOrdinal, ["foodOrdinal"] = foodOrdinal,
                        ["reason"] = "urgent selected pot harvest reuses its own emptied bun board because ordinary storage is occupied" });
                if (options.StagedResourceRelease)
                    workers[player]!.ReleasableTransfer = new(food, board, pot, AttachmentParent(pot),
                        Entity(food)?["observedOrdinal"]?.GetValue<int>() ?? -1,
                        Entity(board)?["observedOrdinal"]?.GetValue<int>() ?? -1,
                        Entity(pot)?["observedOrdinal"]?.GetValue<int>() ?? -1);
            }
            return started;
        }
        return false;
    }

    private bool IsLooseChoppedBun(int id) => id != 0 && Chopped(id, CarnivalRecipes.Bun.Id) &&
        Food(id).IngredientIds is [var ingredient] && ingredient == CarnivalRecipes.Bun.Id;

    private int BufferedBunSource()
    {
        if (!options.BufferChoppedBuns || bufferedBun == 0 || !Free(bufferedBun, bunBufferCounter)) return 0;
        if (Attached(bunBufferCounter) != bufferedBun || !IsLooseChoppedBun(bufferedBun) || HeldByAnyone(bufferedBun))
            throw new InvalidOperationException("Reserved chopped-bun buffer changed outside its native transfer job.");
        return bunBufferCounter;
    }

    private bool BufferChoppedBun(int player)
    {
        if (!options.BufferChoppedBuns || bufferedBun != 0 || Held(player) != 0 ||
            ComponentDemand(CarnivalRecipes.Bun.Id) < 2 || LooseIngredientCount(CarnivalRecipes.Bun.Id) > 2) return false;
        int board = Board("upper-left", false), food = Attached(board);
        if (!IsLooseChoppedBun(food) || !Free(board, food)) return false;
        // Use ordinary center storage, preserving the FIFO workspace and both
        // island handoff lanes. The same counter later holds the unplated meal.
        int destination = EmptyCenterCounter(Station(board)!.Position);
        if (destination == 0) return false;
        if (PreferOtherCentral(CentralPreferenceKind.BunBuffer,player,board,board,food,destination)) return false;
        bool started = Start(player, "buffer-native-chopped-bun", [A("take", board), A("place", destination)],
            [board, food, destination], () =>
            {
                if (Attached(board) != 0 || Attached(destination) != food || Held(player) != 0 || !IsLooseChoppedBun(food))
                    throw new InvalidOperationException("Bun buffering did not preserve the exact native chopped item and empty source board.");
                Log("plannerBunBuffered", new JsonObject { ["food"] = food, ["counter"] = destination, ["source"] = board, ["frame"] = Frame });
            });
        if (started) { bufferedBun = food; bunBufferCounter = destination; }
        return started;
    }

    private bool AddUnplatedOnions(int player, int selectedPan = 0)
    {
        if (selectedPan == 0 && TryCombineEarlyOnion(player)) return true;
        foreach (var (index, recipe) in Pending().Where(p => p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) && !HasEarlyOnionLease(p.Index)))
        {
            int food = mealFoods.GetValueOrDefault(index), source = AttachmentParent(food);
            if (food == 0 || source == 0 || !Free(food, source) || Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id)) continue;
            int pan = Stations("pan").Select(s => s.EntityId).FirstOrDefault(id => (selectedPan == 0 || id == selectedPan) && Free(id) && Cooked(id, CarnivalRecipes.Onion.Id));
            if (pan == 0) return false;
            assembling.Add(index);
            bool started = Start(player, "finish-unplated-onions-" + (index + 1), [A("take", source), A("combine", pan), A("place", source)], [food, source, pan], () =>
            {
                if (!CarnivalRecipes.MatchRecipe(Entity(food), 472326, false).ReadyToDeliver)
                    throw new InvalidOperationException("Onion completion did not produce the expected native unplated hotdog.");
                assembling.Remove(index);
            });
            if (!started) assembling.Remove(index);
            return started;
        }
        return false;
    }

    private bool PrepareDonut(int player, int selectedBowl = 0)
    {
        AssignBowls();
        foreach (var bowl in Stations("bowl").Where(b => selectedBowl == 0 || b.EntityId == selectedBowl))
        {
            int id = bowl.EntityId;
            if (!Free(id) || !bowlHomes.TryGetValue(id, out int home)) continue;
            int assigned = bowlAssignments.GetValueOrDefault(id, -1);
            if (selectedBowl != 0 && TryTransferNearReadyDough(player, id, assigned, home)) return true;
            if (assigned >= 0 && assigned < recipes.Length && AssignedDoughReady(Food(id), recipes[assigned]) && BakeryFryingAdmitted(id, assigned))
            {
                int basket = Stations("basket").Select(s => s.EntityId).FirstOrDefault(e => EmptyFood(e) && Free(e));
                if (basket == 0) continue;
                bool started = Start(player, "transfer-mixed-dough", [A("take", id), A("combine", basket), A("place", home)], BakeryTransferResources(id, basket, home),
                    () =>
                    {
                        if (!EmptyFood(id) || Attached(home) != id || Held(player) != 0 || !AssignedDoughReady(Food(basket), recipes[assigned]))
                            throw new InvalidOperationException("Mixed dough did not reach its assigned fryer intact or its empty bowl did not return home.");
                        basketAssignments[basket] = assigned; CompleteBakeryTransfer(id);
                        bowlFlavors.Remove(id); bowlAssignments.Remove(id);
                    });
                if (started) BeginBakeryTransfer(id, player);
                return started;
            }
            if (!bowlFlavors.TryGetValue(id, out int flavor) || Food(id).IngredientIds.Contains(flavor)) continue;
            int board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(e => Free(e) && Chopped(Attached(e), flavor));
            if (board != 0) return Start(player, "add-chopped-donut-flavor", [A("take", board), A("place", id)], [board, Attached(board), id]);
        }
        return false;
    }

    private static bool AssignedDoughReady(FoodObservation food, NativeRecipe recipe) => IsDonut(recipe) && MixedReady(food) &&
        food.IngredientIds.Order().SequenceEqual(recipe.RequiredInputs.Select(i => i.Id).Order());

    private bool MoveChoppedOnion(int player)
    {
        if (options.ParallelOnionHeat) return TryStartParallelOnion(player);
        // A speculative onion must wait off heat. Seed0's first onion order is
        // index4; cooking it during startup brought a pan to23.70/24 seconds
        // before even the second delivery in the observed four-a trial.
        int recipients = Pending().Count(p => p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion) &&
            mealFoods.TryGetValue(p.Index, out int meal) && !Food(meal).IngredientIds.Contains(CarnivalRecipes.Onion.Id) &&
            CarnivalRecipes.MatchRecipe(Entity(meal), 296560, false).ReadyToDeliver);
        int occupied = Stations("pan").Count(s => Food(s.EntityId).IngredientIds.Contains(CarnivalRecipes.Onion.Id));
        if (recipients <= occupied) return TryStartEarlyOnion(player);
        int board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && Chopped(Attached(id), CarnivalRecipes.Onion.Id));
        int pan = Stations("pan").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && EmptyFood(id));
        return board != 0 && pan != 0 && Start(player, "start-native-onion-fry", [A("take", board), A("place", pan)], [board, Attached(board), pan]);
    }

    private bool ChopAvailable(int player)
    {
        var position = KitchenModel.Position(chefs[player]["position"]);
        var board = Stations("chop").Where(s => Free(s.EntityId) && Attached(s.EntityId) != 0 &&
                KitchenModel.Components(Entity(Attached(s.EntityId))!).Contains("WorkableItem"))
            .OrderBy(s => s.Position.Distance(position)).FirstOrDefault();
        return board is not null && Start(player, "chop-native-item", [A("chop", board.EntityId)], [board.EntityId, Attached(board.EntityId)]);
    }

    private bool RelayDirty(int player)
    {
        int source = Single("dirty-return"), destination = Counter(15.6, -19.2);
        return I(Entity(source)?["plateCount"]) > 0 && EmptyAttachment(destination) && Free(source, destination) &&
            Start(player, "relay-whole-dirty-stack", [A("take", source), A("place", destination)], [source, destination]);
    }

    private bool RelayRawIntoVessel(int player)
    {
        foreach (int counter in new[] { Counter(15.6, -13.2), Counter(25.2, -13.2) })
        {
            int item = Attached(counter);
            if (item == 0 || !Free(item, counter) || IsPlate(Entity(item))) continue;
            int ingredient = Food(item).IngredientIds.Length == 1 ? Food(item).IngredientIds[0] : 0;
            int destination = 0;
            bool addressed = counterSupplies.TryGetValue(counter, out var supply);
            if (addressed)
            {
                if (ingredient != supply.Ingredient || !entities.ContainsKey(supply.Vessel))
                    throw new InvalidOperationException("Addressed ingredient handoff changed its item or lost its target vessel.");
                destination = supply.Vessel;
                if (!Free(destination)) continue;
                if (Food(destination).IngredientIds.Contains(ingredient))
                    throw new InvalidOperationException("Addressed vessel already contains the pending handoff ingredient; refusing a duplicate.");
            }
            else if (ingredient == CarnivalRecipes.Frankfurter.Id)
                destination = Stations("pot").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && EmptyFood(id));
            else if (ingredient == CarnivalRecipes.Flour.Id || ingredient == CarnivalRecipes.Egg.Id)
                destination = Stations("bowl").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && bowlFlavors.ContainsKey(id) && !Food(id).IngredientIds.Contains(ingredient));
            if (destination != 0)
            {
                if (PreferOtherCentral(CentralPreferenceKind.RawRelay,player,counter,item,counter,destination)) continue;
                int exactTarget = destination;
                int before = Food(exactTarget).IngredientIds.Count(i => i == ingredient);
                return Start(player, "load-addressed-counter-ingredient", [A("take", counter), A("place", exactTarget)], [item, counter, exactTarget], () =>
                {
                    if (Food(exactTarget).IngredientIds.Count(i => i == ingredient) != before + 1)
                        throw new InvalidOperationException("Counter handoff did not add exactly one ingredient to its reserved vessel.");
                    if (addressed) counterSupplies.Remove(counter);
                });
            }
        }
        return false;
    }

    private bool RelayClean(int player)
    {
        int source = Counter(15.6, -20.4), plate = Attached(source);
        if (plate == 0 || !IsPlate(Entity(plate)) || !EmptyFood(plate) || !Free(source, plate)) return false;
        int destination = EmptyCenterCounter(new Point2(19.2, -16.8));
        return destination != 0 && Start(player, "clear-clean-plate-handoff", [A("take", source), A("place", destination)], [source, plate, destination]);
    }

    private bool Supply(int player, NativeIngredient ingredient, int destination, bool vessel)
    {
        int source = model.Stations.Single(s => s.Ingredient == ingredient.Name).EntityId;
        var actions = new List<JsonObject> { A("take", source) };
        int requestedVessel = destination, handoff = 0;
        if (vessel && counterSupplies.Values.Any(s => s.Vessel == requestedVessel)) return false;
        int nearPot = NearSupplyVessel(2);
        if(vessel&&player==2&&ingredient==CarnivalRecipes.Frankfurter&&destination!=nearPot&&TryConditionalFarPot(source,destination))return true;
        // Native eight-a demonstrated a far-pot throw leaving the kitchen
        // while the nearer pot stayed full. The occupied onion board changes
        // that lane; use the same addressed handoff already used for far bowls.
        JsonObject? directGuard = vessel ? DirectSupplyGuard(player, destination) : null;
        bool direct = options.DirectVesselThrows && directGuard is not null;
        if (vessel && direct)
        {
            var staging = ThrowStaging(player); staging["dash"] = options.UseDash || options.UseShortDash; staging["shortDash"] = options.UseShortDash; actions.Add(staging);
            // Exact catch/contents provenance is checked inside RouteThrow.
            var action = new JsonObject { ["type"] = "throw", ["targetEntityId"] = destination, ["nativeSupplyHome"] = directGuard };
            actions.Add(action);
        }
        else if (vessel)
        {
            int pass = Counter(player == 2 ? 15.6 : 25.2, -13.2);
            if (!EmptyAttachment(pass) || !Free(pass) || counterSupplies.ContainsKey(pass)) return false;
            actions.Add(A("place", pass));
            handoff = pass;
            destination = pass;
        }
        else actions.Add(A("place", destination));
        Action? pantryChopComplete = AppendPantryChop(player, ingredient, destination, vessel, actions);
        if (!Start(player, "supply-" + ingredient.Name, actions, [source, destination, vessel ? requestedVessel : 0, direct ? I(directGuard?["home"]) : 0], pantryChopComplete)) return false;
        if (options.SharedPantryChopping && pantryChopComplete is not null)
            workers[player]!.PantrySupply = new PantrySupply(player, ingredient, source, destination,
                I(Entity(source)?["observedOrdinal"]), I(Entity(destination)?["observedOrdinal"]));
        if (handoff != 0)
        {
            counterSupplies.Add(handoff, (requestedVessel, ingredient.Id));
            Log("plannerAddressedSupply", new JsonObject { ["counter"] = handoff, ["vessel"] = requestedVessel, ["ingredient"] = ingredient.Id });
        }
        return true;
    }

    private void SupplyHeld(int player, int held)
    {
        var food = Food(held);
        int ingredient = food.IngredientIds.FirstOrDefault();
        if (ingredient == 0)
            ingredient = CarnivalRecipes.Ingredients.Values.FirstOrDefault(i => Entity(held)?["name"]?.ToString().StartsWith(i.Name, StringComparison.OrdinalIgnoreCase) == true)?.Id ?? 0;
        if (player == 2 && ingredient == CarnivalRecipes.Frankfurter.Id)
        {
            int pot = Stations("pot").OrderBy(s => s.Position.X).Select(s => s.EntityId).FirstOrDefault(e => Free(e) && EmptyFood(e));
            if (pot == 0 || counterSupplies.Values.Any(s => s.Vessel == pot)) return;
            var directGuard = DirectSupplyGuard(player, pot);
            if (options.DirectVesselThrows && directGuard is not null)
            {
                var staging = ThrowStaging(player); staging["dash"] = options.UseDash || options.UseShortDash; staging["shortDash"] = options.UseShortDash;
                Start(player, "resume-held-sausage-throw", [staging, new JsonObject { ["type"] = "throw", ["targetEntityId"] = pot, ["nativeSupplyHome"] = directGuard }], [held, pot, I(directGuard["home"])]);
            }
            else
            {
                int pass = Counter(15.6, -13.2);
                if (!EmptyAttachment(pass) || !Free(pass) || counterSupplies.ContainsKey(pass)) return;
                if (Start(player, "resume-held-sausage-handoff", [A("place", pass)], [held, pot, pass]))
                {
                    counterSupplies.Add(pass, (pot, ingredient));
                    Log("plannerAddressedSupply", new JsonObject { ["counter"] = pass, ["vessel"] = pot, ["ingredient"] = ingredient });
                }
            }
            return;
        }
        int board = Board(player == 2 ? "upper-left" : "upper-right", ingredient == CarnivalRecipes.Onion.Id);
        if (EmptyAttachment(board) && Free(board)) Start(player, "resume-held-pantry-item", [A("place", board)], [held, board]);
    }

    private void AssignBowls()
    {
        var pending = BakeryPending().Where(p => IsDonut(p.Recipe)).ToArray();
        foreach (var bowl in Stations("bowl").OrderByDescending(s => s.Position.X))
        {
            int id = bowl.EntityId;
            if (bowlFlavors.ContainsKey(id)) continue;
            int existing = Food(id).IngredientIds.FirstOrDefault(i => i == CarnivalRecipes.Chocolate.Id || i == CarnivalRecipes.Raspberry.Id);
            var next = pending.FirstOrDefault(p => !bowlAssignments.Values.Contains(p.Index) && !basketAssignments.Values.Contains(p.Index) &&
                (existing == 0 || p.Recipe.RequiredInputs.Any(i => i.Id == existing)));
            if (next.Recipe is null || !AdmitBakeryAssignment(id, next.Index)) continue;
            bowlFlavors[id] = next.Recipe.RequiredInputs.Single(i => i == CarnivalRecipes.Chocolate || i == CarnivalRecipes.Raspberry).Id;
            bowlAssignments[id] = next.Index;
        }
        TryReprioritizeEmptyBowls();
    }

    private bool LeftSupplyNeeded() =>
        Stations("pot").Any(s => EmptyFood(s.EntityId) && Free(s.EntityId)) && ComponentDemand(CarnivalRecipes.Frankfurter.Id) > ComponentStockCount(CarnivalRecipes.Frankfurter.Id) ||
        EmptyAttachment(Board("upper-left", false)) && Free(Board("upper-left", false)) &&
            Math.Min(2, ComponentDemand(CarnivalRecipes.Bun.Id)) > LooseIngredientCount(CarnivalRecipes.Bun.Id) ||
        EmptyAttachment(Board("upper-left", true)) && Free(Board("upper-left", true)) &&
            ComponentDemand(CarnivalRecipes.Onion.Id) > ComponentStockCount(CarnivalRecipes.Onion.Id);
    private bool RightSupplyNeeded()
    {
        AssignBowls();
        return bowlFlavors.Any(p => Food(p.Key).IngredientIds.Length < 3 &&
            (!Food(p.Key).IngredientIds.Contains(CarnivalRecipes.Flour.Id) || !Food(p.Key).IngredientIds.Contains(CarnivalRecipes.Egg.Id) ||
             !Food(p.Key).IngredientIds.Contains(p.Value) && LooseIngredientCount(p.Value) == 0));
    }

    private int MealOutput(int plate, int index)
    {
        if (!options.ServiceSideHead && index == delivered && Region(2) == "upper-left")
        {
            int upper = Counter(15.6, -13.2);
            if (EmptyAttachment(upper) && Free(upper)) return upper;
        }
        // Later dishes cannot occupy the FIFO head's guaranteed service slot.
        var slots = index == delivered ? new[] { Counter(25.2, -19.2), Counter(25.2, -20.4), Counter(25.2, -18) }
            : new[] { Counter(25.2, -20.4), Counter(25.2, -18) };
        return slots
            .FirstOrDefault(id => EmptyAttachment(id) && Free(id));
    }
    private JsonObject[] AvailablePlates() => entities.Values.Where(IsPlate).Where(e => EmptyFood(Id(e)) && Free(Id(e)) && !HeldByAnyone(Id(e)))
        .Where(e => Station(Id(e))?.Regions.Contains("center") == true || Station(AttachmentParent(Id(e)))?.Regions.Contains("center") == true)
        .ToArray();
    private bool CanAllocatePlate(int index) => index == delivered || mealPlates.ContainsKey(delivered) || plating.Contains(delivered) || AvailablePlates().Length >= 2;
    private int AvailablePlate(int player) => AvailablePlates()
        .OrderBy(e => KitchenModel.Position(e["position"]).Distance(KitchenModel.Position(chefs[player]["position"]))).Select(Id).FirstOrDefault();
    private int EmptyCenterCounter(Point2 nearby, int exclude = 0, bool allowWorkspace = false) => Stations("counter").Where(s => s.EntityId != exclude && s.Regions.SequenceEqual(new[] { "center" }) &&
        (allowWorkspace || s.Position.Distance(new Point2(20.4, -16.8)) > .1) &&
        s.Position.X > 18.5 && s.Position.X < 22.1 && EmptyAttachment(s.EntityId) && Free(s.EntityId))
        .OrderBy(s => s.Position.Distance(nearby)).Select(s => s.EntityId).FirstOrDefault();
    private bool HasAccessibleHead(string region) => mealPlates.TryGetValue(delivered, out int plate) &&
        (RegionOf(plate) == region || Station(AttachmentParent(plate))?.Regions.Contains(region) == true);
    private bool IsHeadMeal(int id) => mealPlates.TryGetValue(delivered, out int plate) && plate == id && CarnivalRecipes.MatchRecipe(Entity(id), recipes[delivered].Id).ReadyToDeliver;
    private IEnumerable<JsonObject> ComponentEntities() => entities.Values.Where(e => !IsPlate(e) && !KitchenModel.Components(e).Contains("PlayerIDProvider") &&
        !(e["name"]?.ToString().EndsWith("_Rigidbody", StringComparison.Ordinal) ?? false) && e["spawnPrefab"] is null)
        .Where(e => !Food(Id(e)).IsRuined);
    private int ComponentDemand(int ingredient) => Pending().Count(p => p.Recipe.RequiredInputs.Any(i => i.Id == ingredient) &&
        (!mealFoods.TryGetValue(p.Index, out int meal) || !Food(meal).IngredientIds.Contains(ingredient)));
    private int ComponentStockCount(int ingredient) => ComponentEntities().Count(e => Food(Id(e)).IngredientIds is [var only] && only == ingredient);
    private int LooseIngredientCount(int ingredient) => ComponentEntities().Count(e => Food(Id(e)).IngredientIds is [var only] && only == ingredient &&
        !KitchenModel.Components(e).Contains("CookableContainer") && !KitchenModel.Components(e).Contains("MixableContainer"));
    private int FreeCleanCount() => entities.Values.Count(e => IsPlate(e) && EmptyFood(Id(e)) && !HeldByAnyone(Id(e)));
    private int DirtyCount() => Stations("dirty-return").Sum(s => I(Entity(s.EntityId)?["plateCount"])) +
        Stations("sink").Sum(s => I(Entity(s.EntityId)?["plateCount"])) +
        new[] { Counter(15.6, -19.2) }.Sum(id => I(Entity(Attached(id))?["plateCount"]));
    private bool IsDirty(int id) => Entity(id)?["plateStackKind"]?.ToString().Contains("dirty", StringComparison.OrdinalIgnoreCase) == true ||
        Entity(id) is { } entity && KitchenModel.Components(entity).Contains("DirtyPlateStack");
    private bool NeedsAim(string side, string expected) => model.RegionAt(KitchenModel.Position(Entity(CannonId(side))?["cannonTarget"])) != expected;
    private int CannonId(string side) => Stations("cannon").OrderBy(s => s.Position.X).ElementAt(side == "left" ? 0 : 1).EntityId;
    private int Board(string region, bool upper) => Stations("chop").Where(s => s.Regions.Contains(region)).OrderByDescending(s => s.Position.Z).ElementAt(upper ? 0 : 1).EntityId;
    private int Counter(double x, double z) => Stations("counter").OrderBy(s => s.Position.Distance(new(x, z))).First(s => s.Position.Distance(new(x, z)) < .1).EntityId;
    private int Single(string role) => Stations(role).Single().EntityId;
    private KitchenStation[] Stations(string role) => model.Stations.Where(s => s.Role == role).ToArray();
    private KitchenStation? Station(int id) => model.Stations.FirstOrDefault(s => s.EntityId == id);
    private int AttachmentParent(int child) => entities.Values.FirstOrDefault(e => I(e["attachedEntityId"]) == child) is { } parent ? Id(parent) : 0;
    private int Attached(int id) => I(Entity(id)?["attachedEntityId"]);
    private bool EmptyAttachment(int id) => Attached(id) == 0;
    private bool Empty(int id) => EmptyFood(id);
    private bool EmptyFood(int id) => Food(id).IngredientIds.Length == 0;
    private bool Free(params int[] ids) => ids.All(id => id == 0 || !reserved.Contains(id));
    private bool HeldByAnyone(int id) => chefs.Any(c => I(c["heldEntityId"]) == id);
    private int Held(int player) => I(chefs[player]["heldEntityId"]);
    private string? Region(int player) => model.RegionAt(KitchenModel.Position(chefs[player]["position"]));
    private Point2 Position(int player) => KitchenModel.Position(chefs[player]["position"]);
    private string? RegionOf(int id) => model.RegionAt(KitchenModel.Position(Entity(id)?["position"]));
    private JsonObject? Entity(int id) => entities.GetValueOrDefault(id);
    private FoodObservation Food(int id) => CarnivalRecipes.ClassifyEntity(Entity(id)).Food;
    private bool Chopped(int id, int ingredient) => id != 0 && Food(id).IngredientIds.Contains(ingredient) && Food(id).Preparation == FoodPreparation.Chopped;
    private bool Cooked(int id, int ingredient) => Food(id).Preparation == FoodPreparation.Cooked && Food(id).IngredientIds.Contains(ingredient) && !Food(id).IsRuined;
    private static bool MixedReady(FoodObservation food) => !food.IsRuined && food.DescendantsAndSelf().Any(n => n.Kind == FoodNodeKind.Mixed && n.Preparation == FoodPreparation.Mixed);
    private static bool IsPlate(JsonObject? e) => e is not null && KitchenModel.Components(e).Contains("Plate");
    private static bool IsDonut(NativeRecipe r) => r.RequiredInputs.Contains(CarnivalRecipes.Flour);
    private static int Id(JsonObject e) => I(e["id"]);
    private static int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString(), CultureInfo.InvariantCulture);
    private static double N(JsonNode? n) => KitchenModel.N(n);
    private static bool B(JsonNode? n) => n?.GetValue<bool>() == true;
    private int Frame => I(state["gameplayFrame"]);
    private JsonObject A(string type, int station) => new() { ["type"] = type, ["station"] = station.ToString(CultureInfo.InvariantCulture), ["dash"] = options.UseDash || options.UseShortDash, ["shortDash"] = options.UseShortDash };
    private JsonObject Cannon(string type, string side) => new() { ["type"] = type, ["cannon"] = side, ["dash"] = options.UseDash || options.UseShortDash, ["shortDash"] = options.UseShortDash };
    private static JsonObject ThrowStaging(int player) => new() { ["type"] = "navigate",
        ["target"] = new JsonObject { ["x"] = player == 2 ? 13.6 : 27.2, ["z"] = -12.2 } };
    private JsonObject Portal(string destination) => new() { ["type"] = "portal", ["station"] = "portal", ["destinationRegion"] = destination, ["dash"] = options.UseDash || options.UseShortDash, ["shortDash"] = options.UseShortDash };
    private JsonObject SauceSwitch(NativeIngredient sauce) => new() { ["type"] = "switch-condiment", ["index"] = sauce == CarnivalRecipes.Mustard ? 0 : 1, ["dash"] = options.UseDash || options.UseShortDash, ["shortDash"] = options.UseShortDash };
    private void Log(string name, JsonObject value) => trace?.Event(name, value);
    private JsonObject Status() => new() { ["gameplayFrame"] = Frame, ["timer"] = state["timer"]?.DeepClone(),
        ["score"] = state["score"]?.DeepClone(), ["delivered"] = delivered, ["targetScore"] = options.TargetScore,
        ["activeJobs"] = new JsonArray(workers.Select(w => (JsonNode?)JsonValue.Create(w?.Name)).ToArray()),
        ["reservedMeals"] = new JsonArray(mealPlates.OrderBy(p => p.Key).Select(p => (JsonNode?)new JsonObject { ["index"] = p.Key, ["plate"] = p.Value }).ToArray()),
        ["cleanPlates"] = FreeCleanCount(), ["dirtyPlates"] = DirtyCount(), ["cooperativeSauces"] = SauceStatus(),
        ["bunBufferEnabled"] = options.BufferChoppedBuns, ["bufferedBun"] = bufferedBun, ["bunBufferCounter"] = bunBufferCounter,
        ["earlyOnionEnabled"] = options.EarlyOnionHeat, ["earlyOnion"] = EarlyOnionStatus(), ["pantryChoppingEnabled"] = options.PantryChopping,
        ["sharedPantryChoppingEnabled"] = options.SharedPantryChopping, ["sharedPantryChops"] = SharedPantryStatus(),
        ["fryerRescues"] = FryerStatus(), ["potRescues"] = PotStatus(),
        ["washerPlating"] = WasherPlatingStatus(),
        ["heatSafetyBlockedPlayers"] = System.Text.Json.JsonSerializer.SerializeToNode(heatSafetyBlocked.Order()),
        ["bakeryLookahead"] = options.BakeryLookahead, ["bakeryLeases"] = BakeryStatus(),
        ["parallelOnionEnabled"] = options.ParallelOnionHeat, ["earlyOnions"] = new JsonArray(EarlyOnionLeases().Select(l => (JsonNode?)EarlyOnionStatus(l)).ToArray()),
        ["stagedResourceReleaseEnabled"] = options.StagedResourceRelease, ["preServiceStock"] = PreServiceStockStatus(),
        ["cannonBoundaryPreemptionEnabled"] = options.CannonBoundaryPreemption, ["pausedCannonJobs"] = CannonInterruptionStatus(),
        ["releaseFiringChefOnLaunchEnabled"] = options.ReleaseFiringChefOnLaunch, ["cannonArrivalObligations"] = CannonFlightStatus(),
        ["serveBeforeSafeHeatEnabled"] = options.ServeBeforeSafeHeat, ["safeServiceHeat"] = safeServiceHeat is { } serviceHeat ? SafeServiceHeatStatus(serviceHeat) : null,
        ["sausageBufferSize"] = options.SausageBufferSize, ["sausageBuffers"] = SausageBufferStatus(),
        ["directCleanPassAssemblyEnabled"] = options.DirectCleanPassAssembly, ["nearReadyFryerHarvestEnabled"] = options.NearReadyFryerHarvest,
        ["nearestCentralTaskPreferenceEnabled"] = options.NearestCentralTaskPreference, ["continuousWaypointsEnabled"] = options.ContinuousWaypoints,
        ["stationaryTargetTransfersEnabled"] = options.StationaryTargetTransfers,
        ["waitForFinalSauceHeadEnabled"] = options.WaitForFinalSauceHead,
        ["predictiveBakeryReturnEnabled"] = options.PredictiveBakeryReturn, ["predictiveBakeryVisit"] = PredictiveBakeryStatus(),
        ["platedOnionFinishEnabled"] = options.PlatedOnionFinish, ["fifoEmptyBowlsEnabled"] = options.FifoEmptyBowls,
        ["platedHotdogBaseEnabled"] = options.PlatedHotdogBase };
    private async Task<JsonObject> FinishAsync(string reason)
    {
        CancelPredictiveBakeryVisit("planner-finished: " + reason);
        response = await call(Inputs.Step(1));
        Refresh();
        if (reason == "native-round-ended" && sauceLease is { } lease)
        {
            var details = SauceStatus(); details["reason"] = reason;
            Log("cooperativeSauceInterrupted", details);
            workers[lease.Owner] = workers[lease.Helper] = null;
            ReleaseSauceLease(lease);
        }
        var result = response.DeepClone().AsObject();
        result["planner"] = Status(); result["planner"]!["reason"] = reason;
        result["planner"]!["targetReached"] = I(state["score"]) >= options.TargetScore;
        Log("plannerFinished", result["planner"]!.AsObject());
        return result;
    }

    /// <summary>Recorded v4 delivered-plate lifecycle regression; no game calls or native state writes.</summary>
    public static int RetiredPlateSelfTest(JsonObject deliverySnapshot)
    {
        int count = 0;
        void Check(bool value, string detail) { if (!value) throw new InvalidOperationException("Delivered plate regression: " + detail); count++; }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Plate fixture attempted I/O."), null)
                { response = deliverySnapshot.DeepClone().AsObject(), options = new(),
                    recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(47642), 32).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh();
            // Isolate the plate lifecycle assertion from this fixture's next
            // live order: the synthetic future repeats the just-served recipe.
            p.state["orders"] = new JsonArray();
            p.mealPlates[p.delivered - 1] = 181;
            return p;
        }
        void EnablePhysicalColliders(JsonObject plate)
        {
            foreach (var collider in plate["colliders"]!.AsArray().OfType<JsonObject>())
            { collider["enabled"] = true; collider["active"] = true; }
        }
        var fixture = Make();
        Check(fixture.delivered == 5 && CarnivalRecipes.MatchRecipe(fixture.Entity(181), 47642).ReadyToDeliver,
            "actual delivered frame still exposes the fifth meal's complete plate composition");
        Check(fixture.PlateRegistrationSequence(181) >= 0, "actual fixture contains native registration identity");
        fixture.ObserveMeals();
        Check(fixture.retiredPlates.ContainsKey(181) && !fixture.mealPlates.Values.Contains(181),
            "native delivery retires the visible plate instead of assigning it to a repeated future recipe");
        fixture.ObserveMeals();
        Check(!fixture.mealPlates.Values.Contains(181), "released controller jobs cannot readopt a delivery-animation residual");
        fixture.entities.Remove(181); fixture.ObserveMeals();
        Check(!fixture.retiredPlates.ContainsKey(181), "native disappearance releases only retired identity bookkeeping");

        var reuse = Make(); reuse.ObserveMeals();
        var audit = reuse.state["entityRegistration"]!.AsObject();
        var eventArray = audit["events"]!.AsArray();
        long newSequence = reuse.retiredPlates[181].RegistrationSequence + 10000;
        eventArray.Add(new JsonObject { ["sequence"] = newSequence, ["kind"] = "register", ["entity"] = new JsonObject { ["entityId"] = 181 } });
        EnablePhysicalColliders(reuse.Entity(181)!);
        reuse.ObserveMeals();
        Check(!reuse.retiredPlates.ContainsKey(181) && reuse.mealPlates.Values.Contains(181),
            "actual new registration permits native-ID reuse even if fallback observation identity is unchanged");
        var fallback = Make(); fallback.state["entityRegistration"] = null; fallback.ObserveMeals();
        fallback.Entity(181)!["observedOrdinal"] = I(fallback.Entity(181)!["observedOrdinal"]) + 10000;
        EnablePhysicalColliders(fallback.Entity(181)!);
        fallback.ObserveMeals();
        Check(!fallback.retiredPlates.ContainsKey(181) && fallback.mealPlates.Values.Contains(181),
            "older traces or expired registry windows use changed observation identity to permit ID reuse");
        var resetOrdinal = Make(); resetOrdinal.ObserveMeals();
        resetOrdinal.Entity(181)!["observedOrdinal"] = I(resetOrdinal.Entity(181)!["observedOrdinal"]) + 10000;
        resetOrdinal.ObserveMeals();
        Check(resetOrdinal.retiredPlates.ContainsKey(181), "unchanged actual registration wins over a changed fallback observation counter");
        var unknown = Make(); unknown.state["entityRegistration"] = null; unknown.Entity(181)!.Remove("observedOrdinal");
        unknown.ObserveMeals(); unknown.ObserveMeals();
        Check(unknown.retiredPlates.ContainsKey(181), "missing incarnation evidence cannot fabricate ID reuse");

        var ordinary = Make(); ordinary.ObserveMeals();
        var unserved = ordinary.Entity(181)!.DeepClone().AsObject(); unserved["id"] = 900; unserved["observedOrdinal"] = 20000;
        EnablePhysicalColliders(unserved);
        ordinary.entities.Add(900, unserved); ordinary.ObserveMeals();
        Check(ordinary.mealPlates.Values.Contains(900) && !ordinary.mealPlates.Values.Contains(181),
            "a different unserved plate with the same recipe remains eligible for ordinary adoption");
        ordinary.entities.Remove(900);
        bool rejected = false;
        try { ordinary.ObserveMeals(); } catch (InvalidOperationException error) { rejected = error.Message.StartsWith("An undelivered reserved meal disappeared:", StringComparison.Ordinal); }
        Check(rejected, "genuine disappearance of an undelivered reserved plate still fails the attempt");
        var fresh = Make(); fresh.mealPlates.Clear(); fresh.ObserveMeals();
        Check(!fresh.mealPlates.Values.Contains(181), "a fresh planner cannot adopt the actual unheld, detached residual with disabled physical colliders");
        var held = Make(); held.mealPlates.Clear(); held.chefs[2]["heldEntityId"] = 181; held.ObserveMeals();
        Check(held.mealPlates.Values.Contains(181), "disabled collider evidence alone cannot exclude an observed carried plate");
        var attached = Make(); attached.mealPlates.Clear(); attached.Entity(32)!["attachedEntityId"] = 181; attached.ObserveMeals();
        Check(attached.mealPlates.Values.Contains(181), "an observed counter attachment preserves prepared meal adoption");
        var noColliders = Make(); noColliders.mealPlates.Clear(); noColliders.Entity(181)!.Remove("colliders"); noColliders.ObserveMeals();
        Check(noColliders.mealPlates.Values.Contains(181), "absent collider telemetry is not proof of a consumed delivery object");
        return count;
    }

    /// <summary>Recorded eight-v3 crossing regression; simulated movement is not native replay evidence.</summary>
    public static int TrafficSelfTest(JsonObject failedSnapshot)
    {
        int count = 0;
        void Check(bool value, string detail) { if (!value) throw new InvalidOperationException("Traffic regression: " + detail); count++; }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Traffic fixture attempted I/O."), null)
                { response = failedSnapshot.DeepClone().AsObject(), options = new() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Traffic fixture attempted I/O."), null);
            foreach (var pair in new[] { (Player: 0, Station: 2, Type: "place"), (Player: 3, Station: 56, Type: "chop") })
            {
                var action = p.runner.CreateAction(new JsonObject { ["type"] = pair.Type, ["player"] = pair.Player, ["station"] = pair.Station.ToString() });
                action.Action.Stage = "navigate"; action.Action.Frames = 60;
                action.Action.InitialHeld = p.Held(pair.Player);
                action.Action.Motion = new RouteRunner.RouteMotion { Model = p.model, Station = p.Station(pair.Station), NoPathSince = 0 };
                p.workers[pair.Player] = new Work("recorded-blocked-station", [], [pair.Station], null) { Active = action };
                p.reserved.Add(pair.Station);
            }
            return p;
        }
        var fixture = Make();
        Check(!Navigation.ToStation(fixture.model, fixture.Position(0), fixture.Station(2)!, fixture.TrafficObstacles(0).Select(o => o with { CircleRadius = null }).ToArray()).Success &&
            !Navigation.ToStation(fixture.model, fixture.Position(3), fixture.Station(56)!, fixture.TrafficObstacles(3).Select(o => o with { CircleRadius = null }).ToArray()).Success,
            "the recorded prior square proxies rejected both station approaches before capsule geometry was represented");
        Check(Navigation.ToStation(fixture.model, fixture.Position(0), fixture.Station(2)!).Success &&
            Navigation.ToStation(fixture.model, fixture.Position(3), fixture.Station(56)!).Success,
            "recorded static kitchen admits both routes");
        var owner = fixture.workers[0]!.Active; var helper = fixture.workers[3]!.Active;
        Check(fixture.TryBeginTrafficYield(), "recorded reciprocal blockage admits a measured yield");
        var yield = fixture.trafficYield!;
        Check(yield.Winner == 0 && yield.Helper == 3 && fixture.reserved.SetEquals([2, 56]) &&
            ReferenceEquals(helper, fixture.workers[3]!.Active) && ReferenceEquals(owner, fixture.workers[0]!.Active),
            "deterministic yield preserves both existing jobs and resource owners");
        Check(Navigation.ToStation(fixture.model, fixture.Position(0), fixture.Station(2)!, fixture.TrafficObstacles(0, 3, yield.Target)).Success,
            "helper destination leaves a collision-checked winner route");
        var cached = new Point2(); int initialFrame = fixture.Frame; bool interactionsNeutral = true;
        for (int frame = 0; frame < 185 && !yield.Move.IsDone; frame++)
        {
            fixture.state["framesSinceNoPhysics"] = (frame + 5) % 6;
            var input = fixture.runner.Tick(yield.Move, fixture.response);
            interactionsNeutral &= new[] { "pickup", "use", "dash", "throw" }.All(k => !B(input[k]));
            var position = fixture.Position(3);
            if (frame % 6 != 0) position = new(position.X + cached.X * .02, position.Z + cached.Z * .02);
            var stick = new Point2(N(input["x"]), -N(input["y"])); double magnitude = stick.Distance(default);
            cached = magnitude < 1e-8 ? default : new(stick.X / magnitude * 6, stick.Z / magnitude * 6);
            fixture.chefs[3]["position"]!["x"] = position.X; fixture.chefs[3]["position"]!["z"] = position.Z;
            fixture.chefs[3]["lastVelocity"]!["x"] = cached.X; fixture.chefs[3]["lastVelocity"]!["z"] = cached.Z;
            fixture.state["gameplayFrame"] = initialFrame + frame + 1;
        }
        Check(yield.Move.IsDone && yield.Move.Error is null && interactionsNeutral,
            "queued 60/50 Hz movement reaches the yield using only ordinary walking inputs");
        fixture.AdvanceTrafficYield();
        Check(fixture.trafficYield is { Arrived: true } && !owner!.IsDone && helper!.ElapsedFrames == 60,
            "arrival does not resume the original helper action before the winner completes");
        owner!.IsDone = true;
        fixture.AdvanceTrafficYield();
        Check(fixture.trafficYield is null && helper!.Action.Motion is null && fixture.reserved.SetEquals([2, 56]),
            "winner completion discards only the stale path and keeps exact jobs and leases");
        var transient = Make(); transient.workers[3]!.Active!.Action.Motion!.NoPathSince = -1;
        Check(!transient.TryBeginTrafficYield(), "one transient blocked route does not interrupt another chef");
        var suppressed = Make(); suppressed.chefs[3]["inputSuppressed"] = true;
        Check(!suppressed.TryBeginTrafficYield(), "suppressed native controls cannot be commanded to yield");
        var timed = Make(); Check(timed.TryBeginTrafficYield(), "timeout fixture has a valid measured yield");
        timed.state["gameplayFrame"] = timed.Frame + 241;
        bool rejected = false;
        try { timed.AdvanceTrafficYield(); } catch (TimeoutException) { rejected = true; }
        Check(rejected, "a crossing that does not clear fails the bounded attempt");
        return count;
    }

    /// <summary>Offline regression checks against a recorded native scene; never calls the game.</summary>
    public static int SelfTest(JsonObject initialSnapshot)
    {
        int count = 0;
        void Check(bool result, string detail) { if (!result) throw new InvalidOperationException("Planner regression: " + detail); count++; }
        CarnivalPlanner Make(Func<JsonObject, Task<JsonObject>>? fakeCall = null)
        {
            var p = new CarnivalPlanner(fakeCall ?? (_ => throw new InvalidOperationException("Offline planner test attempted I/O.")), null)
            {
                response = initialSnapshot.DeepClone().AsObject(), options = new(), recipes = CarnivalRecipes.All.ToArray()
            };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline action attempted I/O."), null);
            return p;
        }
        var stale = Make(); int left = stale.CannonId("left");
        stale.Entity(left)!["cannonLoadedEntityId"] = I(stale.chefs[2]["entityId"]);
        stale.Entity(left)!["cannonState"] = "Launched";
        stale.Entity(left)!["cannonFlying"] = false;
        stale.chefs[2]["controlsEnabled"] = true;
        Check(!stale.FireLoaded(0), "stale launched passenger id must not trigger another fire");
        stale.PantryAndService("upper-left");
        Check(stale.workers[2] is not null, "returned pantry chef must progress despite stale cannon passenger id");
        var boarded = Make(); left = boarded.CannonId("left");
        boarded.Entity(left)!["cannonLoadedEntityId"] = I(boarded.chefs[2]["entityId"]);
        boarded.Entity(left)!["cannonState"] = "Load"; boarded.Entity(left)!["cannonFlying"] = false;
        boarded.chefs[2]["controlsEnabled"] = false;
        boarded.PantryAndService("upper-left");
        Check(boarded.workers[2] is null, "boarded chef stays neutral and leaves the cannon available for the firing chef");
        Check(boarded.FireLoaded(0), "observed Load state and disabled matching passenger admit a firing job");
        Check(!boarded.Start(3, "conflict", [boarded.A("navigate", left)], [left]), "another chef cannot reserve an already reserved cannon");
        var ready = new FoodObservation(FoodNodeKind.Cooked, FoodPreparation.Raw, 0, "basket wrapper", 0, 0,
            [new(FoodNodeKind.Mixed, FoodPreparation.Mixed, 0, "dough", 0, 1, [])]);
        Check(MixedReady(ready), "mixed dough inside a raw cooking wrapper is ready for fryer transfer");
        Check(!MixedReady(ready with { Children = [ready.Children[0] with { Preparation = FoodPreparation.Overmixed }] }), "ruined mixed dough cannot be dispatched");
        var fallback = Make();
        int far = fallback.Stations("bowl").OrderBy(s => s.Position.X).First().EntityId;
        int rightPass = fallback.Counter(25.2, -13.2);
        fallback.Supply(1, CarnivalRecipes.Flour, far, true);
        Check(fallback.workers[1] is { } supplyWork && !supplyWork.Actions.Any(a => a["type"]?.ToString() == "throw") &&
            I(supplyWork.Actions.Last()["station"]) == rightPass, "far bowl uses the shared counter instead of an unverified throw lane");
        Check(fallback.counterSupplies[rightPass] == (far, CarnivalRecipes.Flour.Id), "far-bowl handoff retains exact vessel and ingredient identities");
        foreach (int id in fallback.workers[1]!.Resources) fallback.reserved.Remove(id);
        fallback.workers[1] = null;
        int flour = fallback.entities.Keys.Max() + 1;
        fallback.entities.Add(flour, new JsonObject { ["id"] = flour, ["name"] = "Flour", ["components"] = new JsonArray(),
            ["contents"] = new JsonArray(new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Flour.Id }) });
        fallback.Entity(rightPass)!["attachedEntityId"] = flour;
        Check(fallback.RelayRawIntoVessel(3) && I(fallback.workers[3]!.Actions.Last()["station"]) == far,
            "relay follows the far-bowl address even when the nearer bowl is empty and compatible");

        JsonObject NativeFood(FoodObservation food) => new()
        {
            ["type"] = food.Kind switch { FoodNodeKind.Ingredient => "IngredientAssembledNode", FoodNodeKind.Cooked => "CookedCompositeAssembledNode",
                FoodNodeKind.Mixed => "MixedCompositeAssembledNode", _ => "CompositeAssembledNode" },
            ["id"] = food.IngredientId, ["state"] = food.Preparation.ToString(), ["cookingStepId"] = food.CookingStepId,
            ["children"] = new JsonArray(food.Children.Select(c => (JsonNode?)NativeFood(c)).ToArray())
        };
        int AddFood(CarnivalPlanner p, int parent, FoodObservation food)
        {
            int id = p.entities.Keys.Max() + 1;
            p.entities.Add(id, new JsonObject { ["id"] = id, ["name"] = "offline-native-shape-fixture", ["active"] = true,
                ["components"] = new JsonArray("CarryableItem"), ["composition"] = NativeFood(food),
                ["position"] = p.Entity(parent)!["position"]!.DeepClone() });
            p.Entity(parent)!["attachedEntityId"] = id;
            return id;
        }
        var plateGuard = Make();
        var clean = plateGuard.AvailablePlates();
        Check(clean.Length == CarnivalRecipes.InitialPlates, "native initial fixture contains all four available plate tokens");
        foreach (var plate in clean.Take(3)) plateGuard.reserved.Add(Id(plate));
        Check(plateGuard.CanAllocatePlate(0) && !plateGuard.CanAllocatePlate(1), "the last clean plate is reserved for the FIFO head");
        plateGuard.assembling.Add(0);
        Check(!plateGuard.CanAllocatePlate(1), "an unplated hotdog job does not count as a plate allocated to the head");
        plateGuard.plating.Add(0);
        Check(plateGuard.CanAllocatePlate(1), "a separately reserved head plating job allows remaining plate use");
        var outputGuard = Make();
        outputGuard.Entity(outputGuard.Counter(25.2, -20.4))!["attachedEntityId"] = 99998;
        outputGuard.Entity(outputGuard.Counter(25.2, -18))!["attachedEntityId"] = 99999;
        outputGuard.reserved.Add(outputGuard.Counter(15.6, -13.2));
        Check(outputGuard.MealOutput(0, 1) == 0 && outputGuard.MealOutput(0, 0) == outputGuard.Counter(25.2, -19.2),
            "later meals cannot occupy the FIFO head's last service counter");
        var workspaceGuard = Make(); int workspace = workspaceGuard.Counter(20.4, -16.8);
        foreach (var counter in workspaceGuard.Stations("counter").Where(s => s.Regions.SequenceEqual(new[] { "center" }) &&
            s.Position.X > 18.5 && s.Position.X < 22.1 && s.EntityId != workspace)) workspaceGuard.Entity(counter.EntityId)!["attachedEntityId"] = 99999;
        workspaceGuard.Entity(workspace)!["attachedEntityId"] = 0;
        Check(workspaceGuard.EmptyCenterCounter(new(20.4, -16.8)) == 0 && workspaceGuard.EmptyCenterCounter(new(20.4, -16.8), 0, true) == workspace,
            "ordinary storage preserves a workspace that the FIFO head may borrow");
        var plainRecipe = CarnivalRecipes.GetRecipe(296560);
        var stock = Make(); stock.recipes = [plainRecipe, plainRecipe];
        int buffered = AddFood(stock, stock.Counter(19.2, -10.8), plainRecipe.ExpectedFood);
        stock.mealFoods[1] = buffered;
        Check(stock.ComponentDemand(CarnivalRecipes.Bun.Id) == 1 && stock.LooseIngredientCount(CarnivalRecipes.Bun.Id) == 0,
            "a future unplated hotdog satisfies its own bun demand without pretending to be a loose bun for the head");
        AddFood(stock, stock.Board("upper-left", false), plainRecipe.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id));
        Check(stock.LooseIngredientCount(CarnivalRecipes.Bun.Id) == 1, "a separate chopped bun remains available stock");
        var harvest = Make(); harvest.recipes = [CarnivalRecipes.GetRecipe(472326)];
        int bunBoard = harvest.Board("upper-left", false), pot = harvest.Stations("pot").First().EntityId;
        int bun = AddFood(harvest, bunBoard, plainRecipe.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id));
        harvest.Entity(pot)!["composition"] = NativeFood(plainRecipe.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked));
        Check(harvest.BuildUnplatedHotdog(0) && harvest.workers[0]!.Actions.Count(a => a["type"]?.ToString() == "combine") == 1,
            "a ready sausage is harvested into its bun even while the onion pan is empty");
        var unplatedWork = harvest.workers[0]!;
        int bufferCounter = I(unplatedWork.Actions.Last()["station"]);
        harvest.Entity(bunBoard)!["attachedEntityId"] = 0; harvest.Entity(bufferCounter)!["attachedEntityId"] = bun;
        harvest.Entity(bun)!["composition"] = NativeFood(plainRecipe.ExpectedFood);
        foreach (int id in unplatedWork.Resources) harvest.reserved.Remove(id);
        harvest.workers[0] = null; unplatedWork.Complete!();
        Check(harvest.mealFoods.GetValueOrDefault(0) == bun && !harvest.TryAssemble(3), "a plain intermediate for an onion order waits unplated");
        int pan = harvest.Stations("pan").First().EntityId;
        harvest.Entity(pan)!["composition"] = NativeFood(CarnivalRecipes.GetRecipe(472326).ExpectedFood.Children.Single(n => n.CookingStepId == CarnivalRecipes.PanCookingStepId));
        Check(harvest.AddUnplatedOnions(3) && I(harvest.workers[3]!.Actions.ElementAt(1)["station"]) == pan,
            "the pending onion is later combined into the exact reserved unplated hotdog");
        var assembly = Make(); assembly.recipes = [plainRecipe];
        int surface = assembly.Counter(19.2, -10.8), meal = AddFood(assembly, surface, plainRecipe.ExpectedFood);
        assembly.mealFoods[0] = meal;
        Check(assembly.TryAssemble(3) && I(assembly.workers[3]!.Actions.Single(a => a["type"]?.ToString() == "assemble")["station"]) == surface &&
            assembly.workers[3]!.Resources.Contains(surface), "plate-under assembly targets and reserves the stable parent counter after native food consumption");
        var equalDonuts = Make(); var chocolate = CarnivalRecipes.GetRecipe(228996);
        equalDonuts.recipes = [chocolate, chocolate, chocolate];
        int occupiedBasket = equalDonuts.Stations("basket").First().EntityId;
        equalDonuts.Entity(occupiedBasket)!["composition"] = NativeFood(chocolate.ExpectedFood);
        equalDonuts.basketAssignments[occupiedBasket] = 0;
        equalDonuts.AssignBowls();
        Check(equalDonuts.bowlAssignments.Values.Order().SequenceEqual(new[] { 1, 2 }),
            "equal recipes in consecutive orders receive distinct bowl jobs despite an earlier identical basket");
        var heatAdmission = Make();
        var onionRecipe = CarnivalRecipes.GetRecipe(472326);
        heatAdmission.recipes = [plainRecipe, plainRecipe, plainRecipe, CarnivalRecipes.GetRecipe(228996), onionRecipe];
        var onionLeaf = onionRecipe.ExpectedFood.Children.Single(n => n.CookingStepId == CarnivalRecipes.PanCookingStepId).Children.Single();
        int onionBoard = heatAdmission.Board("upper-left", true);
        AddFood(heatAdmission, onionBoard, onionLeaf);
        Check(!heatAdmission.MoveChoppedOnion(0), "a later onion order cannot start frying before its unplated base exists");
        int onionBase = AddFood(heatAdmission, heatAdmission.Counter(19.2, -10.8), plainRecipe.ExpectedFood);
        heatAdmission.mealFoods[4] = onionBase;
        Check(heatAdmission.MoveChoppedOnion(0), "an observed matching unplated base admits one onion pan");
        foreach (int resource in heatAdmission.workers[0]!.Resources) heatAdmission.reserved.Remove(resource);
        heatAdmission.workers[0] = null;
        int cookingPan = heatAdmission.Stations("pan").First().EntityId;
        heatAdmission.Entity(cookingPan)!["composition"] = NativeFood(onionRecipe.ExpectedFood.Children.Single(n => n.CookingStepId == CarnivalRecipes.PanCookingStepId)
            with { Preparation = FoodPreparation.Cooking });
        Check(!heatAdmission.MoveChoppedOnion(3), "one unplated base cannot admit a second simultaneously cooking onion");
        CarnivalPlanner Cooperative(Func<JsonObject, Task<JsonObject>>? fakeCall = null)
        {
            var p = Make(fakeCall); p.options = new(CooperativeSauces: true); p.recipes = [CarnivalRecipes.GetRecipe(125780)];
            int counter = p.Counter(19.2, -10.8);
            p.mealFoods[0] = AddFood(p, counter, plainRecipe.ExpectedFood);
            p.Entity(p.Single("condiment"))!["switchIndex"] = 0;
            return p;
        }
        void FinishChild(CarnivalPlanner p, int player)
        {
            var work = p.workers[player] ?? throw new InvalidOperationException("Missing synthetic cooperative child.");
            Check(work.Resources.Length == 0, "cooperative child must not separately own and release shared locks");
            p.workers[player] = null;
            work.Complete!();
        }
        void Reject(Action action, string detail)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, detail);
        }
        var serial = Cooperative(); serial.options = new();
        Check(serial.TryAssemble(0) && serial.sauceLease is null && serial.workers[3] is null,
            "cooperative sauces default off preserves serial work ownership");
        var busyHelper = Cooperative();
        busyHelper.Start(3, "existing-native-job", [busyHelper.A("navigate", busyHelper.Counter(21.6, -10.8))], []);
        Check(busyHelper.TryAssemble(0) && busyHelper.sauceLease is null && busyHelper.workers[0]!.Actions.Count(a => a["type"]?.ToString() == "switch-condiment") == 2,
            "an occupied helper causes the complete serial fallback without a partial shared lease");
        var coop = Cooperative();
        Check(coop.TryAssemble(0) && coop.sauceLease is not null && coop.IsSauceParticipant(0) && coop.IsSauceParticipant(3),
            "two idle empty center chefs can acquire a cooperative sauce session");
        var lease = coop.sauceLease!;
        Check(new[] { lease.Plate, lease.Food, lease.Source, lease.Output, lease.Dispenser, lease.Button }.All(coop.reserved.Contains) &&
            !coop.Start(2, "conflicting-switch-job", [coop.SauceSwitch(CarnivalRecipes.Ketchup)], [lease.Button]),
            "the shared lease excludes external use of food, plate, source, output, dispenser and switch");
        coop.Entity(lease.Plate)!["composition"] = NativeFood(plainRecipe.ExpectedFood);
        coop.chefs[lease.Owner]["heldEntityId"] = lease.Plate;
        FinishChild(coop, lease.Owner);
        coop.AdvanceCooperativeSauces();
        Check(lease.Phase == SaucePhase.Preparing && coop.workers[lease.Owner] is null,
            "the holder waits neutrally for helper completion before the first application");
        FinishChild(coop, lease.Helper);
        coop.Entity(lease.Dispenser)!["switchIndex"] = 1;
        Reject(coop.AdvanceCooperativeSauces, "helper completion alone cannot bypass the observed mustard index barrier");
        coop.Entity(lease.Dispenser)!["switchIndex"] = 0;
        coop.AdvanceCooperativeSauces();
        Check(lease.Phase == SaucePhase.ApplyFirst && I(coop.workers[lease.Owner]!.Actions.Single()["expectedIngredientId"]) == CarnivalRecipes.Mustard.Id,
            "verified base plate and native mustard index admit only the mustard application");
        FinishChild(coop, lease.Owner);
        Reject(coop.AdvanceCooperativeSauces, "a finished input action cannot admit the next switch without native mustard food evidence");
        coop.Entity(lease.Plate)!["composition"] = NativeFood(CarnivalRecipes.GetRecipe(158500).ExpectedFood);
        coop.AdvanceCooperativeSauces();
        Check(lease.Phase == SaucePhase.SwitchSecond && coop.workers[lease.Owner] is null &&
            I(coop.workers[lease.Helper]!.Actions.Single()["index"]) == 1 && coop.Held(lease.Owner) == lease.Plate,
            "after native mustard evidence the helper switches while the owner retains the exact plate");
        FinishChild(coop, lease.Helper);
        Reject(coop.AdvanceCooperativeSauces, "the second apply waits for the native ketchup index despite helper completion");
        coop.Entity(lease.Dispenser)!["switchIndex"] = 1;
        coop.AdvanceCooperativeSauces();
        Check(lease.Phase == SaucePhase.ApplySecond && I(coop.workers[lease.Owner]!.Actions.Single()["expectedIngredientId"]) == CarnivalRecipes.Ketchup.Id,
            "verified ketchup index admits only the second application");
        FinishChild(coop, lease.Owner);
        coop.Entity(lease.Plate)!["composition"] = NativeFood(lease.Recipe.ExpectedFood);
        coop.AdvanceCooperativeSauces();
        Check(lease.Phase == SaucePhase.StageMeal && coop.workers[lease.Owner]!.Actions.Single()["type"]?.ToString() == "place",
            "the complete native meal is staged only after both ingredient barriers pass");
        FinishChild(coop, lease.Owner);
        Reject(coop.AdvanceCooperativeSauces, "local action completion cannot release the lease while the plate is still held");
        coop.chefs[lease.Owner]["heldEntityId"] = 0;
        coop.Entity(lease.Output)!["attachedEntityId"] = lease.Plate;
        coop.AdvanceCooperativeSauces();
        Check(coop.sauceLease is null && lease.Resources.All(id => !coop.reserved.Contains(id)) &&
            coop.mealPlates[lease.Index] == lease.Plate && coop.workers[0] is null && coop.workers[3] is null,
            "verified native staging releases the entire lease and both helpers cleanly");
        var requests = new List<JsonObject>(); CarnivalPlanner? timeout = null;
        timeout = Cooperative(request => { requests.Add(request.DeepClone().AsObject()); return Task.FromResult(timeout!.response); });
        timeout.initialized = true;
        Check(timeout.TryAssemble(0), "timeout fixture starts an ordinary cooperative attempt");
        timeout.state["gameplayFrame"] = 601;
        foreach (var order in (timeout.state["orders"] as JsonArray)?.OfType<JsonObject>() ?? []) order["recipeId"] = 125780;
        bool timedOut = false;
        try { timeout.RunAsync(timeout.options).GetAwaiter().GetResult(); } catch (TimeoutException) { timedOut = true; }
        Check(timedOut && timeout.cooperativeAttemptFailed && timeout.sauceLease is null && timeout.workers[0] is null && timeout.workers[3] is null,
            "logical timeout aborts the entire attempt and releases cooperative ownership");
        Check(requests.Count == 2 && requests.Last()["command"]?.ToString() == "step" && I(requests.Last()["steps"]) == 1 &&
            (requests.Last()["inputs"] as JsonArray)!.OfType<JsonObject>().All(input => N(input["x"]) == 0 && N(input["y"]) == 0 && !B(input["pickup"]) && !B(input["use"]) && !B(input["dash"])),
            "cooperative timeout emits one all-chef neutral frame and no dependent action inputs");
        var shortDash = Make(); shortDash.options = new(UseShortDash: true);
        Check(new[] { shortDash.A("take", shortDash.Counter(19.2, -10.8)), shortDash.Cannon("board-cannon", "left"), shortDash.Portal("upper-left"),
            shortDash.SauceSwitch(CarnivalRecipes.Mustard) }.All(action => B(action["dash"]) && B(action["shortDash"])),
            "short-dash opt-in forwards both flags without weakening native action guards");
        var farPotHandoff = Make();
        int farPot = farPotHandoff.Stations("pot").OrderByDescending(s => s.Position.X).First().EntityId;
        int leftPass = farPotHandoff.Counter(15.6, -13.2);
        farPotHandoff.Supply(2, CarnivalRecipes.Frankfurter, farPot, true);
        Check(farPotHandoff.workers[2] is { } farPotWork && !farPotWork.Actions.Any(a => a["type"]?.ToString() == "throw") &&
            I(farPotWork.Actions.Last()["station"]) == leftPass && farPotHandoff.counterSupplies[leftPass] == (farPot, CarnivalRecipes.Frankfurter.Id),
            "the far pot uses the exact addressed upper-left handoff after the demonstrated onion-board deflection");
        foreach (int resource in farPotHandoff.workers[2]!.Resources) farPotHandoff.reserved.Remove(resource);
        farPotHandoff.workers[2] = null;
        AddFood(farPotHandoff, leftPass, plainRecipe.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked).Children.Single());
        Check(farPotHandoff.RelayRawIntoVessel(0) && I(farPotHandoff.workers[0]!.Actions.Last()["station"]) == farPot,
            "the far-pot relay cannot redirect the ingredient into an empty compatible nearer pot");
        var reservedPass = Make();
        reservedPass.Start(0, "native-plate-handoff", [reservedPass.A("take", leftPass)], [leftPass]);
        reservedPass.Supply(2, CarnivalRecipes.Frankfurter, farPot, true);
        Check(reservedPass.workers[2] is null && !reservedPass.counterSupplies.ContainsKey(leftPass),
            "a plate handoff reservation prevents raw pantry work from claiming the shared counter");

        var bunLeaf = plainRecipe.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id);
        CarnivalPlanner BunFixture(bool enabled = true)
        {
            var p = Make(); p.options = new(BufferChoppedBuns: enabled); p.recipes = [plainRecipe, plainRecipe];
            AddFood(p, p.Board("upper-left", false), bunLeaf);
            return p;
        }
        var noBuffer = BunFixture(false);
        Check(!noBuffer.BufferChoppedBun(0) && noBuffer.workers[0] is null && noBuffer.bufferedBun == 0,
            "bun staging is disabled by default and changes no reservations");
        var oneDemand = BunFixture(); oneDemand.recipes = [plainRecipe];
        Check(!oneDemand.BufferChoppedBun(0), "one remaining bun demand does not cause speculative staging");
        var fullStorage = BunFixture();
        foreach (var counter in fullStorage.Stations("counter")) fullStorage.reserved.Add(counter.EntityId);
        Check(!fullStorage.BufferChoppedBun(0), "buffer cannot displace occupied or reserved storage");
        var onlyWorkspace = BunFixture();
        foreach (var counter in onlyWorkspace.Stations("counter").Where(s => s.EntityId != workspace)) onlyWorkspace.reserved.Add(counter.EntityId);
        onlyWorkspace.Entity(workspace)!["attachedEntityId"] = 0;
        Check(!onlyWorkspace.BufferChoppedBun(0), "buffer cannot consume the FIFO head's protected workspace");
        var changedBun = BunFixture();
        changedBun.Entity(changedBun.Attached(changedBun.Board("upper-left", false)))!["composition"] = NativeFood(plainRecipe.ExpectedFood);
        Check(!changedBun.BufferChoppedBun(0), "an assembled hotdog cannot be relabeled a loose chopped-bun buffer");

        var buffer = BunFixture();
        int sourceBoard = buffer.Board("upper-left", false), exactBun = buffer.Attached(sourceBoard);
        Check(buffer.BufferChoppedBun(0), "idle cook can stage one native chopped bun while pots are cooking");
        var buffering = buffer.workers[0]!; int stagingCounter = buffer.bunBufferCounter;
        Check(buffering.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }) &&
            buffering.Resources.Contains(exactBun) && buffering.Resources.Contains(sourceBoard) && buffering.Resources.Contains(stagingCounter),
            "staging emits legal take/place and reserves exact source, bun and destination");
        Check(stagingCounter != workspace && buffer.Station(stagingCounter)!.Regions.SequenceEqual(new[] { "center" }) &&
            !new[] { leftPass, buffer.Counter(15.6, -19.2), buffer.Counter(15.6, -20.4) }.Contains(stagingCounter),
            "bun buffer preserves service, dirty and clean handoffs");
        Check(!buffer.BufferChoppedBun(3) && buffer.BufferedBunSource() == 0,
            "another chef cannot buffer or harvest the in-flight reserved item");
        bool badStagingRejected = false;
        try { buffering.Complete!(); } catch (InvalidOperationException) { badStagingRejected = true; }
        Check(badStagingRejected, "job completion alone cannot prove the native bun transfer");
        buffer.Entity(sourceBoard)!["attachedEntityId"] = 0;
        buffer.Entity(stagingCounter)!["attachedEntityId"] = exactBun;
        foreach (int resource in buffering.Resources) buffer.reserved.Remove(resource);
        buffer.workers[0] = null; buffering.Complete!();
        Check(buffer.BufferedBunSource() == stagingCounter && buffer.LooseIngredientCount(CarnivalRecipes.Bun.Id) == 1,
            "same chopped bun on its buffer counter becomes available stock");
        AddFood(buffer, sourceBoard, bunLeaf);
        Check(!buffer.BufferChoppedBun(3) && buffer.LooseIngredientCount(CarnivalRecipes.Bun.Id) == 2,
            "one offboard bun plus one on the board reaches the existing two-bun stock bound");
        var serviceFloor = buffer.model.Regions.Single(r => r.Id == "lower-right").FloorBounds;
        void AimForService(CarnivalPlanner p) => p.Entity(p.CannonId("left"))!["cannonTarget"] = new JsonObject
        { ["x"] = (serviceFloor.MinX + serviceFloor.MaxX) / 2, ["y"] = 0, ["z"] = (serviceFloor.MinZ + serviceFloor.MaxZ) / 2 };
        AimForService(buffer);
        foreach (var vessel in buffer.Stations("pot")) buffer.reserved.Add(vessel.EntityId);
        buffer.PantryAndService("upper-left");
        Check(buffer.workers[2] is null, "pantry will not spawn a third loose bun when two already exist");
        foreach (var vessel in buffer.Stations("pot")) buffer.reserved.Remove(vessel.EntityId);
        int readyPot = buffer.Stations("pot").First().EntityId;
        buffer.Entity(readyPot)!["composition"] = NativeFood(plainRecipe.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked));
        Check(buffer.BuildUnplatedHotdog(3), "ready sausage can be harvested using the offboard chopped bun");
        var bufferedHarvest = buffer.workers[3]!;
        Check(I(bufferedHarvest.Actions.First()["station"]) == stagingCounter && I(bufferedHarvest.Actions.Last()["station"]) == stagingCounter &&
            !bufferedHarvest.Resources.Contains(sourceBoard), "harvest reuses the buffer counter and leaves the refilled chopping board available");
        buffer.Entity(exactBun)!["composition"] = NativeFood(plainRecipe.ExpectedFood);
        foreach (int resource in bufferedHarvest.Resources) buffer.reserved.Remove(resource);
        buffer.workers[3] = null; bufferedHarvest.Complete!();
        Check(buffer.bufferedBun == 0 && buffer.bunBufferCounter == 0 && buffer.mealFoods.GetValueOrDefault(0) == exactBun,
            "validated unplated meal replaces its buffer lease without changing entity identity");

        var supplyFallback = Make(); supplyFallback.recipes = [plainRecipe, plainRecipe]; AimForService(supplyFallback);
        int near = supplyFallback.Stations("pot").OrderBy(s => s.Position.X).First().EntityId;
        supplyFallback.reserved.Add(near);
        supplyFallback.counterSupplies[leftPass] = (farPot, CarnivalRecipes.Frankfurter.Id);
        Check(!supplyFallback.Supply(2, CarnivalRecipes.Frankfurter, farPot, true) && supplyFallback.workers[2] is null,
            "an already addressed vessel reports no new supply job");
        supplyFallback.PantryAndService("upper-left");
        Check(supplyFallback.workers[2]?.Name == "supply-HotdogBun" && supplyFallback.counterSupplies[leftPass].Vessel == farPot,
            "an unstartable pot handoff no longer suppresses legal bun stocking or changes the handoff address");
        return count;
    }
}
