using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private Action? AppendPantryChop(int player, NativeIngredient ingredient, int destination, bool vessel, List<JsonObject> actions)
    {
        if (!options.PantryChopping || vessel || ingredient.ChopImpacts <= 0) return null;
        string expectedRegion = player switch { 2 => "upper-left", 1 => "upper-right", _ => "" };
        bool expectedIngredient = player == 2 && (ingredient == CarnivalRecipes.Bun || ingredient == CarnivalRecipes.Onion) ||
            player == 1 && (ingredient == CarnivalRecipes.Chocolate || ingredient == CarnivalRecipes.Raspberry);
        if (!expectedIngredient || Station(destination) is not { Role: "chop" } board || !board.Regions.Contains(expectedRegion))
            throw new InvalidOperationException("Pantry chopping requires the supplier's native shared board and its verified ingredient family.");
        // The existing supply job keeps its destination board reserved until
        // the raw item's native replacement has been observed. Central cooks
        // cannot duplicate chopping or remove its ingredient midway through.
        actions.Add(A("chop", destination));
        return () =>
        {
            int item = Attached(destination);
            var observed = CarnivalRecipes.ClassifyEntity(Entity(item));
            if (item == 0 || Held(player) != 0 || Region(player) != expectedRegion || !B(chefs[player]["controlsEnabled"]) ||
                observed.EvidenceGaps.Length != 0 || observed.Food.Kind != FoodNodeKind.Ingredient ||
                observed.Food.Preparation != FoodPreparation.Chopped || !observed.Food.IngredientIds.SequenceEqual(new[] { ingredient.Id }) ||
                Entity(item) is not { } prepared || KitchenModel.Components(prepared).Any(c => c is "WorkableItem" or "ServerWorkableItem"))
                throw new InvalidOperationException("Pantry supply completion lacks its exact native prepared ingredient, unchanged board and empty controlled supplier.");
            Log("pantryChopComplete", new JsonObject { ["player"] = player, ["board"] = destination, ["preparedEntity"] = item,
                ["ingredientId"] = ingredient.Id, ["gameplayFrame"] = Frame });
        };
    }

    /// <summary>Offline supply/board ownership and native completion-shape regressions; no game calls.</summary>
    public static int PantryChopSelfTest(JsonObject initialSnapshot)
    {
        int count = 0;
        void Check(bool value, string description)
        { if (!value) throw new InvalidOperationException("Pantry chopping regression: " + description); count++; }
        void Reject(Action action, string description)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, description);
        }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline pantry fixture attempted I/O."), null)
            { response = initialSnapshot.DeepClone().AsObject(), options = new(PantryChopping: enabled), recipes = CarnivalRecipes.All.ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); return p;
        }
        void Prepared(CarnivalPlanner p, int board, NativeIngredient ingredient)
        {
            int id = p.entities.Keys.Max() + 1;
            p.entities[id] = new JsonObject { ["id"] = id, ["name"] = ingredient.Name + "_prepared", ["active"] = true,
                ["components"] = new JsonArray("CarryableItem"), ["composition"] = new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = ingredient.Id },
                ["position"] = p.Entity(board)!["position"]!.DeepClone() };
            p.Entity(board)!["attachedEntityId"] = id;
        }
        var disabled = Make(false); int bunBoard = disabled.Board("upper-left", false);
        Check(disabled.Supply(2, CarnivalRecipes.Bun, bunBoard, false) &&
            disabled.workers[2]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }) &&
            disabled.workers[2]!.Complete is null, "default-off supply retains its original actions and completion behavior");
        foreach (var (player, ingredient, upper) in new[] { (2, CarnivalRecipes.Bun, false), (2, CarnivalRecipes.Onion, true),
                     (1, CarnivalRecipes.Chocolate, false), (1, CarnivalRecipes.Raspberry, false) })
        {
            var p = Make(); int board = p.Board(player == 2 ? "upper-left" : "upper-right", upper);
            Check(p.Supply(player, ingredient, board, false), "native ingredient supplier admits an atomic chopping job: " + ingredient.Name);
            var work = p.workers[player]!;
            Check(work.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place", "chop" }) &&
                I(work.Actions.Last()["station"]) == board && work.Resources.Contains(board), "same board remains reserved through native chopping: " + ingredient.Name);
            Check(!p.Start(player == 2 ? 0 : 3, "conflicting-central-chop", [p.A("chop", board)], [board]),
                "central worker cannot duplicate the supplier's board job: " + ingredient.Name);
            Reject(() => work.Complete!(), "an empty board cannot count as prepared supply: " + ingredient.Name);
            Prepared(p, board, ingredient);
            work.Complete!(); Check(true, "native single prepared ingredient and empty supplier pass: " + ingredient.Name);
        }
        var vessel = Make(); int pot = vessel.Stations("pot").OrderBy(s => s.Position.X).First().EntityId;
        Check(vessel.Supply(2, CarnivalRecipes.Frankfurter, pot, true) && !vessel.workers[2]!.Actions.Any(a => a["type"]?.ToString() == "chop"),
            "direct native vessel supply never chops an already usable sausage");
        var flour = Make(); int bowl = flour.Stations("bowl").OrderByDescending(s => s.Position.X).First().EntityId;
        Check(flour.Supply(1, CarnivalRecipes.Flour, bowl, true) && !flour.workers[1]!.Actions.Any(a => a["type"]?.ToString() == "chop"),
            "bowl supply keeps native flour transfer unchanged");
        var bad = Make(); bad.Supply(2, CarnivalRecipes.Bun, bunBoard, false); var callback = bad.workers[2]!.Complete!;
        Prepared(bad, bunBoard, CarnivalRecipes.Onion);
        Reject(callback, "wrong prepared ingredient cannot satisfy the bun supply job");
        Prepared(bad, bunBoard, CarnivalRecipes.Bun); int item = bad.Attached(bunBoard);
        bad.Entity(item)!["components"] = new JsonArray("WorkableItem");
        Reject(callback, "raw workable object cannot substitute for its native prepared replacement");
        bad.Entity(item)!["components"] = new JsonArray("CarryableItem"); bad.chefs[2]["heldEntityId"] = item;
        Reject(callback, "supplier must leave the prepared food on the board with empty hands");
        bad.chefs[2]["heldEntityId"] = 0; bad.chefs[2]["controlsEnabled"] = false;
        Reject(callback, "a disabled or flying supplier cannot finish the chopping job");
        bad.chefs[2]["controlsEnabled"] = true;
        var actions = new List<JsonObject>();
        Reject(() => bad.AppendPantryChop(1, CarnivalRecipes.Bun, bunBoard, false, actions), "wrong supplier/ingredient/board combination is rejected");
        var shortDash = Make(); shortDash.options = new(PantryChopping: true, UseShortDash: true);
        shortDash.Supply(2, CarnivalRecipes.Bun, bunBoard, false);
        Check(B(shortDash.workers[2]!.Actions.Last()["dash"]) && B(shortDash.workers[2]!.Actions.Last()["shortDash"]),
            "existing native navigation guards retain explicit dash option forwarding");
        return count;
    }
}
