using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private bool CompatiblePartialMixer(int bowl, out int index, out int[] actual, out int[] required)
    {
        index = bowlAssignments.GetValueOrDefault(bowl, -1);
        actual = Food(bowl).IngredientIds.Order().ToArray();
        required = index >= 0 && index < recipes.Length ? recipes[index].RequiredInputs.Select(i => i.Id).Order().ToArray() : [];
        if (index < 0 || index >= recipes.Length || !IsDonut(recipes[index]) || Food(bowl).IsRuined ||
            actual.Length == 0 || actual.Length >= required.Length) return false;
        int[] wanted = required;
        return actual.GroupBy(i => i).All(g => g.Count() <= wanted.Count(i => i == g.Key));
    }

    private int ExactMissingMixerSupply(int bowl)
    {
        if (!CompatiblePartialMixer(bowl, out _, out int[] actual, out int[] required)) return 0;
        foreach (var pair in counterSupplies.OrderBy(p => p.Key))
        {
            if (pair.Value.Vessel != bowl || (pair.Value.Ingredient != CarnivalRecipes.Flour.Id && pair.Value.Ingredient != CarnivalRecipes.Egg.Id)) continue;
            int item = Attached(pair.Key), ingredient = pair.Value.Ingredient;
            if (item != 0 && required.Count(i => i == ingredient) > actual.Count(i => i == ingredient) &&
                Food(item).IngredientIds.SequenceEqual(new[] { ingredient }) && !Food(item).IsRuined)
                return pair.Key;
        }
        return 0;
    }

    private bool TryAddDeadlineMixerSupply(int player, int bowl)
    {
        int source = ExactMissingMixerSupply(bowl), item = Attached(source), home = AttachmentParent(bowl);
        if (source == 0 || item == 0 || home == 0 || !HeatCookAvailable(player) || !Free(bowl, source, item) ||
            !bowlHomes.TryGetValue(bowl, out int originalHome) || home != originalHome ||
            !KitchenModel.Components(Entity(home)!).Contains("MixingStation") || !CompatiblePartialMixer(bowl, out int index, out var actual, out _)) return false;
        var address = counterSupplies[source];
        int ingredient = address.Ingredient;
        int[] expected = actual.Append(ingredient).Order().ToArray();
        int bowlOrdinal = I(Entity(bowl)?["observedOrdinal"]), homeOrdinal = I(Entity(home)?["observedOrdinal"]), sourceOrdinal = I(Entity(source)?["observedOrdinal"]);
        double before = N(Entity(bowl)?["mixingProgress"]);
        return Start(player, "heat-deadline-add-addressed-mixer-ingredient-" + (index + 1),
            [BakeryAction("take", source), BakeryAction("place", bowl)], [bowl, source, item], () =>
        {
            if (I(Entity(bowl)?["observedOrdinal"]) != bowlOrdinal || I(Entity(home)?["observedOrdinal"]) != homeOrdinal ||
                I(Entity(source)?["observedOrdinal"]) != sourceOrdinal || AttachmentParent(bowl) != home || Held(player) != 0 ||
                Attached(source) != 0 || bowlAssignments.GetValueOrDefault(bowl, -1) != index || Food(bowl).IsRuined ||
                !Food(bowl).IngredientIds.Order().SequenceEqual(expected) ||
                !counterSupplies.TryGetValue(source, out var current) || current != address)
                throw new InvalidOperationException("Urgent mixer handoff lost its exact address, identities, or native one-ingredient delta.");
            counterSupplies.Remove(source);
            Log("nativeHeatMixerIngredientAccepted", new JsonObject { ["frame"] = Frame, ["bowl"] = bowl,
                ["orderIndex"] = index, ["ingredient"] = ingredient, ["source"] = source, ["item"] = item,
                ["previousObservedMixProgress"] = before, ["currentNativeMixProgress"] = Entity(bowl)?["mixingProgress"]?.DeepClone(),
                ["composition"] = Entity(bowl)?["composition"]?.DeepClone() });
        });
    }

    private bool TrySupplyUrgentMixerPrerequisite()
    {
        // A far bowl can keep processing while P1 washes. On return, its
        // existing clock outranks starting a fresh near bowl's next batch.
        // Production still uses ordinary Supply and its exact addressed
        // handoff; this neither injects an ingredient nor resets a clock.
        if (Held(1) != 0 || workers[1] is not null || Region(1) != "upper-right" ||
            !B(chefs[1]["controlsEnabled"]) || NativeCannonFlight(1)) return false;
        var pending = Stations("bowl").Where(s => CompatiblePartialMixer(s.EntityId, out _, out _, out _) &&
                bowlHomes.TryGetValue(s.EntityId, out int home) && Attached(home) == s.EntityId &&
                N(Entity(s.EntityId)?["mixingTime"]) == 12 && N(Entity(s.EntityId)?["mixingProgress"]) >= 9)
            .OrderByDescending(s => N(Entity(s.EntityId)?["mixingProgress"])).ThenBy(s => s.EntityId).ToArray();
        foreach (var bowl in pending)
        {
            int[] actual = Food(bowl.EntityId).IngredientIds;
            var ingredient = new[] { CarnivalRecipes.Flour, CarnivalRecipes.Egg }.FirstOrDefault(i => !actual.Contains(i.Id));
            if (ingredient is null) continue;
            if (!Free(bowl.EntityId) || counterSupplies.Values.Any(s => s.Vessel == bowl.EntityId)) return true;
            if (Supply(1, ingredient, bowl.EntityId, true))
                Log("nativeMixerUrgentSupplyStarted", new JsonObject { ["frame"] = Frame, ["bowl"] = bowl.EntityId,
                    ["orderIndex"] = bowlAssignments[bowl.EntityId], ["ingredient"] = ingredient.Id,
                    ["nativeMixProgress"] = Entity(bowl.EntityId)?["mixingProgress"]?.DeepClone() });
            // Do not wash or start a fresh bowl while this known missing
            // prerequisite waits for its normal receiving handoff to clear.
            return true;
        }
        return false;
    }
}
