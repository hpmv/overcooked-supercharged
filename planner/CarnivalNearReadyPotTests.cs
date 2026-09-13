using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int NearReadyPotSelfTest(JsonObject at789, JsonObject at843)
    {
        int count = 0; void Check(bool yes, string message) { if (!yes) throw new InvalidOperationException("Near-ready pot regression: " + message); count++; }
        JsonObject NativeFood(FoodObservation f) => new() { ["type"] = f.Kind switch { FoodNodeKind.Ingredient => "IngredientAssembledNode", FoodNodeKind.Cooked => "CookedCompositeAssembledNode", _ => "CompositeAssembledNode" },
            ["id"] = f.IngredientId, ["state"] = f.Kind == FoodNodeKind.Cooked ? f.Preparation.ToString() : null, ["cookingStepId"] = f.CookingStepId,
            ["children"] = new JsonArray(f.Children.Select(c => (JsonNode?)NativeFood(c)).ToArray()) };
        CarnivalPlanner Make(JsonObject? snapshot = null)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline near-ready pot test attempted game I/O."), null)
            { response = (snapshot ?? at789).DeepClone().AsObject(), options = new(NearReadyPotHarvest: true, BufferChoppedBuns: true, StagedResourceRelease: true),
                recipes = new[] { 158500, 125780, 224216, 228996, 47642, 472326, 257844, 130976 }.Select(CarnivalRecipes.GetRecipe).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline near-ready action attempted I/O."), null);
            p.CapturePotHomes(); p.bufferedBun = 143; p.bunBufferCounter = 32;
            // Native ordinary Work resources in the captured event stream.
            p.Start(1, "captured-Raspberry-supply", [p.A("take", 67), p.A("place", 24)], [67, 24]);
            if (p.Frame == 789) p.Start(0, "captured-flour-egg-handoff", [p.A("place", 3)], [145, 48, 3]);
            else p.reserved.UnionWith(new[] { 7, 19, 33 }); // other pot's observed persistent rescue lease
            return p;
        }
        void CompleteNext(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var action = work.Actions.Dequeue(); action["player"] = player;
            work.Active = p.runner.CreateAction(action); work.Active.IsDone = true;
            p.ObserveNearReadyPotHarvests(); p.CompleteWork(); p.ReleaseCompletedUnplatedResources();
        }
        void Cook(CarnivalPlanner p, int pot)
        {
            p.Entity(pot)!["composition"] = NativeFood(CarnivalRecipes.GetRecipe(296560).ExpectedFood.Children.Single(n => n.CookingStepId == CarnivalRecipes.PotCookingStepId));
            p.Entity(pot)!["cookingProgress"] = 12.166;
        }
        void Consume(CarnivalPlanner p, NearReadyPot plan)
        {
            p.Entity(plan.Pot)!["composition"] = NativeFood(new(FoodNodeKind.Cooked, FoodPreparation.Raw, 0, "", CarnivalRecipes.PotCookingStepId, 0, []));
            p.Entity(plan.Pot)!["ingredientIds"] = new JsonArray(); p.Entity(plan.Pot)!["contents"] = new JsonArray(); p.Entity(plan.Pot)!["cookingProgress"] = 0;
            p.Entity(plan.Food)!["composition"] = NativeFood(CarnivalRecipes.GetRecipe(296560).ExpectedFood);
        }
        var actual = Make();
        Check(actual.Frame == 789 && actual.NativeNearReadyPot(7) && actual.IsLooseChoppedBun(143) && actual.Attached(32) == 143 &&
            !actual.DirectPotHarvestHasTime(3, 7), "actual11-second pot and exact prepared buffer exist while original Cooked-only gate rejects");
        var unchanged = actual.state.ToJsonString(); var other = actual.workers[0];
        Check(actual.TryRescuePot(3, 7) && actual.potRescues.Count == 0 && actual.nearReadyPots.Count == 1, "exact first native admission selects waiting harvest without an offheat lease");
        var work = actual.workers[3]!; var plan = actual.nearReadyPots[work];
        Check(actual.state.ToJsonString() == unchanged && ReferenceEquals(other, actual.workers[0]), "admission changes no native state or other central work");
        Check(plan.Index == 0 && plan.Food == 143 && plan.Source == 32 && plan.Output == 32 && plan.Home == 19,
            "original FIFO recipe, buffered bun identity and selected output are preserved");
        Check(work.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "navigate", "cook", "combine", "place" }) &&
            work.OwnedResources.SetEquals(new[] { 32, 143, 7, 19 }), "five native actions retain exact pot/home/source/food ownership");
        Check(N(plan.Evidence["estimatedSecondsToConsumption"]) is > 3.80 and < 3.81 && N(plan.Evidence["guardRemainingSeconds"]) is > 11.99 and < 12.01,
            "actual full walking and native wait budget has over eight seconds of guarded margin");
        actual.ObserveNearReadyPotHarvests(); Check(!plan.CookedObserved && !plan.Harvested, "a raw11-second sausage is never classified as already cooked");
        actual.Entity(32)!["attachedEntityId"] = 0; actual.chefs[3]["heldEntityId"] = 143;
        CompleteNext(actual, 3); CompleteNext(actual, 3);
        Check(plan.PickedUp && actual.Attached(19) == 7 && actual.reserved.IsSupersetOf(new[] { 7, 19, 32, 143 }), "bun pickup/navigation retains whole pot on heat and its exact leases");
        Cook(actual, 7); CompleteNext(actual, 3);
        Check(plan.CookedObserved && !plan.Harvested, "native Cooked observation precedes combine");
        Consume(actual, plan); CompleteNext(actual, 3);
        Check(plan.Harvested && plan.PotReleased && plan.HomeReleased && work.OwnedResources.SetEquals(new[] { 32, 143 }),
            "completed native combine releases both empty pot and home through staged ownership");
        Check(actual.Start(2, "successor-native-pot-refill", [actual.A("place", 7)], [7, 19]), "successor producer can reserve the freshly emptied original pot and home");
        actual.Entity(7)!["composition"] = KitchenModel.SnapshotState(at789)["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == 7)["composition"]!.DeepClone();
        actual.Entity(7)!["cookingProgress"] = .02;
        actual.ObserveNearReadyPotHarvests(); Check(plan.Harvested, "new owner's legal pot refill is not mistaken for the old consumer failing");
        actual.Entity(32)!["attachedEntityId"] = 143; actual.chefs[3]["heldEntityId"] = 0; CompleteNext(actual, 3);
        Check(actual.nearReadyPots.Count == 0 && actual.mealFoods.GetValueOrDefault(0) == 143 && actual.bufferedBun == 0 &&
            actual.workers[3] is null && actual.reserved.IsSupersetOf(new[] { 7, 19 }) && actual.workers[2] is not null,
            "native selected output completes original recipe bookkeeping without releasing the successor's leases");
        var second = Make(at843);
        Check(second.TryRescuePot(0, 2) && second.nearReadyPots.Values.Single().Home == 17 &&
            N(second.nearReadyPots.Values.Single().Evidence["estimatedSecondsToConsumption"]) is > 3.79 and < 3.80 && second.reserved.IsSupersetOf(new[] { 7, 19, 33 }),
            "second captured opportunity preserves the other pot's existing persistent rescue resources");
        var board = Make(); board.options = board.options with { BufferChoppedBuns = false }; board.bufferedBun = board.bunBufferCounter = 0;
        Check(board.TryRescuePot(3, 7), "observed prepared bun149 on native chopping board is an alternative source");
        var boardPlan = board.nearReadyPots.Values.Single(); Check(boardPlan.Source == 23 && boardPlan.Food == 149 && boardPlan.Output != 23, "ordinary source uses the original actual empty-counter selection");
        board.Entity(23)!["attachedEntityId"] = 0; board.chefs[3]["heldEntityId"] = 149; CompleteNext(board, 3);
        Check(boardPlan.SourceReleased && board.Free(23) && board.reserved.Contains(boardPlan.Output), "completed bun pickup releases its source while retaining the different selected output");

        foreach (string defect in new[] { "off", "early", "already-cooked", "missing-bun", "raw-bun", "source-reserved", "home-reserved", "pot-replaced", "source-inactive", "bad-output", "no-path", "busy-chef" })
        {
            var p = Make();
            switch (defect)
            {
                case "off": p.options = p.options with { NearReadyPotHarvest = false }; break;
                case "early": p.Entity(7)!["cookingProgress"] = 10.9; break;
                case "already-cooked": Cook(p, 7); break;
                case "missing-bun": p.Entity(32)!["attachedEntityId"] = 0; break;
                case "raw-bun": p.Entity(143)!["components"] = new JsonArray("WorkableItem", "ServerWorkableItem");
                    p.Entity(143)!["composition"] = null; p.Entity(143)!["contents"] = new JsonArray(); p.Entity(143)!["ingredientIds"] = new JsonArray();
                    p.Entity(143)!["name"] = "HotdogBun(Clone)"; p.Entity(143)!["workProgress"] = 0; break;
                case "source-reserved": p.reserved.Add(32); break;
                case "home-reserved": p.reserved.Add(19); break;
                case "pot-replaced": p.Entity(7)!["observedOrdinal"] = 999; break;
                case "source-inactive": p.Entity(32)!["active"] = false; break;
                case "bad-output": p.Entity(32)!["components"]!.AsArray().Add("CookingStation"); break;
                case "no-path": p.chefs[3]["position"]!["x"] = 999; p.Refresh(); break;
                case "busy-chef": p.Start(3, "ongoing-job", [p.A("take", 56)], [56]); break;
            }
            var before = p.reserved.ToHashSet();
            Check(p.PlanNearReadyPot(3, 0, 32, 143, 7, 0, 32) is null && p.nearReadyPots.Count == 0 && p.reserved.SetEquals(before), "invalid direct admission leaves ordinary safety behavior untouched: " + defect);
        }
        var off = Make(); off.options = off.options with { NearReadyPotHarvest = false };
        Check(off.TryRescuePot(3, 7) && off.potRescues.ContainsKey(7) && off.nearReadyPots.Count == 0 && off.workers[3]!.Actions.Count == 4,
            "default-off retains the original cook-wait, whole-pot parking branch");
        foreach (string defect in new[] { "identity", "recipe", "resource", "pot-moved", "premature-empty", "wrong-food", "deadline", "late-phase", "lost-source", "missing-progress", "negative-progress" })
        {
            var p = Make(); Check(p.TryRescuePot(3, 7), "prepare native observer mutation: " + defect); var selected = p.nearReadyPots.Values.Single();
            switch (defect)
            {
                case "identity": p.Entity(143)!["observedOrdinal"] = 999; break;
                case "recipe": p.assembling.Remove(0); break;
                case "resource": p.reserved.Remove(19); break;
                case "pot-moved": p.Entity(19)!["attachedEntityId"] = 0; break;
                case "premature-empty": Consume(p, selected); break;
                case "wrong-food": p.Entity(143)!["composition"] = NativeFood(CarnivalRecipes.GetRecipe(224216).ExpectedFood); break;
                case "deadline": p.Entity(7)!["cookingProgress"] = 23; break;
                case "late-phase": p.state["gameplayFrame"] = 1510; break;
                case "lost-source": p.Entity(32)!["attachedEntityId"] = 0; break;
                case "missing-progress": p.Entity(7)!.Remove("cookingProgress"); break;
                case "negative-progress": p.Entity(7)!["cookingProgress"] = -1; break;
            }
            bool rejected = false; try { p.ObserveNearReadyPotHarvests(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, "active exact native guard rejects: " + defect);
        }
        return count;
    }
}
