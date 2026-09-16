using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class NearReadyPot(int player, int index, int source, int food, int pot, int home, int pan, int output, int frame, JsonObject evidence)
    {
        public readonly int Player = player, Index = index, Source = source, Food = food, Pot = pot, Home = home, Pan = pan, Output = output, Started = frame;
        public readonly Dictionary<int, int> Identities = [];
        public readonly JsonObject Evidence = evidence;
        public Work Work = null!;
        public bool PickedUp, CookedObserved, Harvested, SourceReleased, PotReleased, HomeReleased;
        public int CookedFrame = -1, HarvestFrame = -1;
        public int[] Resources => new[] { Source, Food, Pot, Home, Pan, Output }.Where(i => i != 0).Distinct().ToArray();
    }
    private readonly Dictionary<Work, NearReadyPot> nearReadyPots = [];

    private bool NativeNearReadyPot(int pot) => !potRescues.ContainsKey(pot) && potHomes.TryGetValue(pot, out var home) &&
        SameObservedEntity(pot, home.PotOrdinal) && SameObservedEntity(home.Home, home.HomeOrdinal) &&
        B(Entity(pot)?["active"]) && B(Entity(home.Home)?["active"]) && Attached(home.Home) == pot && !HeldByAnyone(pot) &&
        NativeCookingStation(Entity(home.Home)) && NativeSausagePot(pot) && !NativeSausagePot(pot, true) &&
        N(Entity(pot)?["cookingProgress"]) is >= 11 and < 12;

    private bool NearReadyPotStation(int id) => Station(id) is { Role: "counter" or "chop" } station &&
        station.Regions.Contains("center") && Entity(id) is { } entity && B(entity["active"]) && entity["observedOrdinal"] is not null &&
        KitchenModel.Components(entity).Contains("AttachStation") && !NativeCookingStation(entity) && !KitchenModel.Components(entity).Contains("MixingStation");
    private bool NearReadyPotSource(int source, int food) => NearReadyPotStation(source) && Free(source, food) && Attached(source) == food &&
        Entity(food)?["observedOrdinal"] is not null && B(Entity(food)?["active"]) && IsLooseChoppedBun(food) && !HeldByAnyone(food);

    private NearReadyPot? PlanNearReadyPot(int player, int index, int source, int food, int pot, int pan, int output)
    {
        if (!options.NearReadyPotHarvest || !PotCookAvailable(player) || !NativeNearReadyPot(pot) ||
            !NearReadyPotSource(source, food) || !NearReadyPotStation(output) || output != source && !EmptyAttachment(output) ||
            index < delivered || index >= recipes.Length || IsDonut(recipes[index]) || assembling.Contains(index) || mealFoods.ContainsKey(index)) return null;
        int home = potHomes[pot].Home;
        int[] identities = new[] { source, food, pot, home, pan, output }.Where(i => i != 0).Distinct().ToArray();
        if (!Free(identities) || identities.Any(id => Entity(id)?["observedOrdinal"] is null || !B(Entity(id)?["active"]))) return null;
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]), progress = N(Entity(pot)?["cookingProgress"]);
        if (!(speed > 0) || !double.IsFinite(speed)) return null;
        var obstacles = TrafficObstacles(player);
        var pickup = Navigation.ToStation(model, Position(player), Station(source)!, obstacles);
        if (!pickup.Success) return null;
        var approach = Navigation.ToStation(model, pickup.Points[^1], Station(pot)!, obstacles);
        if (!approach.Success) return null;
        double beforeHarvest = (pickup.Length + approach.Length) / speed + Math.Max(0, 12 - progress) + 2;
        if (!double.IsFinite(beforeHarvest) || beforeHarvest >= 23 - progress) return null;
        Point2 end = approach.Points[^1]; double total = beforeHarvest;
        NavigationPath? onion = null;
        if (pan != 0)
        {
            if (!recipes[index].RequiredInputs.Contains(CarnivalRecipes.Onion) || HasEarlyOnionLease(index) || !Cooked(pan, CarnivalRecipes.Onion.Id) || Station(pan) is not { } panStation) return null;
            onion = Navigation.ToStation(model, end, panStation, obstacles);
            if (!onion.Success) return null;
            total += onion.Length / speed + 1;
            if (NativeCookingStation(Entity(AttachmentParent(pan))) && total >= 21 - N(Entity(pan)?["cookingProgress"])) return null;
            end = onion.Points[^1];
        }
        var returning = Navigation.ToStation(model, end, Station(output)!, obstacles);
        total += returning.Length / speed + 1;
        if (!returning.Success || !double.IsFinite(total) || total >= 12) return null;
        var plan = new NearReadyPot(player, index, source, food, pot, home, pan, output, Frame, new JsonObject {
            ["nativeCookingProgress"] = progress, ["remainingNativeCookSeconds"] = Math.Max(0, 12 - progress),
            ["estimatedSecondsToConsumption"] = beforeHarvest, ["guardRemainingSeconds"] = 23 - progress,
            ["estimatedTotalSeconds"] = total, ["chefToBun"] = JsonSerializer.SerializeToNode(pickup),
            ["bunToPot"] = JsonSerializer.SerializeToNode(approach), ["potToPan"] = onion is null ? null : JsonSerializer.SerializeToNode(onion),
            ["returnToOriginalSelectedOutput"] = JsonSerializer.SerializeToNode(returning) });
        foreach (int id in identities) plan.Identities.Add(id, I(Entity(id)?["observedOrdinal"]));
        return plan;
    }

    private void RegisterNearReadyPot(NearReadyPot plan, Work work)
    {
        if (!work.OwnedResources.SetEquals(plan.Resources) || work.Active is not null)
            throw new InvalidOperationException("Near-ready pot lost its original unplated recipe/source/output reservation at admission.");
        plan.Work = work; nearReadyPots.Add(work, plan);
        Log("nativeNearReadyPotAdmitted", NearReadyPotStatus(plan));
    }
    private void RequireNearReadyPot(NearReadyPot plan, bool requireWork)
    {
        if (plan.Identities.Any(p => !SameObservedEntity(p.Key, p.Value) || !B(Entity(p.Key)?["active"])) ||
            !assembling.Contains(plan.Index) || plan.Index < delivered || Food(plan.Food).IsRuined || !NearReadyPotStation(plan.Output) ||
            !NearReadyPotStation(plan.Source) || !NativeCookingStation(Entity(plan.Home)) || NativeCannonFlight(plan.Player) ||
            !B(chefs[plan.Player]["controlsEnabled"]) || Region(plan.Player) != "center")
            throw new InvalidOperationException("Near-ready pot lost its exact native food, source/output, home, chef or recipe index.");
        var expected = plan.Resources.Where(id => !(plan.SourceReleased && id == plan.Source || plan.PotReleased && id == plan.Pot || plan.HomeReleased && id == plan.Home)).ToHashSet();
        if (requireWork && (!ReferenceEquals(workers[plan.Player], plan.Work) || !plan.Work.OwnedResources.SetEquals(expected) ||
            expected.Any(id => !reserved.Contains(id)) || workers.OfType<Work>().Any(w => w != plan.Work && w.OwnedResources.Overlaps(expected))))
            throw new InvalidOperationException("Near-ready pot lost its remaining exclusive Work leases.");
        if (!plan.PotReleased && (Entity(plan.Pot)?["cookingProgress"] is null || !double.IsFinite(N(Entity(plan.Pot)?["cookingProgress"])) || N(Entity(plan.Pot)?["cookingProgress"]) < 0))
            throw new InvalidOperationException("Near-ready pot lost its finite native cooking-progress observation.");
        if (Frame - plan.Started > 720 || !plan.PotReleased && !EmptyFood(plan.Pot) && N(Entity(plan.Pot)?["cookingProgress"]) >= 23)
            throw new TimeoutException("Near-ready pot exceeded its native harvest/input deadline.");
        if (!plan.PotReleased && (Attached(plan.Home) != plan.Pot || HeldByAnyone(plan.Pot) ||
            N(Entity(plan.Pot)?["cookingTime"]) != 12 || I(Entity(plan.Pot)?["cookingTypeId"]) != CarnivalRecipes.PotCookingStepId))
            throw new InvalidOperationException("Near-ready pot was moved or changed instead of harvested at its original native stove.");
        if (plan.Pan != 0 && NativeCookingStation(Entity(AttachmentParent(plan.Pan))) && !EmptyFood(plan.Pan) && N(Entity(plan.Pan)?["cookingProgress"]) >= 21)
            throw new TimeoutException("Near-ready pot's reserved onion pan exceeded its native consumption deadline.");
    }

    private void ObserveNearReadyPot(NearReadyPot plan, bool requireWork)
    {
        RequireNearReadyPot(plan, requireWork);
        if (Held(plan.Player) == plan.Food && !plan.PickedUp)
        {
            if (!IsLooseChoppedBun(plan.Food) || Attached(plan.Source) != 0)
                throw new InvalidOperationException("Near-ready pot did not begin with the original prepared bun pickup.");
            plan.PickedUp = true;
        }
        if (!plan.PotReleased && NativeSausagePot(plan.Pot, true) && !plan.CookedObserved)
        {
            plan.CookedObserved = true; plan.CookedFrame = Frame; Log("nativeNearReadyPotCooked", NearReadyPotStatus(plan));
        }
        if (!plan.PotReleased && EmptyFood(plan.Pot) && !plan.Harvested)
        {
            if (!plan.PickedUp || !plan.CookedObserved || Held(plan.Player) != plan.Food || !NativeEmptyPot(plan.Pot) ||
                !CarnivalRecipes.MatchRecipe(Entity(plan.Food), 296560, false).ReadyToDeliver)
                throw new InvalidOperationException("Near-ready pot emptied without observed Cooked sausage entering the exact held bun.");
            plan.Harvested = true; plan.HarvestFrame = Frame; Log("nativeNearReadyPotConsumed", NearReadyPotStatus(plan));
        }
        if (!plan.Harvested)
        {
            if (!NativeSausagePot(plan.Pot) || !IsLooseChoppedBun(plan.Food) ||
                plan.PickedUp && Held(plan.Player) != plan.Food || !plan.PickedUp && Attached(plan.Source) != plan.Food)
                throw new InvalidOperationException("Near-ready pot changed its reserved sausage or prepared bun before native consumption.");
        }
        else
        {
            if ((!plan.PotReleased && !NativeEmptyPot(plan.Pot)) ||
                !(CarnivalRecipes.MatchRecipe(Entity(plan.Food), 296560, false).ReadyToDeliver ||
                    plan.Pan != 0 && CarnivalRecipes.MatchRecipe(Entity(plan.Food), 472326, false).ReadyToDeliver) ||
                !(Held(plan.Player) == plan.Food || Held(plan.Player) == 0 && Attached(plan.Output) == plan.Food))
                throw new InvalidOperationException("Near-ready pot lost its exact finished base or returned it to the wrong selected output.");
        }
        if (!plan.SourceReleased && plan.Source != plan.Output && plan.PickedUp && Attached(plan.Source) != 0 ||
            plan.Output != plan.Source && Attached(plan.Output) != 0 && Attached(plan.Output) != plan.Food)
            throw new InvalidOperationException("Near-ready pot's still-reserved source or output was occupied by another item.");
    }
    private void ObserveNearReadyPotHarvests()
    {
        foreach (var plan in nearReadyPots.Values) ObserveNearReadyPot(plan, true);
    }
    private void CompleteNearReadyPot(NearReadyPot plan)
    {
        ObserveNearReadyPot(plan, false);
        if (!plan.Harvested || !plan.CookedObserved || Held(plan.Player) != 0 || Attached(plan.Output) != plan.Food ||
            !CarnivalRecipes.MatchRecipe(Entity(plan.Food), plan.Pan == 0 ? 296560 : 472326, false).ReadyToDeliver)
            throw new InvalidOperationException("Near-ready pot lacks exact native recipe completion and original selected output return.");
        nearReadyPots.Remove(plan.Work); Log("nativeNearReadyPotComplete", NearReadyPotStatus(plan));
    }
    private void RecordNearReadyPotRelease(Work work, int resource)
    {
        if (!nearReadyPots.TryGetValue(work, out var plan)) return;
        if (resource == plan.Source && resource != plan.Output && plan.PickedUp && Attached(plan.Source) == 0) plan.SourceReleased = true;
        else if (resource == plan.Pot && plan.Harvested && NativeEmptyPot(plan.Pot) && Attached(plan.Home) == plan.Pot) plan.PotReleased = true;
        else if (resource == plan.Home && plan.PotReleased) plan.HomeReleased = true;
        else throw new InvalidOperationException("Near-ready pot released a source/home/vessel without its completed native consumer evidence.");
    }
    private void ReleaseNearReadyPotHome(int player, Work work, int pot)
    {
        if (!nearReadyPots.TryGetValue(work, out var plan) || plan.Pot != pot) return;
        if (!plan.PotReleased || !plan.Harvested || !NativeEmptyPot(pot) || Attached(plan.Home) != pot)
            throw new InvalidOperationException("Near-ready pot home release lacks its original empty vessel proof.");
        ReleaseUnplatedResource(player, work, plan.Home, "near-ready-pot-native-consumer-completed-original-stove-released-with-empty-pot");
    }
    private JsonObject NearReadyPotStatus(NearReadyPot plan) => new() { ["frame"] = Frame, ["player"] = plan.Player, ["recipeIndex"] = plan.Index,
        ["source"] = plan.Source, ["food"] = plan.Food, ["pot"] = plan.Pot, ["home"] = plan.Home, ["pan"] = plan.Pan, ["output"] = plan.Output,
        ["startedFrame"] = plan.Started, ["cookedFrame"] = plan.CookedFrame, ["harvestFrame"] = plan.HarvestFrame,
        ["potReleased"] = plan.PotReleased, ["homeReleased"] = plan.HomeReleased, ["sourceReleased"] = plan.SourceReleased,
        ["identities"] = JsonSerializer.SerializeToNode(plan.Identities), ["admissionEvidence"] = plan.Evidence.DeepClone() };
}
