using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual native washer mechanism snapshots with explicit synthetic planner ownership and negative mutations; no game calls.</summary>
    public static int WasherPlatingSelfTest(JsonObject shared1143, JsonObject clean1159, JsonObject assembled1194,
        JsonObject staged1202, JsonObject recovered1211, JsonObject finished1299)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Washer plating regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline washer fixture attempted I/O."), null)
            { response = shared1143.DeepClone().AsObject(), options = new(WasherSidePlating: true, ServiceSideHead: true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 96).ToArray() };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline washer fixture attempted I/O."), null);
            // The native mechanism probe used output49. Pin the same legal
            // choice with a synthetic unrelated reservation on the earlier slot.
            p.reserved.Add(47); p.mealFoods[0] = 125;
            return p;
        }
        void Apply(CarnivalPlanner p, JsonObject snapshot)
        { p.response = snapshot.DeepClone().AsObject(); p.Refresh(); p.AdvanceWasherPlating(); }
        void Complete(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var last = work.Actions.Last().DeepClone().AsObject(); last["player"] = player;
            work.Actions.Clear(); work.Active = p.runner.CreateAction(last); work.Active.IsDone = true;
            p.CompleteWork();
        }
        var disabled = Make(); disabled.options = disabled.options with { WasherSidePlating = false };
        Check(!disabled.TryBeginWasherPlating() && disabled.workers.All(w => w is null), "default-off leaves ordinary dispatch unchanged");
        var actual = Make(); string before = actual.response.ToJsonString();
        Check(actual.TryBeginWasherPlating(), "actual prepared125 on shared50 and exact clean10 on44 admit the native mechanism");
        var lease = actual.washerPlating!;
        Check(lease.Plate == 10 && lease.PlateOrdinal == 9 && lease.Food == 125 && lease.FoodOrdinal == 124 && lease.Shared == 50 && lease.CleanPass == 44 && lease.Output == 49,
            "exact native plate, food and handoff identities are preserved");
        Check(before == actual.response.ToJsonString(), "planner admission does not mutate native observations");
        Check(lease.Resources.All(actual.reserved.Contains) && !lease.Resources.Contains(46) && !lease.Resources.Contains(75), "dirty handoff and sink remain outside shared lease");
        Check(actual.workers[0] is null && actual.workers[3] is null, "already staged food consumes no center job");
        actual.CompleteWork();
        Check(ReferenceEquals(actual.workers[1], lease.WasherWaiter), "ordinary completion leaves the explicitly owned neutral washer wait intact");
        Check(!actual.Start(0, "conflicting-clean-pass", [], [44]) && !actual.Start(3, "conflicting-food", [], [125]), "ordinary dispatch cannot steal clean plate or food lease");
        actual.AdvanceWasherPlating();
        Check(lease.Phase == WasherPlatePhase.WasherPlating && actual.workers[1]!.Actions.Count == 3 &&
            StationIs(actual.workers[1]!.Actions.ElementAt(0), 44) && StationIs(actual.workers[1]!.Actions.ElementAt(1), 50) &&
            StationIs(actual.workers[1]!.Actions.ElementAt(2), 50), "washer emits only native take44, assemble50, place50");
        Apply(actual, clean1159);
        Check(actual.Held(1) == 10 && actual.EmptyFood(10), "actual washer pickup retains original clean plate10");
        Apply(actual, assembled1194);
        Check(actual.Held(1) == 10 && actual.Entity(125) is null && CarnivalRecipes.MatchRecipe(actual.Entity(10), 296560).ReadyToDeliver,
            "actual native assembly consumes original125 and retains the same prepared plate10");
        Apply(actual, staged1202); Complete(actual, 1);
        Check(lease.Phase == WasherPlatePhase.ReadyForCenter && actual.workers[1] is null && actual.Attached(50) == 10,
            "washer is released immediately after native same-plate staging");
        var unrelatedWasher = new Work("ordinary-next-wash", [], [75], null); actual.workers[1] = unrelatedWasher; actual.reserved.Add(75);
        actual.heatSafetyBlocked.UnionWith([0, 3]);
        Check(!actual.TryRecoverWasherPlate() && actual.workers[0] is null && actual.workers[3] is null,
            "blocked native heat safety keeps elective plate recovery from occupying either center");
        actual.heatSafetyBlocked.Clear();
        Check(actual.TryRecoverWasherPlate() && lease.RecoveryPlayer == 0, "measured nearby empty center can recover while washer has unrelated work");
        Apply(actual, recovered1211);
        Check(actual.Held(0) == 10 && ReferenceEquals(actual.workers[1], unrelatedWasher), "actual center pickup preserves plate and independent washer job");
        Apply(actual, finished1299); Complete(actual, 0);
        Check(actual.washerPlating is null && actual.mealPlates.GetValueOrDefault(0) == 10 && !actual.mealFoods.ContainsKey(0) &&
            actual.Attached(49) == 10 && !actual.assembling.Contains(0) && !actual.plating.Contains(0), "native final output closes exact recipe ownership");
        Check(lease.Resources.All(id => !actual.reserved.Contains(id)) && actual.reserved.Contains(75) && actual.reserved.Contains(47), "only this lease's resources release");
        foreach (string mutation in new[] { "busy-washer", "washer-held", "sink-work", "dirty-handoff", "clean-output", "occupied50", "plate-lease", "food-lease", "source-ordinal", "sauce", "wrong-food", "young-future-last-plate", "hot-deadline" })
        {
            var p = Make();
            switch (mutation)
            {
                case "busy-washer": p.workers[1] = new Work("washing", [], [], null); break;
                case "washer-held": p.chefs[1]["heldEntityId"] = 11; break;
                case "sink-work": p.Entity(75)!["plateCount"] = 1; break;
                case "dirty-handoff": p.Entity(46)!["attachedEntityId"] = 11; break;
                case "clean-output": p.Entity(p.Single("drying"))!["plateCount"] = 1; break;
                case "occupied50": p.mealFoods[0] = 125; p.Entity(50)!["attachedEntityId"] = 11; break;
                case "plate-lease": p.reserved.Add(10); break;
                case "food-lease": p.reserved.Add(125); break;
                case "source-ordinal": p.Entity(50)!.Remove("observedOrdinal"); break;
                case "sauce": p.recipes[0] = CarnivalRecipes.GetRecipe(158500); break;
                case "wrong-food": p.Entity(125)!["composition"] = new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Frankfurter.Id }; break;
                case "young-future-last-plate":
                    p.mealFoods.Remove(0); p.mealFoods[1] = 125;
                    foreach (var plate in p.entities.Values.Where(IsPlate).Where(e => Id(e) != 10)) p.reserved.Add(Id(plate));
                    break;
                case "hot-deadline":
                    p.Entity(7)!["cookingProgress"] = 15;
                    p.Entity(7)!["contents"] = new JsonArray(new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Frankfurter.Id });
                    p.Entity(7)!["composition"] = p.Entity(125)!["composition"]!.DeepClone(); break;
            }
            Check(!p.TryBeginWasherPlating() && p.washerPlating is null, "admission preserves " + mutation);
        }
        foreach (string mutation in new[] { "plate-incarnation", "food-incarnation", "shared-incarnation", "output-lease", "waiter-owner", "plate-moved", "food-lost", "recipe" })
        {
            var p = Make(); p.TryBeginWasherPlating();
            switch (mutation)
            {
                case "plate-incarnation": p.Entity(10)!["observedOrdinal"] = 999; break;
                case "food-incarnation": p.Entity(125)!["observedOrdinal"] = 999; break;
                case "shared-incarnation": p.Entity(50)!["observedOrdinal"] = 999; break;
                case "output-lease": p.reserved.Remove(p.washerPlating!.Output); break;
                case "waiter-owner": p.workers[1] = new Work("replacement", [], [], null); break;
                case "plate-moved": p.Entity(44)!["attachedEntityId"] = 0; p.Entity(46)!["attachedEntityId"] = 10; break;
                case "food-lost": p.Entity(125)!["active"] = false; p.Refresh(); break;
                case "recipe": p.recipes[0] = CarnivalRecipes.GetRecipe(472326); break;
            }
            Reject(p.AdvanceWasherPlating, "active lease rejects " + mutation);
        }
        var timeout = Make(); timeout.TryBeginWasherPlating(); timeout.state["gameplayFrame"] = timeout.Frame + 361;
        bool timedOut = false; try { timeout.AdvanceWasherPlating(); } catch (TimeoutException) { timedOut = true; }
        Check(timedOut, "phase wait has a fixed bound");
        // Explicit synthetic starting location tests the additional legal
        // center staging phase, while keeping the actual native cooked food.
        var moving = Make(); int stagingCounter = moving.Counter(19.2, -16.8);
        moving.Entity(50)!["attachedEntityId"] = 0; moving.Entity(stagingCounter)!["attachedEntityId"] = 125;
        var oldFoodPosition = KitchenModel.Position(moving.Entity(125)!["position"]);
        var newFoodPosition = KitchenModel.Position(moving.Entity(stagingCounter)!["position"]);
        moving.Entity(125)!["position"] = moving.Entity(stagingCounter)!["position"]!.DeepClone();
        foreach (var collider in moving.Entity(125)!["colliders"]!.AsArray().OfType<JsonObject>())
        {
            collider["center"]!["x"] = N(collider["center"]!["x"]) + newFoodPosition.X - oldFoodPosition.X;
            collider["center"]!["z"] = N(collider["center"]!["z"]) + newFoodPosition.Z - oldFoodPosition.Z;
        }
        // In the native1143 observation P0 stands at the shared handoff and
        // blocks another cook's approach. This separate staging fixture moves
        // that otherwise idle chef to a measured-clear central floor position;
        // runtime admission must still find every full-clearance route.
        moving.chefs[0]["position"] = new JsonObject { ["x"] = 22.2, ["y"] = .05, ["z"] = -18.2 };
        moving.Refresh();
        Check(moving.TryBeginWasherPlating() && moving.washerPlating!.Phase == WasherPlatePhase.StagingFood,
            "ready food on another free center counter admits bounded native staging");
        var stagingLease = moving.washerPlating!; int stager = stagingLease.Stager;
        Check(ReferenceEquals(moving.workers[1], stagingLease.WasherWaiter) && moving.workers[stager]!.Actions.Count == 2,
            "washer waits in an owned empty slot while only stager takes and places food");
        moving.Entity(stagingCounter)!["attachedEntityId"] = 0; moving.Entity(50)!["attachedEntityId"] = 125;
        moving.Entity(125)!["position"] = moving.Entity(50)!["position"]!.DeepClone(); moving.Refresh(); Complete(moving, stager);
        Check(moving.workers[stager] is null && stagingLease.Phase == WasherPlatePhase.ReadyForWasher && moving.reserved.Contains(50),
            "stager is released at exact shared-food proof while the handoff stays reserved");
        return count;
    }
}
