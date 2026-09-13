using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int HeatArbitrationSelfTest(JsonObject at2384, JsonObject at2489, JsonObject at2604, JsonObject initial)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Native heat arbitration: " + message); checks++; }
        CarnivalPlanner Make(JsonObject snapshot, bool busy = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline heat fixture attempted game I/O."), null)
            { response = snapshot.DeepClone().AsObject(), options = new(BakeryLookahead: 12), recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(130976), 20).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline heat fixture attempted game I/O."), null);
            p.CapturePotHomes();
            // Homes were captured at initialization, before the native trace
            // parked basket5/8. Reconstruct those immutable initial links.
            var start = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline fixture attempted I/O."), null)
                { response = initial.DeepClone().AsObject() };
            if (start.response["state"] is null) start.response = new JsonObject { ["state"] = start.response };
            start.Refresh(); start.CaptureFryerHomes();
            foreach (var pair in start.fryerHomes) p.fryerHomes.Add(pair.Key, pair.Value);
            foreach (var bowl in p.Stations("bowl")) p.bowlHomes[bowl.EntityId] = p.AttachmentParent(bowl.EntityId);
            p.bowlAssignments[6] = 10; p.bowlFlavors[6] = CarnivalRecipes.Raspberry.Id; p.basketAssignments[8] = 7;
            p.bakeryLeases[6] = new BakeryLease(6, 10, 18, 33, I(p.Entity(6)?["observedOrdinal"]), I(p.Entity(18)?["observedOrdinal"]), I(p.Entity(33)?["observedOrdinal"]), 1504);
            p.reserved.UnionWith(new[] { 18, 33, 32, 45, 61, 153, 165 });
            if (busy)
            {
                var first = new JsonObject { ["type"] = "switch-condiment", ["index"] = 1, ["player"] = 0 };
                p.Start(0, "captured-ongoing-head-assembly", [p.A("take", 10), p.A("assemble", 40), p.A("apply", 72), p.A("place", 47)], [10, 161, 40, 47, 72, 79]);
                p.workers[0]!.Active = p.runner.CreateAction(first);
            }
            return p;
        }
        void Offheat(CarnivalPlanner p, params int[] vessels)
        {
            // Explicit synthetic observations remove only the processing
            // attachment for isolated scheduler cases; no runtime calls occur.
            foreach (int vessel in vessels) { int parent = p.AttachmentParent(vessel); if (parent != 0) p.Entity(parent)!["attachedEntityId"] = 0; }
        }
        var early = Make(at2384); var original = early.workers[0]!; var active = original.Active;
        string queue = JsonSerializer.Serialize(original.Actions); int[] locks = original.OwnedResources.Order().ToArray();
        var pending = early.NativeHeatObligations().OrderBy(h => h.Remaining).ToArray();
        Check(pending[0].Vessel == 7 && Math.Abs(pending[0].Remaining - 4.4334) < .01, "actual2384 pot has the earliest4.433-second guarded deadline");
        early.DispatchNativeHeatSafety();
        Check(early.workers[3]?.Name == "rescue-cooked-pot-7" && early.potRescues.ContainsKey(7), "actual2384 free chef starts exact pot rescue before dirty relay");
        Check(ReferenceEquals(early.workers[0], original) && ReferenceEquals(original.Active, active) && queue == JsonSerializer.Serialize(original.Actions) && locks.SequenceEqual(original.OwnedResources.Order()),
            "ongoing head action, queue and ownership are unchanged");
        Check(!early.fryerRescues.ContainsKey(8) && early.EarlyOnionLeases().Count() == 0, "later fryer and unheated onion do not steal the only available rescue chef");
        var late = Make(at2489); var ordered = late.NativeHeatObligations().OrderBy(h => h.Remaining).ToArray();
        Check(ordered[0].Vessel == 7 && ordered.Single(h => h.Vessel == 8).Remaining > 9, "actual2489 pot still outranks the much later fryer deadline");
        late.DispatchNativeHeatSafety();
        Check(late.workers[3] is null && late.HeatSafetyBlocks(3) && !late.fryerRescues.ContainsKey(8), "late pot approach plus conservative2-second allowance blocks electives instead of choosing a lower-priority fryer");
        Check(!late.IdleTrafficHelper(3), "a blocked safety chef cannot be silently borrowed for elective shared chopping");
        var busy = Make(at2384); busy.Start(3, "another-existing-native-action", [busy.A("take", 24)], [24]);
        var before = busy.workers[3]; busy.DispatchNativeHeatSafety();
        Check(ReferenceEquals(before, busy.workers[3]) && busy.potRescues.Count == 0, "two busy chefs never have an action replaced or privately preempted");
        var reserved = Make(at2384); reserved.reserved.Add(7); reserved.DispatchNativeHeatSafety();
        Check(reserved.HeatSafetyBlocks(3) && reserved.workers[3] is null && reserved.reserved.Contains(7), "unexplained reserved safety vessel blocks fallthrough without releasing its lock");

        var partial = Make(at2604); Offheat(partial, 2, 7, 8);
        var obligation = partial.NativeHeatObligations().Single(h => h.Vessel == 6);
        Check(obligation.Kind == "partial-mixer-flavor" && obligation.Target == 24 && partial.Food(6).IngredientIds.Length == 2,
            "actual native Mixed flour+egg is included with exact waiting chopped raspberry target");
        var direct = Navigation.ToStation(partial.model, partial.Position(3), partial.Station(24)!, partial.TrafficObstacles(3));
        var complete = partial.HeatInteractionPath(3, obligation);
        Check(complete.Success && complete.Length > direct.Length + .5, "feasibility includes the subsequent board-to-bowl walk to the actual heat-changing placement");
        partial.DispatchNativeHeatSafety();
        Check(partial.workers[3]?.Name == "heat-deadline-add-exact-flavor-11" && partial.workers[3]!.Resources.Order().SequenceEqual(new[] { 6, 24, 173 }.Order()),
            "partial mixer dispatch reserves only its exact bowl, board and prepared ingredient");
        Check(partial.bakeryLeases[6].Phase == BakeryPhase.Supplying && !partial.workers[3]!.Actions.Any(a => a["type"]?.ToString() == "mix"),
            "partial dough is neither declared complete nor parked through the full-recipe path");
        var placedFlavor = partial.Entity(173)!["composition"]!.DeepClone();
        partial.Entity(6)!["composition"]!["children"]![0]!["children"]!.AsArray().Add(placedFlavor);
        partial.Entity(6)!["mixingProgress"] = 8.5; partial.Entity(24)!["attachedEntityId"] = 0;
        partial.workers[3]!.Complete!();
        Check(partial.AssignedDoughIngredients(6, 10), "completion checks native accepted ingredient multiset without imposing a fake progress reset");

        var raw = Make(at2604); Offheat(raw, 2, 7, 8); raw.Entity(6)!["mixingProgress"] = 9;
        var rawFlavor = raw.Entity(173)!; rawFlavor["composition"] = null; rawFlavor["contents"] = new JsonArray();
        rawFlavor["components"] = new JsonArray("WorkableItem", "ServerWorkableItem"); rawFlavor["name"] = "Raspberry"; rawFlavor["workProgress"] = .4;
        Check(raw.NativeHeatObligations().Single(h => h.Vessel == 6).Kind == "partial-mixer-chop", "partially worked exact raw flavor becomes a legal heat prerequisite");
        raw.DispatchNativeHeatSafety();
        Check(raw.workers[3]?.Name == "heat-deadline-chop-exact-flavor-11" && raw.workers[3]!.Actions.Single()["type"]?.ToString() == "chop",
            "PantryChopping=false does not deadlock raw flavor behind the elective block");
        Check(raw.workers[3]!.OwnedResources.Contains(6), "the partial bowl remains assigned to the prerequisite job while native mixing continues");

        var parked = Make(at2604); Offheat(parked, 2, 7, 6);
        Check(parked.NativeHeatObligations().Count() == 0, "actual parked fryer8 and synthetically detached other vessels create no heating obligation");
        parked.DispatchNativeHeatSafety(); Check(!parked.HeatSafetyBlocks(3), "parked food cannot block ordinary chef work");
        var defer = Make(at2604); defer.AdvanceEarlyOnion(false); defer.AdvanceBakeryLookahead(false);
        Check(defer.workers[3] is null, "phase observation alone cannot reintroduce family-first new assignment");

        var stage = Make(initial, false); stage.reserved.Clear(); stage.bakeryLeases.Clear(); stage.bowlAssignments.Clear(); stage.bowlFlavors.Clear();
        stage.bowlAssignments[6] = 10; stage.bowlFlavors[6] = CarnivalRecipes.Raspberry.Id;
        stage.bakeryLeases[6] = new BakeryLease(6, 10, 18, 33, I(stage.Entity(6)?["observedOrdinal"]), I(stage.Entity(18)?["observedOrdinal"]), I(stage.Entity(33)?["observedOrdinal"]), stage.Frame);
        stage.reserved.UnionWith(stage.bakeryLeases[6].Resources);
        Check(stage.WaitForSpeculativeFlavor(6, CarnivalRecipes.Raspberry.Id) && stage.workers[1]?.Name == "supply-Raspberry" && stage.EmptyFood(6),
            "empty speculative bowl stages flavor through ordinary supply before starting flour+egg clocks");

        JsonObject NativeFood(FoodObservation food) => new()
        {
            ["type"] = food.Kind == FoodNodeKind.Ingredient ? "IngredientAssembledNode" : "CookedCompositeAssembledNode",
            ["id"] = food.IngredientId, ["state"] = food.Preparation.ToString(), ["cookingStepId"] = food.CookingStepId,
            ["children"] = new JsonArray(food.Children.Select(c => (JsonNode?)NativeFood(c)).ToArray())
        };
        var ordinary = Make(initial, false); ordinary.reserved.Clear(); ordinary.bakeryLeases.Clear(); ordinary.bowlAssignments.Clear();
        ordinary.options = new(); ordinary.recipes = [CarnivalRecipes.GetRecipe(472326)];
        // This older initialization capture predates movement telemetry. Use
        // the actual later fixture's native speed profile for this synthetic
        // ordinary-pan scenario, leaving its physical geometry unchanged.
        foreach (int chef in new[] { 0, 3 })
        {
            ordinary.chefs[chef]["runSpeed"] = early.chefs[chef]["runSpeed"]?.DeepClone();
            ordinary.chefs[chef]["surfaceSpeedMultiplier"] = early.chefs[chef]["surfaceSpeedMultiplier"]?.DeepClone();
        }
        var onion = ordinary.recipes[0].ExpectedFood.Children.Single(f => f.CookingStepId == CarnivalRecipes.PanCookingStepId);
        ordinary.Entity(4)!["composition"] = NativeFood(onion); ordinary.Entity(4)!["cookingProgress"] = 13;
        Check(!ordinary.AddUnplatedOnions(0, 4) && ordinary.mealFoods.Count == 0, "ordinary pan without a prepared plain base cannot use the immediate onion-harvest path");
        ordinary.DispatchNativeHeatSafety();
        var adopted = ordinary.EarlyOnionLeases().Single();
        Check(adopted.Pan == 4 && adopted.Index == 0 && adopted.Phase == EarlyOnionPhase.Approach && adopted.Owner >= 0,
            "default-option ordinary pan uses the proven exact offheat lease instead of blocking its absent base prerequisite");
        Check(adopted.Resources.All(ordinary.reserved.Contains) && ordinary.workers[adopted.Owner]?.Name == "early-onion-approach-rescue-1",
            "mandatory ordinary-pan rescue reserves the exact home and parking counter through its native lifecycle");
        Check(!ordinary.NativeHeatObligations().Any(h => h.Vessel == 4), "the claimed ordinary pan is not assigned to both center chefs");
        return checks;
    }
}
