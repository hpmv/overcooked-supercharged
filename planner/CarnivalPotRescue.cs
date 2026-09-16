using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum PotPhase { Rescuing, Verifying, Parked, Consuming, Restoring }
    private sealed record PotHome(int Home, int PotOrdinal, int HomeOrdinal);
    private sealed class PotRescue(int pot, PotHome home, int counter, int counterOrdinal, int owner, int frame)
    {
        public readonly int Pot = pot, Home = home.Home, Ordinal = home.PotOrdinal, HomeOrdinal = home.HomeOrdinal,
            Counter = counter, CounterOrdinal = counterOrdinal, Started = frame;
        public int Owner = owner, Index = -1, PhaseFrame = frame, ProofFrame, LastProofFrame, StableSamples;
        public double Progress, ProofTimer;
        public string Food = "";
        public PotPhase Phase;
        public Work? Work;
        public int[] Resources => [Pot, Home, Counter];
    }
    private readonly Dictionary<int, PotHome> potHomes = [];
    private readonly Dictionary<int, PotRescue> potRescues = [];
    private bool IsPotParticipant(int player) => potRescues.Values.Any(l => l.Owner == player);
    private bool PotCookAvailable(int player) => player is 0 or 3 && workers[player] is null && Held(player) == 0 &&
        Region(player) == "center" && !IsPotParticipant(player) && !IsFryerParticipant(player) &&
        !IsEarlyOnionParticipant(player) && !IsBakeryParticipant(player) && !IsSauceParticipant(player) &&
        !cannonInterruptions.ContainsKey(player) && trafficYield?.Helper != player && trafficYield?.Winner != player &&
        B(chefs[player]["controlsEnabled"]) && !NativeCannonFlight(player);

    private void CapturePotHomes()
    {
        foreach (var station in Stations("pot"))
        {
            int pot = station.EntityId, home = AttachmentParent(pot);
            if (home == 0 || !NativeCookingStation(Entity(home)) || Entity(pot)?["observedOrdinal"] is null ||
                Entity(home)?["observedOrdinal"] is null || N(Entity(pot)?["cookingTime"]) != 12 ||
                I(Entity(pot)?["cookingTypeId"]) != CarnivalRecipes.PotCookingStepId)
                throw new InvalidOperationException("Pot rescue initialization requires both native boiling pots at their observed original stoves.");
            potHomes.Add(pot, new(home, I(Entity(pot)!["observedOrdinal"]), I(Entity(home)!["observedOrdinal"])));
        }
        if (potHomes.Count != 2) throw new InvalidOperationException("Pot rescue requires exactly two native boiling pots.");
    }

    private bool NativeSausagePot(int pot, bool cooked = false)
    {
        if (Entity(pot) is not { } entity || entity["composition"] is null || N(entity["cookingTime"]) != 12 ||
            I(entity["cookingTypeId"]) != CarnivalRecipes.PotCookingStepId || !KitchenModel.Components(entity).Contains("CookableContainer")) return false;
        var observation = CarnivalRecipes.ClassifyEntity(entity);
        return observation.EvidenceGaps.Length == 0 && observation.Food is { Kind: FoodNodeKind.Cooked } food && !food.IsRuined &&
            food.CookingStepId == CarnivalRecipes.PotCookingStepId && food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) &&
            (!cooked || food.Preparation == FoodPreparation.Cooked);
    }

    // Admission only reports candidates. The common heat scheduler chooses
    // among these and other native cooking/mixing deadlines.
    private IEnumerable<int> PendingPotRescues() => potHomes.Keys.Where(pot =>
        !potRescues.ContainsKey(pot) && Free(pot, potHomes[pot].Home) && Attached(potHomes[pot].Home) == pot &&
        !HeldByAnyone(pot) && NativeSausagePot(pot) && N(Entity(pot)?["cookingProgress"]) >= 11);

    private JsonObject PotAction(string type, int station) => VesselAction(type, station, 360);

    private bool DirectPotHarvestHasTime(int player, int pot)
    {
        if (!NativeSausagePot(pot, true) || !Free(pot)) return false;
        int board = BufferedBunSource();
        if (board == 0) board = Stations("chop").Select(s => s.EntityId).FirstOrDefault(id => Free(id) && Chopped(Attached(id), CarnivalRecipes.Bun.Id));
        var source = Station(board); var vessel = Station(pot);
        if (source is null || vessel is null || !Free(board, Attached(board))) return false;
        var obstacles = TrafficObstacles(player);
        var pickup = Navigation.ToStation(model, Position(player), source, obstacles);
        if (!pickup.Success) return false;
        var harvest = Navigation.ToStation(model, pickup.Points[^1], vessel, obstacles);
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        // The pot leaves heat risk when native sausage transfers into the bun.
        // Subsequent onion finishing/output travel does not cook the empty pot.
        return harvest.Success && speed > 0 && double.IsFinite(speed) &&
            (pickup.Length + harvest.Length) / speed + 2 < 23 - N(Entity(pot)?["cookingProgress"]);
    }

    private bool TryRescuePot(int player, int pot)
    {
        if (!PotCookAvailable(player) || !PendingPotRescues().Contains(pot)) return false;
        var home = potHomes[pot];
        if (!SameObservedEntity(pot, home.PotOrdinal) || !SameObservedEntity(home.Home, home.HomeOrdinal))
            throw new InvalidOperationException("Pot rescue cannot substitute a respawned pot or changed original stove.");
        if (options.NearReadyPotHarvest && NativeNearReadyPot(pot) && BuildUnplatedHotdog(player, pot, true)) return true;
        if (DirectPotHarvestHasTime(player, pot) && BuildUnplatedHotdog(player, pot))
        {
            Log("potDirectHarvestAdmitted", new JsonObject { ["pot"] = pot, ["home"] = home.Home, ["player"] = player,
                ["frame"] = Frame, ["nativeProgress"] = Entity(pot)?["cookingProgress"]?.DeepClone(), ["guardSeconds"] = 23,
                ["proof"] = "native cooked sausage, available chopped bun, full-clearance chef-to-bun-to-exact-pot walk plus two-second input allowance" });
            return true;
        }
        var station = Station(pot);
        if (station is null) return false;
        int counter = EmptyCenterCounter(station.Position);
        if (counter == 0 || !Free(counter) || Entity(counter)?["observedOrdinal"] is null || NativeCookingStation(Entity(counter))) return false;
        var lease = new PotRescue(pot, home, counter, I(Entity(counter)!["observedOrdinal"]), player, Frame);
        foreach (int resource in lease.Resources) reserved.Add(resource);
        potRescues.Add(pot, lease);
        if (!Start(player, "rescue-cooked-pot-" + pot,
            [PotAction("navigate", home.Home), PotAction("cook", pot), PotAction("take", home.Home), PotAction("place", counter)], [], () =>
            {
                RequirePotIdentity(lease);
                if (Attached(counter) != pot || Attached(home.Home) != 0 || Held(player) != 0 || !NativeSausagePot(pot, true))
                    throw new InvalidOperationException("Pot rescue did not park the exact native cooked sausage off its original heat.");
                lease.Progress = N(Entity(pot)?["cookingProgress"]); lease.Food = PotComposition(pot);
                lease.ProofFrame = lease.LastProofFrame = Frame; lease.ProofTimer = N(state["timer"]); lease.Work = null;
                SetPotPhase(lease, PotPhase.Verifying);
            })) throw new InvalidOperationException("Pot rescue lost its exclusively checked chef slot.");
        lease.Work = workers[player]; Log("potRescueAcquired", PotStatus(lease)); return true;
    }

    private string PotComposition(int pot) => Entity(pot)?["composition"]?.ToJsonString()
        ?? throw new InvalidOperationException("Native pot composition evidence disappeared.");

    private bool NativeEmptyPot(int pot) => Entity(pot)?["composition"] is not null && Entity(pot)?["cookingProgress"] is not null &&
        EmptyFood(pot) && !Food(pot).IsRuined && N(Entity(pot)?["cookingProgress"]) == 0;

    private void RequirePotIdentity(PotRescue lease)
    {
        if (!SameObservedEntity(lease.Pot, lease.Ordinal) || !SameObservedEntity(lease.Home, lease.HomeOrdinal) ||
            !SameObservedEntity(lease.Counter, lease.CounterOrdinal) || !NativeCookingStation(Entity(lease.Home)) ||
            NativeCookingStation(Entity(lease.Counter)) || N(Entity(lease.Pot)?["cookingTime"]) != 12 ||
            Entity(lease.Home)?["attachedEntityId"] is null || Entity(lease.Counter)?["attachedEntityId"] is null ||
            Entity(lease.Pot)?["composition"] is null || Entity(lease.Pot)?["cookingProgress"] is null ||
            !double.IsFinite(N(Entity(lease.Pot)?["cookingProgress"])) ||
            I(Entity(lease.Pot)?["cookingTypeId"]) != CarnivalRecipes.PotCookingStepId ||
            !KitchenModel.Components(Entity(lease.Pot)!).Contains("CookableContainer") ||
            lease.Resources.Any(r => !reserved.Contains(r)) || workers.OfType<Work>().Any(w => w.OwnedResources.Overlaps(lease.Resources)))
            throw new InvalidOperationException("Pot rescue lost an exact native vessel/stove/counter or its sole resource ownership.");
    }

    private void RequireParkedPot(PotRescue lease)
    {
        if (Attached(lease.Counter) != lease.Pot || Attached(lease.Home) != 0 || HeldByAnyone(lease.Pot) ||
            N(Entity(lease.Pot)?["cookingProgress"]) != lease.Progress || PotComposition(lease.Pot) != lease.Food || !NativeSausagePot(lease.Pot, true))
            throw new InvalidOperationException("Parked pot changed its native cooked food, progress or offheat attachment.");
    }

    private void AdvancePotRescues()
    {
        // This independent observer includes pots owned by ordinary hotdog
        // jobs; an unleased harvest must not bypass the same native deadline.
        foreach (var (pot, home) in potHomes)
            if (Attached(home.Home) == pot && !EmptyFood(pot) && N(Entity(pot)?["cookingTime"]) == 12 &&
                N(Entity(pot)?["cookingProgress"]) >= 23)
                throw new TimeoutException("Boiling pot remained on native heat beyond the bounded 23-second harvest deadline.");
        foreach (var lease in potRescues.Values.ToArray())
        {
            RequirePotIdentity(lease);
            if (lease.Owner >= 0 && (!B(chefs[lease.Owner]["controlsEnabled"]) || Region(lease.Owner) != "center" ||
                IsSauceParticipant(lease.Owner) || IsEarlyOnionParticipant(lease.Owner) || IsBakeryParticipant(lease.Owner) || IsFryerParticipant(lease.Owner)))
                throw new InvalidOperationException("Pot rescue chef lost native controls or exclusive phase ownership.");
            if (lease.Phase is PotPhase.Rescuing or PotPhase.Consuming or PotPhase.Restoring &&
                (lease.Work is null || lease.Owner < 0 || !ReferenceEquals(workers[lease.Owner], lease.Work)))
                throw new InvalidOperationException("Pot rescue lost its original active work or consuming job.");
            if (lease.Phase is PotPhase.Verifying or PotPhase.Parked && lease.Owner >= 0 && workers[lease.Owner] is not null)
                throw new InvalidOperationException("Pot verification chef was assigned an unrelated job.");
            if (lease.Phase != PotPhase.Parked && Frame - lease.PhaseFrame > 720)
                throw new TimeoutException("Pot rescue phase exceeded its advancing native-frame budget.");
            if (lease.Phase == PotPhase.Rescuing && !NativeSausagePot(lease.Pot))
                throw new InvalidOperationException("Rescued pot changed its original sausage before parking.");
            if (lease.Phase == PotPhase.Verifying)
            {
                RequireParkedPot(lease);
                if (Frame > lease.LastProofFrame) { lease.LastProofFrame = Frame; lease.StableSamples++; }
                if (lease.StableSamples >= 2 && Frame - lease.ProofFrame >= 2 && N(state["timer"]) < lease.ProofTimer)
                {
                    Log("potOffheatProved", PotStatus(lease)); lease.Owner = -1; SetPotPhase(lease, PotPhase.Parked);
                }
            }
            else if (lease.Phase == PotPhase.Parked) RequireParkedPot(lease);
            else if (lease.Phase == PotPhase.Consuming)
            {
                if (lease.Index < 0 || !assembling.Contains(lease.Index) || Attached(lease.Counter) != lease.Pot || Attached(lease.Home) != 0 ||
                    (EmptyFood(lease.Pot) ? !NativeEmptyPot(lease.Pot) : PotComposition(lease.Pot) != lease.Food || N(Entity(lease.Pot)?["cookingProgress"]) != lease.Progress))
                    throw new InvalidOperationException("Claimed pot lost its single recipe, native food or offheat source.");
            }
            else if (lease.Phase == PotPhase.Restoring && (!NativeEmptyPot(lease.Pot) ||
                (Attached(lease.Home) != 0 && Attached(lease.Home) != lease.Pot)))
                throw new InvalidOperationException("Empty pot was refilled or its original stove reused during restoration.");
        }
    }

    private bool PotAvailableForHotdog(int pot) => potRescues.TryGetValue(pot, out var lease)
        ? lease.Phase == PotPhase.Parked && lease.Owner < 0 && lease.Index < 0
        : Free(pot);

    private void BeginPotConsumption(int pot, int player, int index)
    {
        if (!potRescues.TryGetValue(pot, out var lease)) return;
        RequirePotIdentity(lease); RequireParkedPot(lease);
        if (lease.Phase != PotPhase.Parked || lease.Owner >= 0 || lease.Index >= 0 || workers[player] is not { } work ||
            !assembling.Contains(index) || index < delivered || index >= recipes.Length || IsDonut(recipes[index]))
            throw new InvalidOperationException("Parked pot was claimed by more than one eligible hotdog job.");
        lease.Owner = player; lease.Index = index; lease.Work = work; SetPotPhase(lease, PotPhase.Consuming);
    }

    private void BeginPotRestoration(int pot, int player, int index)
    {
        if (!potRescues.TryGetValue(pot, out var lease)) return;
        RequirePotIdentity(lease);
        if (lease.Phase != PotPhase.Consuming || lease.Owner != player || lease.Index != index || !NativeEmptyPot(pot) ||
            Attached(lease.Counter) != pot || Attached(lease.Home) != 0 || Held(player) != 0 ||
            !mealFoods.TryGetValue(index, out int food) || AttachmentParent(food) == 0 ||
            !CarnivalRecipes.MatchRecipe(Entity(food), Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id) ? 472326 : 296560, false).ReadyToDeliver)
            throw new InvalidOperationException("Hotdog consumption did not empty the original parked pot and stage its exact native meal.");
        SetPotPhase(lease, PotPhase.Restoring);
        if (!Start(player, "restore-empty-pot-" + pot, [PotAction("take", lease.Counter), PotAction("place", lease.Home)], [], () =>
            {
                RequirePotIdentity(lease);
                if (!NativeEmptyPot(pot) || Attached(lease.Home) != pot || Attached(lease.Counter) != 0 || Held(player) != 0)
                    throw new InvalidOperationException("Pot restoration lost the same native empty pot, original stove or empty counter.");
                Log("potRescueReleased", PotStatus(lease));
                foreach (int resource in lease.Resources) reserved.Remove(resource);
                potRescues.Remove(pot);
            })) throw new InvalidOperationException("Pot restoration could not retain its consuming chef.");
        lease.Work = workers[player];
    }

    private void SetPotPhase(PotRescue lease, PotPhase phase)
    { lease.Phase = phase; lease.PhaseFrame = Frame; Log("potRescuePhase", PotStatus(lease)); }
    private JsonObject PotStatus(PotRescue lease) => new() { ["pot"] = lease.Pot, ["home"] = lease.Home, ["counter"] = lease.Counter,
        ["potOrdinal"] = lease.Ordinal, ["homeOrdinal"] = lease.HomeOrdinal, ["counterOrdinal"] = lease.CounterOrdinal,
        ["orderIndex"] = lease.Index, ["owner"] = lease.Owner, ["phase"] = lease.Phase.ToString(), ["frame"] = Frame,
        ["phaseFrame"] = lease.PhaseFrame, ["nativeCookingProgress"] = Entity(lease.Pot)?["cookingProgress"]?.DeepClone(),
        ["parkedProgress"] = lease.Progress, ["proofFrame"] = lease.ProofFrame, ["stableOffheatSamples"] = lease.StableSamples,
        ["resources"] = new JsonArray(lease.Resources.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()) };
    private JsonArray PotStatus() => new(potRescues.Values.OrderBy(l => l.Pot).Select(l => (JsonNode?)PotStatus(l)).ToArray());
}
