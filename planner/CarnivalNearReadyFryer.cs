using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class NearReadyFryer(int player, int index, int plate, int source, int basket, int home, int output, int frame, JsonObject evidence)
    {
        public readonly int Player = player, Index = index, Plate = plate, Source = source, Basket = basket, Home = home, Output = output, Started = frame;
        public readonly Dictionary<int, int> Identities = [];
        public readonly JsonObject Evidence = evidence;
        public Work Work = null!;
        public bool PickedUp, CookedObserved, Harvested;
        public int CookedFrame = -1, HarvestFrame = -1;
        public int[] Resources => new[] { Plate, Basket, Home, Output }.Distinct().ToArray();
    }
    private readonly Dictionary<int, (int Basket, int Home)> fryerNativeIdentities = [];
    private readonly Dictionary<Work, NearReadyFryer> nearReadyFryers = [];

    private void CaptureNearReadyFryerIdentity(int basket, int home) =>
        fryerNativeIdentities.Add(basket, (I(Entity(basket)?["observedOrdinal"]), I(Entity(home)?["observedOrdinal"])));

    private bool OriginalNearReadyFryer(int basket, int home) =>
        fryerHomes.GetValueOrDefault(basket) == home && fryerNativeIdentities.TryGetValue(basket, out var original) &&
        SameObservedEntity(basket, original.Basket) && SameObservedEntity(home, original.Home) &&
        B(Entity(basket)?["active"]) && B(Entity(home)?["active"]) && NativeCookingStation(Entity(home)) &&
        Attached(home) == basket && !HeldByAnyone(basket) && KitchenModel.Components(Entity(basket)!).Contains("CookableContainer") &&
        N(Entity(basket)?["cookingTime"]) == 10 && I(Entity(basket)?["cookingTypeId"]) == CarnivalRecipes.DeepFryerCookingStepId;

    private bool NativeNearReadyFryer(int basket, int index) =>
        index >= delivered && index < recipes.Length && IsDonut(recipes[index]) &&
        basketAssignments.GetValueOrDefault(basket, -1) == index && !fryerRescues.ContainsKey(basket) &&
        fryerHomes.TryGetValue(basket, out int home) && OriginalNearReadyFryer(basket, home) &&
        N(Entity(basket)?["cookingProgress"]) is >= 9 and < 10 && AssignedDoughReady(Food(basket), recipes[index]) &&
        !CarnivalRecipes.MatchRecipe(Entity(basket), recipes[index].Id, false).ReadyToDeliver;

    private NearReadyFryer? PlanNearReadyFryer(int player, int index, int plate, int basket, int output)
    {
        if (!options.NearReadyFryerHarvest || !FryerCookAvailable(player) || !NativeNearReadyFryer(basket, index) ||
            !CanAllocatePlate(index) || !AvailablePlates().Any(e => Id(e) == plate) ||
            !NearReadyPotStation(output) || !EmptyAttachment(output)) return null;
        int home = fryerHomes[basket], source = AttachmentParent(plate);
        if (source == 0 || !NearReadyPotStation(source) || !Free(source, plate, basket, home, output) ||
            !IsPlate(Entity(plate)) || !EmptyFood(plate) || HeldByAnyone(plate)) return null;
        int[] identities = new[] { plate, source, basket, home, output }.Distinct().ToArray();
        if (identities.Any(id => Entity(id)?["observedOrdinal"] is null || I(Entity(id)?["observedOrdinal"]) < 0 || !B(Entity(id)?["active"]))) return null;
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        double progress = N(Entity(basket)?["cookingProgress"]);
        if (!(speed > 0) || !double.IsFinite(speed)) return null;
        var obstacles = TrafficObstacles(player);
        var pickup = Navigation.ToStation(model, Position(player), Station(plate)!, obstacles);
        if (!pickup.Success) return null;
        var approach = Navigation.ToStation(model, pickup.Points[^1], Station(basket)!, obstacles);
        if (!approach.Success) return null;
        // Deliberately add the remaining native cook time instead of assuming
        // it overlaps walking; native cooking and all input delays still run.
        double beforeHarvest = (pickup.Length + approach.Length) / speed + Math.Max(0, 10 - progress) + 2;
        if (!double.IsFinite(beforeHarvest) || beforeHarvest >= 19 - progress) return null;
        var returning = Navigation.ToStation(model, approach.Points[^1], Station(output)!, obstacles);
        double total = beforeHarvest + returning.Length / speed + 1;
        if (!returning.Success || !double.IsFinite(total) || total >= 12) return null;
        var plan = new NearReadyFryer(player, index, plate, source, basket, home, output, Frame, new JsonObject {
            ["nativeCookingProgress"] = progress, ["remainingNativeCookSeconds"] = Math.Max(0, 10 - progress),
            ["estimatedSecondsToConsumption"] = beforeHarvest, ["guardRemainingSeconds"] = 19 - progress,
            ["estimatedTotalSeconds"] = total, ["chefToPlate"] = JsonSerializer.SerializeToNode(pickup),
            ["plateToBasket"] = JsonSerializer.SerializeToNode(approach), ["basketToOutput"] = JsonSerializer.SerializeToNode(returning) });
        foreach (int id in identities) plan.Identities.Add(id, I(Entity(id)?["observedOrdinal"]));
        return plan;
    }

    private void RegisterNearReadyFryer(NearReadyFryer plan, Work work)
    {
        if (!work.OwnedResources.SetEquals(plan.Resources) || work.Active is not null)
            throw new InvalidOperationException("Near-ready fryer lost its exact plate, basket, home or selected output leases at admission.");
        plan.Work = work; nearReadyFryers.Add(work, plan); Log("nativeNearReadyFryerAdmitted", NearReadyFryerStatus(plan));
    }

    private void ObserveNearReadyFryer(NearReadyFryer plan, bool requireWork)
    {
        if (plan.Identities.Any(p => !SameObservedEntity(p.Key, p.Value) || !B(Entity(p.Key)?["active"])) ||
            !OriginalNearReadyFryer(plan.Basket, plan.Home) || !NearReadyPotStation(plan.Source) || !NearReadyPotStation(plan.Output) ||
            plan.Index < delivered || basketAssignments.GetValueOrDefault(plan.Basket, -1) != plan.Index ||
            !assembling.Contains(plan.Index) || !plating.Contains(plan.Index) || !IsPlate(Entity(plan.Plate)) ||
            !B(chefs[plan.Player]["controlsEnabled"]) || Region(plan.Player) != "center" || NativeCannonFlight(plan.Player))
            throw new InvalidOperationException("Near-ready fryer changed its exact native basket, stove, plate, recipe, output or chef.");
        if (requireWork && (!ReferenceEquals(workers[plan.Player], plan.Work) || !plan.Work.OwnedResources.SetEquals(plan.Resources) ||
            plan.Resources.Any(id => !reserved.Contains(id)) || workers.OfType<Work>().Any(w => w != plan.Work && w.OwnedResources.Overlaps(plan.Resources))))
            throw new InvalidOperationException("Near-ready fryer lost its exclusive original Work ownership.");
        double progress = N(Entity(plan.Basket)?["cookingProgress"]);
        if (Entity(plan.Basket)?["cookingProgress"] is null || !double.IsFinite(progress) || progress < 0)
            throw new InvalidOperationException("Near-ready fryer lost its finite native cooking-progress observation.");
        if (Frame - plan.Started > 720 || !EmptyFood(plan.Basket) && progress >= 19)
            throw new TimeoutException("Near-ready fryer exceeded its native harvest/input deadline.");
        if (Food(plan.Basket).IsRuined || Food(plan.Plate).IsRuined)
            throw new InvalidOperationException("Near-ready fryer observed ruined native food.");
        if (Held(plan.Player) == plan.Plate && !plan.PickedUp)
        {
            if (!EmptyFood(plan.Plate) || Attached(plan.Source) == plan.Plate)
                throw new InvalidOperationException("Near-ready fryer did not start with the exact empty plate pickup.");
            plan.PickedUp = true;
        }
        if (CarnivalRecipes.MatchRecipe(Entity(plan.Basket), recipes[plan.Index].Id, false).ReadyToDeliver && !plan.CookedObserved)
        {
            plan.CookedObserved = true; plan.CookedFrame = Frame; Log("nativeNearReadyFryerCooked", NearReadyFryerStatus(plan));
        }
        if (EmptyFood(plan.Basket) && !plan.Harvested)
        {
            if (!plan.PickedUp || !plan.CookedObserved || Held(plan.Player) != plan.Plate ||
                !CarnivalRecipes.MatchRecipe(Entity(plan.Plate), recipes[plan.Index].Id).ReadyToDeliver)
                throw new InvalidOperationException("Near-ready fryer emptied without observed native Cooked food entering the exact held plate.");
            plan.Harvested = true; plan.HarvestFrame = Frame; Log("nativeNearReadyFryerConsumed", NearReadyFryerStatus(plan));
        }
        if (!plan.Harvested)
        {
            if (!AssignedDoughReady(Food(plan.Basket), recipes[plan.Index]) || !EmptyFood(plan.Plate) ||
                (plan.PickedUp ? Held(plan.Player) != plan.Plate : Attached(plan.Source) != plan.Plate || HeldByAnyone(plan.Plate)))
                throw new InvalidOperationException("Near-ready fryer lost its original dough or clean plate before native consumption.");
        }
        else if (!EmptyFood(plan.Basket) || !CarnivalRecipes.MatchRecipe(Entity(plan.Plate), recipes[plan.Index].Id).ReadyToDeliver ||
            !(Held(plan.Player) == plan.Plate || Held(plan.Player) == 0 && Attached(plan.Output) == plan.Plate))
            throw new InvalidOperationException("Near-ready fryer lost the exact finished plate or refilled its still-reserved basket.");
        if (Attached(plan.Output) != 0 && Attached(plan.Output) != plan.Plate)
            throw new InvalidOperationException("Near-ready fryer's reserved output became occupied by another item.");
        // Exact plate ownership and native source occupancy protect pickup;
        // another washer handoff may reuse the source once this plate is held.
    }
    private void ObserveNearReadyFryerHarvests()
    { foreach (var plan in nearReadyFryers.Values) ObserveNearReadyFryer(plan, true); }
    private void CompleteNearReadyFryer(NearReadyFryer plan)
    {
        ObserveNearReadyFryer(plan, false);
        if (!plan.Harvested || !plan.CookedObserved || Held(plan.Player) != 0 || Attached(plan.Output) != plan.Plate)
            throw new InvalidOperationException("Near-ready fryer lacks native cooking, same-plate consumption and selected output completion.");
        nearReadyFryers.Remove(plan.Work); Log("nativeNearReadyFryerComplete", NearReadyFryerStatus(plan));
    }
    private JsonObject NearReadyFryerStatus(NearReadyFryer plan) => new() {
        ["frame"] = Frame, ["player"] = plan.Player, ["recipeIndex"] = plan.Index, ["recipeId"] = recipes[plan.Index].Id,
        ["plate"] = plan.Plate, ["source"] = plan.Source, ["basket"] = plan.Basket, ["home"] = plan.Home, ["output"] = plan.Output,
        ["startedFrame"] = plan.Started, ["cookedFrame"] = plan.CookedFrame, ["harvestFrame"] = plan.HarvestFrame,
        ["identities"] = JsonSerializer.SerializeToNode(plan.Identities), ["admissionEvidence"] = plan.Evidence.DeepClone() };
}
