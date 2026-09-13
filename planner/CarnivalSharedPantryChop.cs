using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record PantrySupply(int Supplier, NativeIngredient Ingredient, int Source, int Board, int SourceOrdinal, int BoardOrdinal);
    private sealed class SharedPantryChop(PantrySupply supply, int helper, int raw, int ordinal, int frame)
    {
        public readonly PantrySupply Supply = supply;
        public readonly int Helper = helper, Raw = raw, RawOrdinal = ordinal, Started = frame;
        public Work Work = null!;
        public bool NativeWorkObserved;
        public double MaximumProgress;
        public int WorkingSamples;
    }
    private readonly Dictionary<int, SharedPantryChop> sharedPantryChops = [];

    private static void ValidateSharedPantryOption(CarnivalPlannerOptions configuration)
    {
        if (configuration.SharedPantryChopping && !configuration.PantryChopping)
            throw new ArgumentException("Shared pantry chopping requires PantryChopping; use --pantry-chop with --share-pantry-chop.");
    }

    private bool NeutralNativeInput(int player)
    {
        var input = (response["inputs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(i => I(i["player"]) == player);
        return input is not null && new[] { "pickup", "use", "dash" }.All(k => input[k]?.GetValue<bool>() == false) &&
            input["x"] is not null && input["y"] is not null && N(input["x"]) == 0 && N(input["y"]) == 0;
    }

    private bool RawPantryItem(int item, NativeIngredient ingredient)
    {
        var entity = Entity(item);
        if (entity is null || entity["workProgress"] is null || N(entity["workProgress"]) != 0 ||
            !KitchenModel.Components(entity).Contains("ServerWorkableItem")) return false;
        var food = CarnivalRecipes.ClassifyEntity(entity);
        return food.EvidenceGaps.Length == 0 && food.Food.Kind == FoodNodeKind.Ingredient &&
            food.Food.Preparation == FoodPreparation.Raw && food.Food.IngredientIds.SequenceEqual(new[] { ingredient.Id });
    }

    private bool TrySharePantryChopping()
    {
        if (!options.SharedPantryChopping) return false;
        bool delegated = false;
        foreach (int supplier in new[] { 2, 1 })
        {
            var original = workers[supplier]; var supply = original?.PantrySupply;
            if (original is null || supply is null || !options.PantryChopping || supply.Supplier != supplier ||
                original.Active is not null || original.NeutralBoundaryFrame != Frame || original.Actions.Count != 1 ||
                original.CompletedBoundaryAction?["type"]?.ToString() != "place" || original.CompletedBoundaryTarget != supply.Board ||
                original.CompletedBoundaryHeld == 0 || original.Actions.Peek()["type"]?.ToString() != "chop" ||
                original.Actions.Peek()["station"]?.ToString() != supply.Board.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                preServiceStock?.Work == original || !TrafficControlsReady(supplier) || !NeutralNativeInput(supplier) || Held(supplier) != 0 ||
                Region(supplier) != (supplier == 2 ? "upper-left" : "upper-right")) continue;
            var board = Station(supply.Board); var source = Station(supply.Source);
            int item = Attached(supply.Board);
            if (board is not { Role: "chop" } || !board.Regions.Contains("center") || source?.Ingredient != supply.Ingredient.Name ||
                I(Entity(supply.Source)?["observedOrdinal"]) != supply.SourceOrdinal || I(Entity(supply.Board)?["observedOrdinal"]) != supply.BoardOrdinal ||
                item != original.CompletedBoundaryHeld || !RawPantryItem(item, supply.Ingredient) || !Free(item) ||
                !original.OwnedResources.SetEquals(new[] { supply.Source, supply.Board }) || original.OwnedResources.Any(id => !reserved.Contains(id))) continue;
            var candidates = new[] { 0, 3 }.Where(p => IdleTrafficHelper(p) && NeutralNativeInput(p) && Region(p) == "center")
                .Select(p => (Player: p, Path: Navigation.ToStation(model, Position(p), board, TrafficObstacles(p)),
                    Speed: N(chefs[p]["runSpeed"]) * N(chefs[p]["surfaceSpeedMultiplier"])))
                .Where(c => c.Path.Success && double.IsFinite(c.Speed) && c.Speed > 0 && c.Path.Length / c.Speed <= 1)
                .OrderBy(c => c.Path.Length).ThenBy(c => c.Player).ToArray();
            if (candidates.Length == 0) continue;
            var choice = candidates[0];
            var lease = new SharedPantryChop(supply, choice.Player, item, I(Entity(item)?["observedOrdinal"]), Frame);
            var chop = original.Actions.Dequeue();
            chop["dash"] = false; chop["shortDash"] = false; chop["timeoutFrames"] = 300;
            var work = new Work("shared-pantry-chop-" + supply.Ingredient.Name, [chop], [supply.Board, item], () => CompleteSharedPantryChop(lease));
            lease.Work = work;
            // Transfer the already held board lease without releasing it, and
            // reserve this exact observed raw item. The completed crate pickup
            // releases only its own source lease. The supplier's prepared-food
            // callback is deliberately not invoked: chopping has not happened.
            original.OwnedResources.Remove(supply.Board);
            ReleaseRemainingWorkResources(original);
            reserved.Add(item);
            workers[supplier] = null; playerCompletedAt[supplier] = Frame;
            workers[choice.Player] = work; sharedPantryChops.Add(choice.Player, lease);
            Log("pantryChopDelegated", new JsonObject { ["frame"] = Frame, ["supplier"] = supplier, ["helper"] = choice.Player,
                ["originalJob"] = original.Name, ["source"] = supply.Source, ["board"] = supply.Board,
                ["rawEntity"] = item, ["rawOrdinal"] = lease.RawOrdinal, ["ingredientId"] = supply.Ingredient.Id,
                ["completedSupplierAction"] = original.CompletedBoundaryAction.DeepClone(),
                ["supplierCompletion"] = "native raw placement completed; prepared-food callback transferred to a separately verified helper job",
                ["boardPath"] = JsonSerializer.SerializeToNode(choice.Path), ["nativeWalkingSecondsLowerBound"] = choice.Path.Length / choice.Speed });
            Log("plannerJobStart", new JsonObject { ["player"] = choice.Player, ["name"] = work.Name, ["frame"] = Frame,
                ["resources"] = JsonSerializer.SerializeToNode(work.Resources), ["actions"] = JsonSerializer.SerializeToNode(work.Actions) });
            delegated = true;
        }
        return delegated;
    }

    private void ObserveSharedPantryChopping()
    {
        foreach (var lease in sharedPantryChops.Values)
        {
            if (workers[lease.Helper] != lease.Work || !lease.Work.OwnedResources.SetEquals(new[] { lease.Supply.Board, lease.Raw }) ||
                lease.Work.OwnedResources.Any(id => !reserved.Contains(id)))
                throw new InvalidOperationException("Shared pantry chop lost its exact helper or native board/item reservation.");
            if (Frame - lease.Started > 300) throw new TimeoutException("Shared pantry chopping exceeded its native-frame bound.");
            if (I(Entity(lease.Supply.Board)?["observedOrdinal"]) != lease.Supply.BoardOrdinal)
                throw new InvalidOperationException("Shared pantry chopping board identity changed.");
            var raw = Entity(lease.Raw);
            if (raw is null || I(raw["observedOrdinal"]) != lease.RawOrdinal) continue;
            if (Attached(lease.Supply.Board) != lease.Raw || !KitchenModel.Components(raw).Contains("ServerWorkableItem"))
                throw new InvalidOperationException("Shared pantry raw ingredient moved before its native prepared replacement.");
            int interaction = I(chefs[lease.Helper]["serverInteractionId"]);
            double progress = N(raw["workProgress"]);
            if (progress > 0 && (interaction == lease.Supply.Board || interaction == lease.Raw))
            {
                lease.NativeWorkObserved = true; lease.WorkingSamples++; lease.MaximumProgress = Math.Max(lease.MaximumProgress, progress);
            }
        }
    }

    private void CompleteSharedPantryChop(SharedPantryChop lease)
    {
        int item = Attached(lease.Supply.Board); var prepared = Entity(item); var food = CarnivalRecipes.ClassifyEntity(prepared);
        if (!lease.NativeWorkObserved || item == 0 || item == lease.Raw && I(prepared?["observedOrdinal"]) == lease.RawOrdinal ||
            prepared is null || KitchenModel.Components(prepared).Any(c => c is "WorkableItem" or "ServerWorkableItem") ||
            food.EvidenceGaps.Length != 0 || food.Food.Kind != FoodNodeKind.Ingredient || food.Food.Preparation != FoodPreparation.Chopped ||
            !food.Food.IngredientIds.SequenceEqual(new[] { lease.Supply.Ingredient.Id }) || Held(lease.Helper) != 0 ||
            Region(lease.Helper) != "center" || !B(chefs[lease.Helper]["controlsEnabled"]))
            throw new InvalidOperationException("Shared pantry completion lacks observed native work and the exact prepared replacement on its reserved board.");
        sharedPantryChops.Remove(lease.Helper);
        Log("sharedPantryChopComplete", new JsonObject { ["frame"] = Frame, ["supplier"] = lease.Supply.Supplier,
            ["helper"] = lease.Helper, ["board"] = lease.Supply.Board, ["rawEntity"] = lease.Raw, ["preparedEntity"] = item,
            ["ingredientId"] = lease.Supply.Ingredient.Id, ["nativeWorkingSamples"] = lease.WorkingSamples, ["maximumNativeWorkProgress"] = lease.MaximumProgress });
    }

    private JsonArray SharedPantryStatus() => new(sharedPantryChops.Values.OrderBy(l => l.Helper).Select(l => (JsonNode?)new JsonObject {
        ["helper"] = l.Helper, ["supplier"] = l.Supply.Supplier, ["board"] = l.Supply.Board, ["rawEntity"] = l.Raw,
        ["ingredientId"] = l.Supply.Ingredient.Id, ["startedFrame"] = l.Started, ["nativeWorkingSamples"] = l.WorkingSamples,
        ["maximumNativeWorkProgress"] = l.MaximumProgress }).ToArray());
}
