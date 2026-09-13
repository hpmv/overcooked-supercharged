using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Recorded V12 geometry; subsequent food transitions are explicitly offline fixtures.</summary>
    public static int NearReadyDoughSelfTest(JsonObject at892)
    {
        int count = 0;
        void Check(bool value, string detail) { if (!value) throw new InvalidOperationException("Near-ready dough regression: " + detail); count++; }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline near-ready test attempted game I/O."), null)
            {
                response = at892.DeepClone().AsObject(), options = new(BakeryLookahead: 12),
                recipes = new[] { 158500, 125780, 224216, 228996, 47642, 472326, 257844, 130976, 158500, 125780, 130976, 228996 }.Select(CarnivalRecipes.GetRecipe).ToArray()
            };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline near-ready action attempted I/O."), null);
            p.CaptureFryerHomes(); p.CapturePotHomes();
            p.bowlHomes[6] = 18; p.bowlHomes[3] = 14;
            p.bowlAssignments[6] = 3; p.bowlFlavors[6] = CarnivalRecipes.Chocolate.Id;
            p.bowlAssignments[3] = 7; p.bowlFlavors[3] = CarnivalRecipes.Raspberry.Id;
            return p;
        }
        void CompleteNext(CarnivalPlanner p)
        {
            var w = p.workers[0]!; var action = w.Actions.Dequeue(); action["player"] = 0;
            w.Active = p.runner.CreateAction(action); w.Active.IsDone = true;
            p.ObserveNearReadyDoughTransfers(); p.CompleteWork();
        }
        var actual = Make();
        Check(actual.Frame == 892 && N(actual.Entity(6)?["mixingProgress"]) is > 10.20 and < 10.21 && actual.EmptyFood(5) && actual.EmptyFood(8),
            "the captured complete chocolate bowl is still natively mixing while both original fryers are empty");
        Check(actual.AssignedDoughIngredients(6, 3) && !AssignedDoughReady(actual.Food(6), actual.recipes[3]),
            "full ingredients do not fabricate native Mixed completion");
        int[] initialReservations = actual.reserved.ToArray(); int[] plateIds = actual.AvailablePlates().Select(Id).ToArray();
        string nativeBeforeAdmission = actual.state.ToJsonString();
        Check(actual.PrepareDonut(0, 6), "selected native heat bowl admits bounded wait then direct fryer transfer");
        Check(actual.state.ToJsonString() == nativeBeforeAdmission, "admission changes no observed native food, timing, score or physical state");
        var work = actual.workers[0]!; var plan = actual.nearReadyDoughTransfers[work];
        Check(work.Name == "wait-then-transfer-mixed-dough" && work.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "navigate", "mix", "take", "combine", "place" }),
            "native mix wait precedes pickup, direct combine and original mixer restoration");
        Check(plan.Bowl == 6 && plan.Mixer == 18 && work.OwnedResources.SetEquals(new[] { 6, 18, plan.Basket, plan.Fryer }) && actual.bakeryLeases.Count == 0,
            "exact bowl/mixer/fryer resources stay in one ordinary work without allocating a parking counter");
        Check(plateIds.All(id => actual.Free(id)) && actual.mealPlates.Count == 0 && actual.plating.Count == 0 && actual.basketAssignments.Count == 0,
            "near-ready transfer allocates no plates and claims no cooked output before native transfer");
        Check(!actual.TryTransferNearReadyDough(3, 6, 3, 18) && !actual.Start(3, "conflicting-fryer", [actual.A("take", plan.Basket)], [plan.Basket]),
            "another chef cannot acquire the same mixing bowl or empty target basket");
        var waitSpec = work.Actions.ElementAt(1).DeepClone().AsObject(); waitSpec["player"] = 0;
        var wait = actual.runner.CreateAction(waitSpec);
        var input = actual.runner.Tick(wait, actual.response);
        Check(!wait.IsDone && new[] { "pickup", "use", "dash" }.All(k => !B(input[k])) && N(input["x"]) == 0 && N(input["y"]) == 0,
            "the preparation action waits with neutral input while the exact native bowl remains Unmixed");
        actual.ObserveNearReadyDoughTransfers(); CompleteNext(actual); // navigate
        actual.Entity(6)!["composition"]!["state"] = "Mixed"; actual.Entity(6)!["composition"]!["progress"] = 1.001;
        actual.Entity(6)!["mixingProgress"] = 12.012;
        actual.runner.Tick(wait, actual.response);
        Check(wait.IsDone, "only observed native Mixed state completes the neutral wait");
        CompleteNext(actual); // mix
        Check(plan.ObservedMixed && !plan.PickedUp, "observer distinguishes native mixing from later pickup");
        actual.Entity(18)!["attachedEntityId"] = 0; actual.chefs[0]["heldEntityId"] = 6; CompleteNext(actual);
        Check(plan.PickedUp && !plan.Transferred && actual.reserved.Contains(plan.Fryer), "whole-bowl pickup retains exclusive empty fryer and original mixer");
        actual.Entity(18)!["attachedEntityId"] = 99999;
        bool homeRejected = false;
        try { actual.ObserveNearReadyDoughTransfers(); } catch (InvalidOperationException) { homeRejected = true; }
        Check(homeRejected, "a stray attachment cannot occupy the reserved original mixer while its bowl is carried");
        actual.Entity(18)!["attachedEntityId"] = 0;
        var mixed = actual.Entity(6)!["composition"]!.DeepClone();
        actual.Entity(plan.Basket)!["composition"] = new JsonObject { ["type"] = "CookedCompositeAssembledNode", ["state"] = "Raw", ["progress"] = .001,
            ["cookingStepId"] = CarnivalRecipes.DeepFryerCookingStepId, ["children"] = new JsonArray(mixed) };
        actual.Entity(plan.Basket)!["cookingProgress"] = .01;
        actual.Entity(6)!["composition"]!["children"] = new JsonArray(); actual.Entity(6)!["composition"]!["state"] = "Unmixed";
        actual.Entity(6)!["composition"]!["progress"] = 0; actual.Entity(6)!["mixingProgress"] = 0;
        actual.Entity(6)!["contents"] = new JsonArray(); actual.Entity(6)!["ingredientIds"] = new JsonArray();
        CompleteNext(actual);
        Check(plan.Transferred && actual.basketAssignments.Count == 0, "native dough transfer is proved before final original-bowl return releases ownership");
        actual.Entity(18)!["attachedEntityId"] = 6; actual.chefs[0]["heldEntityId"] = 0; CompleteNext(actual);
        Check(actual.workers[0] is null && actual.nearReadyDoughTransfers.Count == 0 && actual.reserved.SetEquals(initialReservations),
            "exact original empty-bowl return closes the complete work and releases its resources once");
        Check(actual.basketAssignments[plan.Basket] == 3 && !actual.bowlAssignments.ContainsKey(6) && !actual.bowlFlavors.ContainsKey(6) &&
            actual.plating.Count == 0 && actual.mealPlates.Count == 0 && I(actual.state["score"]) == 0,
            "native fryer receives the addressed recipe while scoring and plate allocation remain untouched");

        foreach (string defect in new[] { "partial", "far-future", "occupied-fryers", "fryer-lease", "bowl-lease", "clock", "early", "slow", "missing-ordinal", "wrong-original-home", "occupied-chef" })
        {
            var bad = Make();
            switch (defect)
            {
                case "partial": bad.Entity(6)!["composition"]!["children"]!.AsArray().RemoveAt(2); break;
                case "far-future": bad.options = bad.options with { Lookahead = 2 }; break;
                case "occupied-fryers": foreach (int b in new[] { 5, 8 }) bad.Entity(b)!["composition"] = bad.Entity(6)!["composition"]!.DeepClone(); break;
                case "fryer-lease": bad.reserved.UnionWith(new[] { 15, 20 }); break;
                case "bowl-lease": bad.reserved.Add(18); break;
                case "clock": bad.Entity(6)!["mixingTime"] = 10; break;
                case "early": bad.Entity(6)!["mixingProgress"] = 8; break;
                case "slow": bad.chefs[0]["runSpeed"] = .05; break;
                case "missing-ordinal": bad.Entity(6)!.Remove("observedOrdinal"); break;
                case "wrong-original-home": bad.Entity(18)!["attachedEntityId"] = 3; break;
                case "occupied-chef": bad.Start(0, "existing", [bad.A("navigate", 32)], [32]); break;
            }
            Check(!bad.TryTransferNearReadyDough(0, 6, 3, 18), "guard rejects " + defect + " without taking ownership");
        }
        foreach (string defect in new[] { "identity", "assignment", "resource", "work", "food", "premature-pickup", "empty-without-transfer", "deadline" })
        {
            var bad = Make(); Check(bad.TryTransferNearReadyDough(0, 6, 3, 18), "mutation fixture admits original work: " + defect);
            var owned = bad.nearReadyDoughTransfers[bad.workers[0]!];
            switch (defect)
            {
                case "identity": bad.Entity(6)!["observedOrdinal"] = 99999; break;
                case "assignment": bad.bowlAssignments[6] = 7; break;
                case "resource": bad.reserved.Remove(owned.Fryer); break;
                case "work": bad.workers[0] = null; break;
                case "food": bad.Entity(6)!["composition"]!["children"]!.AsArray().RemoveAt(2); break;
                case "premature-pickup": bad.Entity(18)!["attachedEntityId"] = 0; bad.chefs[0]["heldEntityId"] = 6; break;
                case "empty-without-transfer": bad.Entity(6)!["composition"]!["children"] = new JsonArray(); break;
                case "deadline": bad.Entity(6)!["mixingProgress"] = 21; break;
            }
            bool rejected = false;
            try { bad.ObserveNearReadyDoughTransfers(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, "active observer rejects " + defect);
        }
        var ordinary = Make();
        ordinary.Entity(3)!["composition"]!["children"]!.AsArray().Add(ordinary.Entity(6)!["composition"]!["children"]![2]!.DeepClone());
        // Call the new helper only for the selected bowl: ordinary dispatch is
        // still free to do useful work rather than wait on arbitrary futures.
        Check(!ordinary.TryTransferNearReadyDough(0, 3, 7, 14), "another addressed bowl cannot borrow the selected chocolate recipe");
        return count;
    }
}
