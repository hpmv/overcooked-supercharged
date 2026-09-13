using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // One free plate is deliberately retained for the FIFO head. Requiring two
    // plates before supplying that head's missing dough can form a closed wait:
    // future plates cannot be served, so no further washing can create a plate.
    private JsonObject? UrgentFifoBakeryEvidence()
    {
        if (delivered >= recipes.Length || !IsDonut(recipes[delivered]) || mealPlates.ContainsKey(delivered) ||
            plating.Contains(delivered) || assembling.Contains(delivered) || Held(1) != 0 || Region(1) != "lower-left" ||
            !B(chefs[1]["controlsEnabled"]) || NativeCannonFlight(1)) return null;
        int sink = Single("sink"), drying = Single("drying"), handoff = Counter(15.6, -19.2);
        // Native ready output and already delivered washing work retain their
        // existing priority. A remote or carried stack is logged separately:
        // this explicit FIFO escape may leave before its pending relay arrives.
        if (I(Entity(sink)?["plateCount"]) != 0 || I(Entity(drying)?["plateCount"]) != 0 || Attached(handoff) != 0) return null;
        int plate = AvailablePlates().Where(e => B(e["active"]) && CanAdoptPlate(e)).Select(Id).FirstOrDefault();
        if (plate == 0) return null;
        var recipe = recipes[delivered]; int[] required = recipe.RequiredInputs.Select(i => i.Id).Order().ToArray();
        // Full dough already mixing, frying, parked or ready requires central
        // processing, not a supplier excursion. Do not infer urgency from time.
        if (Stations("bowl").Concat(Stations("basket")).Any(s => !Food(s.EntityId).IsRuined &&
            Food(s.EntityId).IngredientIds.Order().SequenceEqual(required))) return null;
        AssignBowls();
        foreach (var assignment in bowlAssignments.Where(p => p.Value == delivered).OrderBy(p => p.Key))
        {
            int bowl = assignment.Key;
            if (Entity(bowl) is not { } entity || !B(entity["active"]) || HeldByAnyone(bowl) ||
                !bowlFlavors.TryGetValue(bowl, out int flavor)) continue;
            var food = Food(bowl); int[] present = food.IngredientIds;
            if (food.IsRuined || present.Any(id => !required.Contains(id)) || present.GroupBy(id => id).Any(g => g.Count() > 1)) continue;
            var missing = required.Where(id => !present.Contains(id)).Where(id =>
                !counterSupplies.Values.Any(s => s.Vessel == bowl && s.Ingredient == id) &&
                (id == CarnivalRecipes.Flour.Id || id == CarnivalRecipes.Egg.Id || id == flavor && LooseIngredientCount(id) == 0)).ToArray();
            if (missing.Length == 0) continue;
            int carriedDirty = chefs.Select(c => I(c["heldEntityId"])).Where(id => id != 0 && IsDirty(id)).Distinct()
                .Sum(id => I(Entity(id)?["plateCount"]));
            return new JsonObject { ["frame"] = Frame, ["orderIndex"] = delivered, ["recipeId"] = recipe.Id,
                ["bowl"] = bowl, ["observedAvailableHeadPlate"] = plate, ["missingSupplyIngredientIds"] = JsonSerializer.SerializeToNode(missing),
                ["carriedDirtyPlateCount"] = carriedDirty, ["remoteDirtyReturnCount"] = I(Entity(Single("dirty-return"))?["plateCount"]),
                ["reason"] = "FIFO donut lacks supplier ingredients; one real clean plate is available, with no immediate sink, drying or handoff work" };
        }
        return null;
    }

    private bool TryUrgentFifoBakeryReturn()
    {
        var evidence = UrgentFifoBakeryEvidence();
        if (evidence is null || !Start(1, "urgent-fifo-return-to-bakery", [Portal("upper-right")], [])) return false;
        Log("urgentFifoBakeryReturn", evidence); return true;
    }
}
