using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum FryerPhase { Rescuing, Verifying, Parked, Plating, Restoring }
    private sealed class FryerRescue(int basket, int index, int home, int counter, int ordinal, int homeOrdinal, int counterOrdinal, int owner, int frame)
    {
        public readonly int Basket = basket, Index = index, Home = home, Counter = counter, Ordinal = ordinal,
            HomeOrdinal = homeOrdinal, CounterOrdinal = counterOrdinal, Started = frame;
        public int Owner = owner, PhaseFrame = frame, ProofFrame, LastProofFrame, StableSamples;
        public double Progress, ProofTimer;
        public string Food = "";
        public FryerPhase Phase;
        public int[] Resources => [Basket, Home, Counter];
    }
    private readonly Dictionary<int, int> fryerHomes = [];
    private readonly Dictionary<int, FryerRescue> fryerRescues = [];
    private bool IsFryerParticipant(int player) => fryerRescues.Values.Any(l => l.Owner == player);
    private bool FryerCookAvailable(int player) => workers[player] is null && Held(player) == 0 && Region(player) == "center" &&
        !IsSauceParticipant(player) && !IsEarlyOnionParticipant(player) && !IsBakeryParticipant(player) && !IsFryerParticipant(player) && !IsPotParticipant(player) &&
        !cannonInterruptions.ContainsKey(player) && trafficYield?.Helper != player && trafficYield?.Winner != player &&
        B(chefs[player]["controlsEnabled"]) && !NativeCannonFlight(player);

    private void CaptureFryerHomes()
    {
        foreach (var basket in Stations("basket"))
        {
            int home = AttachmentParent(basket.EntityId);
            if (!NativeCookingStation(Entity(home))) throw new InvalidOperationException("Initial fryer basket has no observed native cooking-station home.");
            fryerHomes.Add(basket.EntityId, home);
            CaptureNearReadyFryerIdentity(basket.EntityId, home);
        }
    }

    private bool FryerAvailableForPlating(int basket, int index) => fryerRescues.TryGetValue(basket, out var lease)
        ? lease.Index == index && lease.Phase == FryerPhase.Parked && lease.Owner < 0
        : Free(basket);

    private JsonObject FryerAction(string type, int station) => VesselAction(type, station, 360);

    private bool DirectFryerPlateHasTime(int player, int basket)
    {
        double progress = N(Entity(basket)?["cookingProgress"]);
        if (progress >= 15) return false;
        int plate = AvailablePlate(player);
        var plateStation = Station(plate); var basketStation = Station(basket);
        if (plate == 0 || plateStation is null || basketStation is null) return false;
        var obstacles = TrafficObstacles(player);
        var pickup = Navigation.ToStation(model, Position(player), plateStation, obstacles);
        if (!pickup.Success) return false;
        var harvest = Navigation.ToStation(model, pickup.Points[^1], basketStation, obstacles);
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        // Include two seconds for facing, button edges, cached movement and
        // settling beyond collision-safe native walking. Every later native
        // frame still has the independent deadline observation below.
        return harvest.Success && speed > 0 && double.IsFinite(speed) && (pickup.Length + harvest.Length) / speed + 2 < 19 - progress;
    }

    private bool TryRescueFryer(int player, int selectedBasket = 0)
    {
        if (!FryerCookAvailable(player)) return false;
        foreach (var basket in Stations("basket").Where(s => selectedBasket == 0 || s.EntityId == selectedBasket).OrderByDescending(s => N(Entity(s.EntityId)?["cookingProgress"])))
        {
            int id = basket.EntityId;
            if (fryerRescues.ContainsKey(id) || !Free(id) || !basketAssignments.TryGetValue(id, out int index) ||
                index < delivered || index >= recipes.Length || !IsDonut(recipes[index]) || !fryerHomes.TryGetValue(id, out int home) ||
                Attached(home) != id || N(Entity(id)?["cookingTime"]) != 10 || N(Entity(id)?["cookingProgress"]) < 9 || Food(id).IsRuined) continue;
            if (options.NearReadyFryerHarvest && NativeNearReadyFryer(id, index) &&
                TryAssemble(player, exactIndex: index, exactBasket: id, waitForNativeFryer: true)) return true;
            if (CarnivalRecipes.MatchRecipe(Entity(id), recipes[index].Id, false).ReadyToDeliver &&
                CanAllocatePlate(index) && DirectFryerPlateHasTime(player, id) && TryAssemble(player, exactIndex: index)) return true;
            int counter = EmptyCenterCounter(basket.Position);
            if (counter == 0 || !Free(home, counter) || !AssignedDoughReady(Food(id), recipes[index])) continue;
            var lease = new FryerRescue(id, index, home, counter, I(Entity(id)?["observedOrdinal"]), I(Entity(home)?["observedOrdinal"]),
                I(Entity(counter)?["observedOrdinal"]), player, Frame);
            foreach (int resource in lease.Resources) reserved.Add(resource);
            fryerRescues.Add(id, lease);
            if (!Start(player, "rescue-cooked-fryer-" + (index + 1),
                [FryerAction("navigate", home), FryerAction("cook", id), FryerAction("take", home), FryerAction("place", counter)], [], () =>
                {
                    RequireFryerIdentity(lease);
                    if (Attached(counter) != id || Attached(home) != 0 || Held(player) != 0 ||
                        !CarnivalRecipes.MatchRecipe(Entity(id), recipes[index].Id, false).ReadyToDeliver)
                        throw new InvalidOperationException("Fryer rescue did not park the exact native Cooked recipe off its original heat.");
                    lease.Progress = N(Entity(id)?["cookingProgress"]); lease.Food = JsonSerializer.Serialize(Food(id));
                    lease.ProofFrame = lease.LastProofFrame = Frame; lease.ProofTimer = N(state["timer"]);
                    SetFryerPhase(lease, FryerPhase.Verifying);
                })) throw new InvalidOperationException("Fryer rescue lost its exclusively checked chef slot.");
            Log("fryerRescueAcquired", FryerStatus(lease)); return true;
        }
        return false;
    }

    private void RequireFryerIdentity(FryerRescue lease)
    {
        if (Entity(lease.Basket) is not { } basket || Entity(lease.Home) is not { } home || Entity(lease.Counter) is not { } counter ||
            I(basket["observedOrdinal"]) != lease.Ordinal || I(home["observedOrdinal"]) != lease.HomeOrdinal ||
            I(counter["observedOrdinal"]) != lease.CounterOrdinal || !NativeCookingStation(home) || NativeCookingStation(counter) ||
            !KitchenModel.Components(basket).Contains("CookableContainer") || N(basket["cookingTime"]) != 10 ||
            lease.Resources.Any(r => !reserved.Contains(r)) || basketAssignments.GetValueOrDefault(lease.Basket, -1) != lease.Index)
            throw new InvalidOperationException("Fryer rescue lost its exact basket, stove, counter, recipe assignment or exclusive reservation.");
    }
    private void RequireParkedFryer(FryerRescue lease)
    {
        if (Attached(lease.Counter) != lease.Basket || Attached(lease.Home) != 0 || HeldByAnyone(lease.Basket) ||
            N(Entity(lease.Basket)?["cookingProgress"]) != lease.Progress || JsonSerializer.Serialize(Food(lease.Basket)) != lease.Food ||
            !CarnivalRecipes.MatchRecipe(Entity(lease.Basket), recipes[lease.Index].Id, false).ReadyToDeliver)
            throw new InvalidOperationException("Parked fryer recipe or native cooking progress changed off heat.");
    }
    private void AdvanceFryerRescues()
    {
        // Ordinary plate jobs also own hot baskets. They must not bypass the
        // same deadline merely because no parking lease was necessary at their
        // start. This observes advancing native time; it never alters cooking.
        foreach (var assignment in basketAssignments)
            if (fryerHomes.TryGetValue(assignment.Key, out int home) && Attached(home) == assignment.Key &&
                !EmptyFood(assignment.Key) && N(Entity(assignment.Key)?["cookingTime"]) == 10 &&
                N(Entity(assignment.Key)?["cookingProgress"]) >= 19)
                throw new TimeoutException("Assigned fryer basket remained on native heat beyond the bounded harvest deadline.");
        foreach (var lease in fryerRescues.Values.ToArray())
        {
            RequireFryerIdentity(lease);
            if (lease.Owner >= 0 && (!B(chefs[lease.Owner]["controlsEnabled"]) || Region(lease.Owner) != "center" ||
                IsSauceParticipant(lease.Owner) || IsEarlyOnionParticipant(lease.Owner) || IsBakeryParticipant(lease.Owner)))
                throw new InvalidOperationException("Fryer rescue chef lost native controls or exclusive ownership.");
            if (Attached(lease.Home) == lease.Basket && !EmptyFood(lease.Basket) && N(Entity(lease.Basket)?["cookingProgress"]) >= 19)
                throw new TimeoutException("Fryer rescue did not remove the native basket before its observed burn deadline.");
            if (lease.Phase != FryerPhase.Parked && Frame - lease.PhaseFrame > 720)
                throw new TimeoutException("Fryer rescue phase exceeded its advancing native-frame budget.");
            if (lease.Phase == FryerPhase.Verifying)
            {
                RequireParkedFryer(lease);
                if (Frame > lease.LastProofFrame) { lease.LastProofFrame = Frame; lease.StableSamples++; }
                if (lease.StableSamples >= 2 && Frame - lease.ProofFrame >= 2 && N(state["timer"]) < lease.ProofTimer)
                {
                    Log("fryerOffheatProved", FryerStatus(lease)); lease.Owner = -1; SetFryerPhase(lease, FryerPhase.Parked);
                }
            }
            else if (lease.Phase == FryerPhase.Parked) RequireParkedFryer(lease);
        }
    }
    private void BeginFryerPlating(int basket, int player)
    {
        if (!fryerRescues.TryGetValue(basket, out var lease)) return;
        RequireFryerIdentity(lease); RequireParkedFryer(lease);
        if (lease.Phase != FryerPhase.Parked || lease.Owner >= 0) throw new InvalidOperationException("Parked fryer was claimed by more than one plate job.");
        lease.Owner = player; SetFryerPhase(lease, FryerPhase.Plating);
    }
    private void BeginFryerRestoration(int basket, int player)
    {
        if (!fryerRescues.TryGetValue(basket, out var lease)) return;
        RequireFryerIdentity(lease);
        if (lease.Phase != FryerPhase.Plating || lease.Owner != player || !EmptyFood(basket) || Attached(lease.Counter) != basket ||
            Attached(lease.Home) != 0 || Held(player) != 0)
            throw new InvalidOperationException("Fryer plating did not empty the original parked basket before restoration.");
        SetFryerPhase(lease, FryerPhase.Restoring);
        if (!Start(player, "restore-empty-fryer-" + basket,
            [FryerAction("take", lease.Counter), FryerAction("place", lease.Home)], [], () =>
            {
                RequireFryerIdentity(lease);
                if (!EmptyFood(basket) || Attached(lease.Home) != basket || Attached(lease.Counter) != 0 || Held(player) != 0)
                    throw new InvalidOperationException("Fryer restoration lost the same native empty basket or original stove.");
                Log("fryerRescueReleased", FryerStatus(lease));
                foreach (int resource in lease.Resources) reserved.Remove(resource);
                fryerRescues.Remove(basket);
            })) throw new InvalidOperationException("Fryer restoration could not retain its plating chef.");
    }
    private void SetFryerPhase(FryerRescue lease, FryerPhase phase)
    { lease.Phase = phase; lease.PhaseFrame = Frame; Log("fryerRescuePhase", FryerStatus(lease)); }
    private JsonObject FryerStatus(FryerRescue lease) => new() { ["basket"] = lease.Basket, ["orderIndex"] = lease.Index,
        ["home"] = lease.Home, ["counter"] = lease.Counter, ["basketOrdinal"] = lease.Ordinal, ["owner"] = lease.Owner,
        ["phase"] = lease.Phase.ToString(), ["frame"] = Frame, ["phaseFrame"] = lease.PhaseFrame,
        ["nativeCookingProgress"] = Entity(lease.Basket)?["cookingProgress"]?.DeepClone(), ["parkedProgress"] = lease.Progress,
        ["proofFrame"] = lease.ProofFrame, ["proofTimer"] = lease.ProofTimer, ["stableOffheatSamples"] = lease.StableSamples };
    private JsonArray FryerStatus() => new(fryerRescues.Values.OrderBy(l => l.Index).Select(l => (JsonNode?)FryerStatus(l)).ToArray());
}
