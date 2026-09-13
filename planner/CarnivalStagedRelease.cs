using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class UnplatedTransfer(int food, int board, int pot, int potHome, int foodOrdinal, int boardOrdinal, int potOrdinal)
    {
        public readonly int Food = food, Board = board, Pot = pot, PotHome = potHome;
        public readonly int FoodOrdinal = foodOrdinal, BoardOrdinal = boardOrdinal, PotOrdinal = potOrdinal;
        public bool PickupCompleted, PotCombineCompleted;
    }

    private static bool StationIs(JsonObject action, int resource) =>
        int.TryParse(action["station"]?.ToString(), out int id) && id == resource;

    private static void ObserveUnplatedActionCompletion(Work work, JsonObject action)
    {
        if (work.ReleasableTransfer is not { } transfer) return;
        if (action["type"]?.ToString() == "take" && StationIs(action, transfer.Board)) transfer.PickupCompleted = true;
        if (action["type"]?.ToString() == "combine" && StationIs(action, transfer.Pot)) transfer.PotCombineCompleted = true;
    }

    private static bool WorkStillUsesStation(Work work, int resource) => work.Actions.Any(a => StationIs(a, resource)) ||
        work.Active is { IsDone: false } active && StationIs(active.Specification, resource);

    private bool SameObservedEntity(int id, int ordinal) => Entity(id) is { } entity &&
        (ordinal < 0 || entity["observedOrdinal"] is not null && I(entity["observedOrdinal"]) == ordinal);

    private void ReleaseRemainingWorkResources(Work work)
    {
        // Resources is the historical job declaration. Only this mutable set
        // still belongs to this job after native staged release. A newer job
        // may already hold one of the historical resources when this completes.
        foreach (int resource in work.OwnedResources) reserved.Remove(resource);
        work.OwnedResources.Clear();
    }

    private void ReleaseUnplatedResource(int player, Work work, int resource, string proof)
    {
        if (!work.OwnedResources.Contains(resource) || !reserved.Contains(resource))
            throw new InvalidOperationException("Unplated staged release lost its current resource ownership.");
        work.OwnedResources.Remove(resource);
        reserved.Remove(resource);
        RecordNearReadyPotRelease(work, resource);
        Log("plannerResourceReleased", new JsonObject { ["player"] = player, ["name"] = work.Name,
            ["resource"] = resource, ["frame"] = Frame, ["proof"] = proof,
            ["heldFood"] = Held(player), ["remainingOwnedResources"] = new JsonArray(work.OwnedResources.Order().Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) });
    }

    private void ReleaseCompletedUnplatedResources()
    {
        if (!options.StagedResourceRelease) return;
        for (int player = 0; player < 4; player++)
        {
            if (workers[player] is not { ReleasableTransfer: { } transfer } work ||
                Held(player) != transfer.Food || !SameObservedEntity(transfer.Food, transfer.FoodOrdinal) ||
                Food(transfer.Food).IsRuined) continue;
            if (work.OwnedResources.Contains(transfer.Board) && transfer.PickupCompleted &&
                !WorkStillUsesStation(work, transfer.Board) && SameObservedEntity(transfer.Board, transfer.BoardOrdinal) &&
                Entity(transfer.Board)?["attachedEntityId"] is not null && Attached(transfer.Board) == 0 &&
                Food(transfer.Food).IngredientIds.Contains(CarnivalRecipes.Bun.Id))
                ReleaseUnplatedResource(player, work, transfer.Board, "completed-native-pickup-exact-food-held-source-empty-no-future-source-use");
            if (!potRescues.ContainsKey(transfer.Pot) && work.OwnedResources.Contains(transfer.Pot) && transfer.PotCombineCompleted &&
                !WorkStillUsesStation(work, transfer.Pot) && SameObservedEntity(transfer.Pot, transfer.PotOrdinal) &&
                Entity(transfer.Pot)?["composition"] is not null && EmptyFood(transfer.Pot) &&
                Entity(transfer.Pot)?["cookingProgress"] is not null && N(Entity(transfer.Pot)?["cookingProgress"]) == 0 && !HeldByAnyone(transfer.Pot) &&
                transfer.PotHome != 0 && AttachmentParent(transfer.Pot) == transfer.PotHome &&
                NativeCookingStation(Entity(transfer.PotHome)) &&
                CarnivalRecipes.MatchRecipe(Entity(transfer.Food), 296560, false).ReadyToDeliver)
            {
                ReleaseUnplatedResource(player, work, transfer.Pot, "completed-native-combine-exact-hotdog-held-pot-empty-reset-at-original-stove-no-future-pot-use");
                ReleaseNearReadyPotHome(player, work, transfer.Pot);
            }
        }
    }
}
