using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // This is an ordinary Work reservation, not a parked-vessel lease. Native
    // mixing continues while the chef walks and waits beside the mixer.
    private sealed class NearReadyDoughTransfer(int owner, int bowl, int mixer, int basket, int fryer, int index,
        int frame, Dictionary<int, int> identities)
    {
        public readonly int Owner = owner, Bowl = bowl, Mixer = mixer, Basket = basket, Fryer = fryer, Index = index, Started = frame;
        public readonly Dictionary<int, int> Identities = identities;
        public Work Work = null!;
        public bool ObservedMixed, PickedUp, Transferred;
        public int[] Resources => [Bowl, Mixer, Basket, Fryer];
    }
    private readonly Dictionary<Work, NearReadyDoughTransfer> nearReadyDoughTransfers = [];

    private sealed record NearReadyDoughAdmission(int Basket, int Fryer, double Progress, double Seconds,
        NavigationPath Approach, NavigationPath Transfer, NavigationPath Restore);

    private NearReadyDoughAdmission? PlanNearReadyDough(int player, int bowl, int index, int mixer, Point2? start = null)
    {
        if (!HeatCookAvailable(player) || index < 0 || index >= recipes.Length || !WithinOrdinaryWindow(index) ||
            !BakeryFryingAdmitted(bowl, index) || bakeryLeases.ContainsKey(bowl) || !Free(bowl, mixer) ||
            Attached(mixer) != bowl || HeldByAnyone(bowl) || !AssignedDoughIngredients(bowl, index) ||
            AssignedDoughReady(Food(bowl), recipes[index]) || N(Entity(bowl)?["mixingTime"]) != 12 ||
            N(Entity(bowl)?["mixingProgress"]) is < 9 or >= 12 ||
            !KitchenModel.Components(Entity(mixer) ?? new()).Contains("MixingStation") ||
            Entity(bowl)?["observedOrdinal"] is null || Entity(mixer)?["observedOrdinal"] is null) return null;
        var evidence = CarnivalRecipes.ClassifyEntity(Entity(bowl));
        if (evidence.EvidenceGaps.Length != 0 || !evidence.Food.DescendantsAndSelf().Any(n => n.Kind == FoodNodeKind.Mixed && n.Preparation == FoodPreparation.Mixing)) return null;

        double progress = N(Entity(bowl)?["mixingProgress"]);
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        if (!double.IsFinite(speed) || speed <= 0 || !double.IsFinite(progress)) return null;
        var obstacles = TrafficObstacles(player);
        var source = Station(mixer);
        if (source is null) return null;
        var approach = Navigation.ToStation(model, start ?? Position(player), source, obstacles);
        if (!approach.Success) return null;
        foreach (var target in Stations("basket").OrderBy(s => s.Position.Distance(source.Position)).ThenBy(s => s.EntityId))
        {
            int basket = target.EntityId;
            if (!fryerHomes.TryGetValue(basket, out int fryer) || !Free(basket, fryer) || fryerRescues.ContainsKey(basket) ||
                Attached(fryer) != basket || !EmptyFood(basket) || N(Entity(basket)?["cookingProgress"]) != 0 ||
                N(Entity(basket)?["cookingTime"]) != 10 || !NativeCookingStation(Entity(fryer)) ||
                Entity(basket)?["observedOrdinal"] is null || Entity(fryer)?["observedOrdinal"] is null) continue;
            var transfer = Navigation.ToStation(model, approach.Points[^1], target, obstacles);
            if (!transfer.Success) continue;
            var restore = Navigation.ToStation(model, transfer.Points[^1], source, obstacles);
            // Include both travel legs even though detaching the bowl ends its
            // overmix risk earlier. Wait time is native, never simulated here.
            double seconds = (approach.Length + transfer.Length) / speed + Math.Max(0, 12 - progress) + 2;
            if (!restore.Success || !double.IsFinite(seconds) || seconds >= 21 - progress || restore.Length / speed + 2 >= 19) continue;
            return new(basket, fryer, progress, seconds, approach, transfer, restore);
        }
        return null;
    }

    private bool TryTransferNearReadyDough(int player, int bowl, int index, int mixer)
    {
            var admitted = PlanNearReadyDough(player, bowl, index, mixer);
            if (admitted is null) return false;
            int basket = admitted.Basket, fryer = admitted.Fryer;
            double progress = admitted.Progress, seconds = admitted.Seconds;
            var approach = admitted.Approach; var transfer = admitted.Transfer;
            var plan = new NearReadyDoughTransfer(player, bowl, mixer, basket, fryer, index, Frame,
                new[] { bowl, mixer, basket, fryer }.ToDictionary(id => id, id => I(Entity(id)?["observedOrdinal"])));
            bool started = Start(player, "wait-then-transfer-mixed-dough", [BakeryAction("navigate", mixer), BakeryAction("mix", bowl, 180),
                BakeryAction("take", mixer), BakeryAction("combine", basket), BakeryAction("place", mixer)], plan.Resources, () =>
            {
                RequireNearReadyDoughIdentity(plan, requireWork: false);
                if (!plan.ObservedMixed || !plan.PickedUp || !plan.Transferred || !EmptyFood(bowl) || Attached(mixer) != bowl ||
                    Attached(fryer) != basket || Held(player) != 0 || !AssignedDoughReady(Food(basket), recipes[index]))
                    throw new InvalidOperationException("Near-ready transfer did not prove native mixing, exact fryer transfer and original empty bowl restoration.");
                basketAssignments[basket] = index; bowlAssignments.Remove(bowl); bowlFlavors.Remove(bowl);
                nearReadyDoughTransfers.Remove(plan.Work);
                Log("nativeNearReadyDoughComplete", NearReadyDoughStatus(plan));
            });
            if (!started) return false;
            plan.Work = workers[player]!; nearReadyDoughTransfers.Add(plan.Work, plan);
            var description = NearReadyDoughStatus(plan);
            description["nativeProgress"] = progress; description["estimatedSeconds"] = seconds;
            description["guardSeconds"] = 21; description["remainingNativeMixSeconds"] = Math.Max(0, 12 - progress);
            description["approachLength"] = approach.Length; description["transferLength"] = transfer.Length;
            Log("nativeNearReadyDoughAdmitted", description);
            return true;
    }

    private void RequireNearReadyDoughIdentity(NearReadyDoughTransfer plan, bool requireWork = true)
    {
        if (plan.Identities.Any(pair => !SameObservedEntity(pair.Key, pair.Value)) ||
            bowlAssignments.GetValueOrDefault(plan.Bowl, -1) != plan.Index || bakeryLeases.ContainsKey(plan.Bowl) ||
            N(Entity(plan.Bowl)?["mixingTime"]) != 12 || N(Entity(plan.Basket)?["cookingTime"]) != 10 ||
            !KitchenModel.Components(Entity(plan.Mixer) ?? new()).Contains("MixingStation") || !NativeCookingStation(Entity(plan.Fryer)) ||
            (requireWork && (!(ReferenceEquals(workers[plan.Owner], plan.Work) || IsSafeServiceSuspendedWork(plan.Owner, plan.Work)) || !plan.Work.OwnedResources.SetEquals(plan.Resources) ||
                plan.Resources.Any(id => !reserved.Contains(id)) || workers.OfType<Work>().Any(w => w != plan.Work && w.OwnedResources.Overlaps(plan.Resources)))))
            throw new InvalidOperationException("Near-ready dough lost its original recipe, vessel identities or exclusive work ownership.");
    }

    private void ObserveNearReadyDoughTransfers()
    {
        foreach (var plan in nearReadyDoughTransfers.Values)
        {
            RequireNearReadyDoughIdentity(plan);
            if (Frame - plan.Started > 720 || !B(chefs[plan.Owner]["controlsEnabled"]) || NativeCannonFlight(plan.Owner) ||
                Food(plan.Bowl).IsRuined || Food(plan.Basket).IsRuined)
                throw new TimeoutException("Near-ready dough transfer lost native controls or exceeded its bounded food deadline.");
            if (Attached(plan.Fryer) != plan.Basket ||
                Attached(plan.Mixer) == plan.Bowl && N(Entity(plan.Bowl)?["mixingProgress"]) >= 21 ||
                !EmptyFood(plan.Basket) && N(Entity(plan.Basket)?["cookingProgress"]) >= 19)
                throw new TimeoutException("Near-ready dough remained on a native processing station past its safety deadline.");
            bool empty = EmptyFood(plan.Bowl), mixed = AssignedDoughReady(Food(plan.Bowl), recipes[plan.Index]);
            if (!empty && !AssignedDoughIngredients(plan.Bowl, plan.Index))
                throw new InvalidOperationException("Waiting bowl ingredients changed outside their exact recipe reservation.");
            if (mixed && !plan.ObservedMixed) { plan.ObservedMixed = true; Log("nativeNearReadyDoughMixed", NearReadyDoughStatus(plan)); }
            if (Held(plan.Owner) == plan.Bowl && !plan.PickedUp)
            {
                if (!plan.ObservedMixed || !mixed || Attached(plan.Mixer) != 0)
                    throw new InvalidOperationException("Near-ready bowl was picked up before native Mixed completion.");
                plan.PickedUp = true; Log("nativeNearReadyDoughDetached", NearReadyDoughStatus(plan));
            }
            if (!plan.PickedUp && (Attached(plan.Mixer) != plan.Bowl || Held(plan.Owner) != 0 || !EmptyFood(plan.Basket)))
                throw new InvalidOperationException("Near-ready wait changed its original mixer attachment or empty fryer.");
            if (empty && !plan.Transferred)
            {
                if (!plan.PickedUp || !AssignedDoughReady(Food(plan.Basket), recipes[plan.Index]))
                    throw new InvalidOperationException("Near-ready dough disappeared without entering its exact native fryer.");
                plan.Transferred = true; Log("nativeNearReadyDoughTransferred", NearReadyDoughStatus(plan));
            }
            if (plan.PickedUp && (Held(plan.Owner) != plan.Bowl && !(plan.Transferred && Held(plan.Owner) == 0 && Attached(plan.Mixer) == plan.Bowl)))
                throw new InvalidOperationException("Near-ready transfer lost its carried original bowl or restored home.");
            if (plan.PickedUp && Held(plan.Owner) == plan.Bowl && Attached(plan.Mixer) != 0)
                throw new InvalidOperationException("Near-ready transfer's reserved original mixer was occupied while its bowl was carried.");
            if (plan.Transferred && (!empty || !AssignedDoughReady(Food(plan.Basket), recipes[plan.Index])))
                throw new InvalidOperationException("Near-ready transfer changed its already observed native fryer contents.");
        }
    }

    private JsonObject NearReadyDoughStatus(NearReadyDoughTransfer p) => new() { ["frame"] = Frame, ["player"] = p.Owner,
        ["bowl"] = p.Bowl, ["mixer"] = p.Mixer, ["basket"] = p.Basket, ["fryer"] = p.Fryer, ["recipeIndex"] = p.Index,
        ["startedFrame"] = p.Started, ["observedMixed"] = p.ObservedMixed, ["pickedUp"] = p.PickedUp, ["transferred"] = p.Transferred };
}
