using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private JsonObject[] ActiveRuinedFood() => entities.Values.Where(e =>
    {
        // Require the live native composition. Numeric cook progress, warning
        // labels and copied Rigidbody proxies alone are not ruin evidence.
        if (!B(e["active"]) || e["composition"] is not JsonObject || e["spawnPrefab"] is not null ||
            (e["name"]?.ToString().EndsWith("_Rigidbody", StringComparison.Ordinal) ?? false)) return false;
        int id = Id(e);
        if (!HeldByAnyone(id) && AttachmentParent(id) == 0)
        {
            var solids = (e["colliders"] as JsonArray)?.OfType<JsonObject>().Where(c => c["trigger"]?.GetValue<bool>() == false).ToArray() ?? [];
            if (solids.Length > 0 && solids.All(c => c["enabled"]?.GetValue<bool>() == false || c["active"]?.GetValue<bool>() == false)) return false;
        }
        var food = Food(id);
        return food.IngredientIds.Length > 0 && food.IsRuined;
    }).OrderBy(Id).ToArray();

    private void RequireNoActiveRuinedFood()
    {
        var ruined = ActiveRuinedFood(); if (ruined.Length == 0) return;
        Log("plannerNativeRuinedFood", new JsonObject { ["frame"] = Frame,
            ["entities"] = new JsonArray(ruined.Select(e => (JsonNode?)new JsonObject { ["id"] = Id(e), ["observedOrdinal"] = e["observedOrdinal"]?.DeepClone(),
                ["name"] = e["name"]?.DeepClone(), ["composition"] = e["composition"]?.DeepClone(),
                ["ingredientIds"] = JsonSerializer.SerializeToNode(Food(Id(e)).IngredientIds) }).ToArray()) });
        throw new InvalidOperationException("Native active food is ruined (Burnt or Overmixed): " + string.Join(", ", ruined.Select(e => Id(e) + " " + e["name"])));
    }
}
