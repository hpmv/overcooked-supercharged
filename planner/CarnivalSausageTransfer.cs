using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // Native V18 parking took 228 frames just to pick up the reserved raw item:
    // an observed moving obstruction added a 14.47m detour. These allowances
    // cover both interactions and bounded recovery, once, for the whole job.
    // Replanning and completing pickup never restart this deadline.
    private const int SausageInteractionFrames = 90, SausageRecoveryFrames = 180, SausageMaximumTransferFrames = 600;
    private sealed class SausageTransfer(int source, int destination, int sourceOrdinal, int destinationOrdinal,
        int start, int budget, JsonObject evidence)
    {
        public readonly int Source = source, Destination = destination, SourceOrdinal = sourceOrdinal, DestinationOrdinal = destinationOrdinal;
        public readonly int Started = start, Budget = budget, Deadline = checked(start + budget);
        public readonly JsonObject Evidence = evidence;
        public bool PickedUp;
        public int ConsumedFrame = -1;
    }

    private static int SausagePathFrames(double length, double speed)
    {
        if (!double.IsFinite(length) || length < 0 || !double.IsFinite(speed) || speed <= 0) return 0;
        double frames = Math.Ceiling(length / speed * 60) + SausageInteractionFrames + SausageRecoveryFrames;
        return double.IsFinite(frames) && frames <= SausageMaximumTransferFrames ? (int)frames : 0;
    }

    private SausageTransfer? PlanSausageTransfer(int player, int source, int destination)
    {
        if (source == destination || Station(source) is not { } first || Station(destination) is not { } second ||
            Entity(source)?["observedOrdinal"] is null || Entity(destination)?["observedOrdinal"] is null ||
            !B(Entity(source)?["active"]) || !B(Entity(destination)?["active"])) return null;
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        if (!double.IsFinite(speed) || speed <= 0) return null;
        var obstacles = TrafficObstacles(player);
        var pickup = Navigation.ToStation(model, Position(player), first, obstacles);
        if (!pickup.Success || pickup.Points.Length == 0) return null;
        var placement = Navigation.ToStation(model, pickup.Points[^1], second, obstacles);
        if (!placement.Success || placement.Points.Length == 0) return null;
        int budget = SausagePathFrames(pickup.Length + placement.Length, speed);
        if (budget == 0) return null;
        return new(source, destination, I(Entity(source)?["observedOrdinal"]), I(Entity(destination)?["observedOrdinal"]), Frame, budget,
            new JsonObject { ["pickupPath"] = JsonSerializer.SerializeToNode(pickup), ["placementPath"] = JsonSerializer.SerializeToNode(placement),
                ["walkingSpeed"] = speed, ["plannedWalkFrames"] = budget - SausageInteractionFrames - SausageRecoveryFrames,
                ["interactionAllowanceFrames"] = SausageInteractionFrames, ["recoveryAllowanceFrames"] = SausageRecoveryFrames,
                ["maximumTransferFrames"] = SausageMaximumTransferFrames,
                ["policy"] = "One fixed two-action deadline; native walking paths plus interaction and bounded replan allowance. No deadline reset after pickup or replanning." });
    }

    private void RequireSausageTransfer(SausageLease lease, bool requireWork)
    {
        var transfer = lease.Transfer ?? throw new InvalidOperationException("Sausage transfer lacks its fixed admission budget.");
        if (lease.Phase is not (SausagePhase.Parking or SausagePhase.Loading) || lease.PhaseFrame != transfer.Started ||
            transfer.Budget is <= 0 or > SausageMaximumTransferFrames || Frame < transfer.Started || Frame > transfer.Deadline)
            throw new TimeoutException("Sausage buffer exceeded its fixed combined pickup/placement deadline.");
        if (lease.Owner is not (0 or 3) || !SameObservedEntity(transfer.Source, transfer.SourceOrdinal) ||
            !SameObservedEntity(transfer.Destination, transfer.DestinationOrdinal) ||
            !B(Entity(transfer.Source)?["active"]) || !B(Entity(transfer.Destination)?["active"]) ||
            !B(chefs[lease.Owner]["controlsEnabled"]) || NativeCannonFlight(lease.Owner))
            throw new InvalidOperationException("Sausage buffer transfer lost its exact source, destination or controlled owner.");
        int[] owned = lease.Phase == SausagePhase.Loading ? [lease.Pot, lease.Home] : [];
        if (requireWork && (lease.Work is null || !ReferenceEquals(workers[lease.Owner], lease.Work) ||
            !lease.Work.OwnedResources.SetEquals(owned) || owned.Any(id => !reserved.Contains(id)) ||
            workers.OfType<Work>().Any(w => w != lease.Work && w.OwnedResources.Overlaps(lease.Owned.Concat(owned)))))
            throw new InvalidOperationException("Sausage buffer transfer lost its original Work or exclusive remaining resource ownership.");
        if (Entity(lease.Food) is not null && !SameObservedEntity(lease.Food, lease.FoodOrdinal))
            throw new InvalidOperationException("Sausage transfer raw identity was replaced during active work.");

        int held = Held(lease.Owner), sourceFood = Attached(transfer.Source);
        bool inHand = held == lease.Food;
        if (inHand && !transfer.PickedUp)
        {
            RequireSausageFood(lease);
            if (sourceFood != 0) throw new InvalidOperationException("Sausage pickup left its reserved source occupied.");
            transfer.PickedUp = true;
        }
        if (lease.Phase == SausagePhase.Parking)
        {
            RequireSausageFood(lease);
            bool placed = Attached(lease.Counter) == lease.Food && held == 0;
            if (!transfer.PickedUp && (sourceFood != lease.Food || held != 0) ||
                transfer.PickedUp && (sourceFood != 0 || !inHand && !placed) ||
                Attached(lease.Counter) != 0 && !placed ||
                chefs.Any(c => I(c["playerId"]) != lease.Owner && I(c["heldEntityId"]) == lease.Food))
                throw new InvalidOperationException("Sausage parking changed its exact source/hand/destination transition.");
        }
        else
        {
            if (transfer.Destination != lease.Pot || Attached(lease.Home) != lease.Pot || HeldByAnyone(lease.Pot) ||
                !SameObservedEntity(lease.Home, lease.HomeOrdinal) || !SameObservedEntity(lease.Pot, lease.PotOrdinal) ||
                !NativeCookingStation(Entity(lease.Home)))
                throw new InvalidOperationException("Sausage loading lost its exact original pot and stove.");
            bool consumed = transfer.PickedUp && sourceFood == 0 && held == 0 &&
                Food(lease.Pot) is { Kind: FoodNodeKind.Cooked, IsRuined: false } food && food.CookingStepId == CarnivalRecipes.PotCookingStepId &&
                food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id });
            if (consumed)
            {
                if (transfer.ConsumedFrame < 0) transfer.ConsumedFrame = Frame;
                if (B(Entity(lease.Food)?["active"]) && Frame - transfer.ConsumedFrame > 2)
                    throw new InvalidOperationException("Sausage loading retained its raw source beyond the bounded native destruction observation.");
            }
            else
            {
                RequireSausageFood(lease);
                if (transfer.ConsumedFrame >= 0 || !EmptyFood(lease.Pot) || N(Entity(lease.Pot)?["cookingProgress"]) != 0 ||
                    !transfer.PickedUp && (sourceFood != lease.Food || held != 0) ||
                    transfer.PickedUp && (sourceFood != 0 || !inHand))
                    throw new InvalidOperationException("Sausage loading changed its reserved raw source or native empty target before consumption.");
            }
        }
    }
}
