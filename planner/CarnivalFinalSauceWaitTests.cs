using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual V16 final-apply observations and explicitly reconstructed unchanged serial Work. No game I/O or counterfactual service motion.</summary>
    public static int FinalSauceWaitSelfTest(JsonObject at11099, JsonObject at11104, JsonObject at11105, JsonObject at11184, JsonObject at11185, JsonObject at12407)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Final-sauce wait regression: " + message); checks++; }
        CarnivalPlanner Make(JsonObject? snapshot = null)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline final-sauce fixture attempted I/O."), null) {
                response = (snapshot ?? at11099).DeepClone().AsObject(), options = new(WaitForImminentHead: true, WaitForFinalSauceHead: true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 64).ToArray() };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline final-sauce fixture attempted I/O."), null);
            bool first = p.Frame == 11099; int index = first ? 16 : 18, player = first ? 3 : 0, plate = first ? 379 : 408, food = first ? 317 : 369, source = first ? 33 : 43;
            int recipe = first ? 257844 : 47642, condiment = first ? CarnivalRecipes.Ketchup.Id : CarnivalRecipes.Mustard.Id;
            p.recipes[index] = CarnivalRecipes.GetRecipe(recipe); p.plating.Add(index); p.assembling.Add(index);
            var apply = p.A("apply", 72); apply["player"] = player; apply["expectedIngredientId"] = condiment;
            var place = p.A("place", 52); place["player"] = player;
            var work = new Work($"assemble-meal-{index + 1}-" + p.recipes[index].Name, [place], [plate, food, source, 52, 72, 79], () => {
                if (!CarnivalRecipes.MatchRecipe(p.Entity(plate), recipe).ReadyToDeliver) throw new InvalidOperationException("Original native recipe callback rejected plate.");
                p.mealPlates[index] = plate; p.plating.Remove(index); p.assembling.Remove(index);
            });
            work.Active = p.runner.CreateAction(apply); p.workers[player] = work; p.reserved.UnionWith(work.OwnedResources);
            return p;
        }
        void Apply(CarnivalPlanner p, JsonObject observation) { p.response = observation.DeepClone().AsObject(); p.Refresh(); }
        var p = Make(); var work = p.workers[3]!; var active = work.Active; int[] locks = work.OwnedResources.Order().ToArray(); string native = p.response.ToJsonString();
        Check(p.delivered == 16 && p.Held(3) == 379 && !CarnivalRecipes.MatchRecipe(p.Entity(379), 257844).ReadyToDeliver &&
            CarnivalRecipes.MatchRecipe(p.Entity(379), 472326).ReadyToDeliver && I(p.chefs[3]["placementTargetId"]) == 72,
            "actual departure has exact native plated onion base missing only Ketchup, already targeting dispenser72");
        Check(p.TryWaitForImminentHead() && p.finalSauceHead is not null, "existing final apply and output fit120-frame wait at recorded11099");
        var wait = p.imminentHead!; var selected = p.finalSauceHead!;
        Check(wait.Start == 11099 && wait.Plate == 379 && wait.Ordinal == 378 && wait.Output == 52 && selected.BaseRecipe == 472326 && selected.Condiment == 158482,
            "wait captures exact native plate/output/base/final-condiment identity and original start");
        Check(p.ValidateImminentHead(wait, out var evidence, out _) && Math.Abs(N(evidence["estimatedRemainingFramesWithTransferAllowance"]) - 90.7773228842) < .01,
            "actual clear path plus12 final-apply and12 placement frames fits the fixed bound");
        Check(native == p.response.ToJsonString() && ReferenceEquals(work, p.workers[3]) && ReferenceEquals(active, work.Active) &&
            work.OwnedResources.Order().SequenceEqual(locks) && p.workers[2] is null,
            "extension changes no native observation, active action, queue, resource ownership or service Work");
        Apply(p, at11104);
        bool continued = p.ValidateImminentHead(wait, out var pendingEvidence, out string continuationReason);
        Check(continued && !selected.NativeApplied && wait.Start == 11099,
            "native11104 still holds the same partial plate and never resets wait start: " + continuationReason);
        Check(!Navigation.ToStation(p.model, p.Position(3), p.Station(52)!, p.TrafficObstacles(3)).Success &&
            B(pendingEvidence["originalAdmissionEvidenceRetained"]) && N(pendingEvidence["pendingApplicationElapsedFrames"]) == 5 &&
            N(pendingEvidence["applyAllowanceFrames"]) == 7 && N(pendingEvidence["remainingWaitFrames"]) == 115 &&
            N(pendingEvidence["pendingApplicationDisplacement"]) < .111 && Math.Abs(N(pendingEvidence["pendingApplicationDisplacementLimit"]) - .24) < .00001,
            "native contact keeps immutable initial path evidence, remaining7 application frames and original115 total frames; no fabricated new path");
        Apply(p, at11105);
        Check(p.ValidateImminentHead(wait, out var applied, out _) && selected.NativeApplied && B(applied["nativeFinalRecipeObserved"]),
            "native11105 exact recipe is accepted during application-before-action-callback interval");
        work.Active!.IsDone = true; p.CompleteWork();
        Check(work.Active is null && work.Actions.Count == 1 && p.ValidateImminentHead(wait, out _, out _) && ReferenceEquals(p.imminentHead, wait),
            "ordinary completed-apply boundary transitions to existing final-place logic under the same wait and leases");
        work.Active = p.runner.CreateAction(work.Actions.Dequeue());
        Apply(p, at11184);
        Check(p.Held(3) == 0 && p.Attached(52) == 379 && p.ValidateImminentHead(wait, out _, out _),
            "native11184 same plate attachment precedes ordinary completion without invalidating wait");
        Apply(p, at11185); work.Active.IsDone = true; p.CompleteWork(); p.ObserveImminentHeadArrival();
        Check(p.mealPlates.GetValueOrDefault(16) == 379 && p.imminentHead is null && p.finalSauceHead is null && p.HasAccessibleHead("lower-right"),
            "native completed recipe callback closes wait and exposes the plate to ordinary collection");
        Check(p.imminentAttempted.Contains(16) && p.workers[3] is null && p.reserved.Count == 0,
            "visit attempt remains recorded while original callback releases only its own resources");

        var late = Make(at12407);
        Check(I(late.chefs[0]["placementTargetId"]) == 43 && !late.TryWaitForImminentHead() && late.imminentHead is null,
            "actual197-frame12407 case is rejected because it has not reached the native dispenser");
        var toSauce = Navigation.ToStation(late.model, late.Position(0), late.Station(72)!, late.TrafficObstacles(0));
        var afterSauce = Navigation.ToStation(late.model, toSauce.Points[^1], late.Station(52)!, late.TrafficObstacles(0));
        Check(toSauce.Success && afterSauce.Success && (toSauce.Length + afterSauce.Length) / 6 * 60 + 24 > 120,
            "negative case's full remaining route also exceeds the120-frame budget");

        foreach (string mutation in new[] { "disabled", "original-wait-disabled", "native-target", "held-other", "wrong-switch", "wrong-dispenser-food",
            "missing-onion", "duplicate-bun", "raw-sausage", "plate-identity", "plate-inactive", "output-identity", "output-inactive", "output-occupied",
            "source-identity", "button-identity", "plate-lease", "source-lease", "output-lease", "duplicate-owner", "extra-switch", "extra-ingredient",
            "wrong-apply", "wrong-output", "wrong-job", "wrong-head", "owner-disabled", "owner-suppressed", "slow-route", "not-serial-plating" })
        {
            var q = Make();
            switch (mutation)
            {
                case "disabled": q.options = q.options with { WaitForFinalSauceHead = false }; break;
                case "original-wait-disabled": q.options = q.options with { WaitForImminentHead = false }; break;
                case "native-target": q.chefs[3]["placementTargetId"] = 33; break;
                case "held-other": q.chefs[3]["heldEntityId"] = 13; break;
                case "wrong-switch": q.Entity(72)!["switchIndex"] = 0; break;
                case "wrong-dispenser-food": q.Entity(72)!["composition"]!["id"] = 17094; break;
                case "missing-onion": q.Entity(379)!["composition"]!["children"]!.AsArray().RemoveAt(1); break;
                case "duplicate-bun": q.Entity(379)!["composition"]!["children"]!.AsArray().Add(q.Entity(379)!["composition"]!["children"]![2]!.DeepClone()); break;
                case "raw-sausage": q.Entity(379)!["composition"]!["children"]![0]!["state"] = "Raw"; break;
                case "plate-identity": q.Entity(379)!["observedOrdinal"] = -1; break;
                case "plate-inactive": q.Entity(379)!["active"] = false; break;
                case "output-identity": q.Entity(52)!.Remove("observedOrdinal"); break;
                case "output-inactive": q.Entity(52)!["active"] = false; break;
                case "output-occupied": q.Entity(52)!["attachedEntityId"] = 13; break;
                case "source-identity": q.Entity(72)!.Remove("observedOrdinal"); break;
                case "button-identity": q.Entity(79)!.Remove("observedOrdinal"); break;
                case "plate-lease": q.reserved.Remove(379); break;
                case "source-lease": q.workers[3]!.OwnedResources.Remove(72); break;
                case "output-lease": q.reserved.Remove(52); break;
                case "duplicate-owner": q.workers[0] = new Work("other", [], [379], null); break;
                case "extra-switch": q.workers[3]!.Actions.Enqueue(q.SauceSwitch(CarnivalRecipes.Mustard)); break;
                case "extra-ingredient": q.workers[3]!.Actions.Enqueue(q.A("take", 56)); break;
                case "wrong-apply": q.workers[3]!.Active!.Action.Spec["expectedIngredientId"] = 17094; break;
                case "wrong-output": q.workers[3]!.Actions.Peek()["station"] = "48"; break;
                case "wrong-job": q.workers[3] = new Work("another-meal", [], locks, null); break;
                case "wrong-head": q.delivered = 15; break;
                case "owner-disabled": q.chefs[3]["controlsEnabled"] = false; break;
                case "owner-suppressed": q.chefs[3]["inputSuppressed"] = true; break;
                case "slow-route": q.chefs[3]["runSpeed"] = 2; break;
                case "not-serial-plating": q.plating.Remove(16); break;
            }
            Check(!q.TryWaitForImminentHead() && q.imminentHead is null && q.finalSauceHead is null && q.workers[2] is null,
                "unsafe/unsupported final-sauce admission rejects without changing existing jobs: " + mutation);
        }
        foreach (string mutation in new[] { "recipe-regressed", "source-changed", "owner-replaced", "output-stolen", "bound", "new-action" })
        {
            var q = Make(); Check(q.TryWaitForImminentHead(), "lifecycle fixture admits before mutation: " + mutation);
            if (mutation == "recipe-regressed")
            {
                Apply(q, at11105); Check(q.ValidateImminentHead(q.imminentHead!, out _, out _), "exact native recipe observed before regression");
                q.Entity(379)!["composition"] = at11099["state"]!["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == 379)["composition"]!.DeepClone();
                Check(!q.ValidateImminentHead(q.imminentHead!, out _, out _), "once native final sauce is observed, its loss cannot revert to pending");
                continue;
            }
            if (mutation == "source-changed") q.Entity(72)!["observedOrdinal"] = 9000;
            if (mutation == "owner-replaced") q.workers[3] = new Work("replacement", [], locks, null);
            if (mutation == "output-stolen") q.Entity(52)!["attachedEntityId"] = 13;
            if (mutation == "bound") q.state["gameplayFrame"] = 11219;
            if (mutation == "new-action") q.workers[3]!.Actions.Enqueue(q.A("take", 56));
            Check(!q.TryWaitForImminentHead() && q.imminentHead is null && q.finalSauceHead is null && !q.TryWaitForImminentHead(),
                "failed wait cancels once without reacquiring this visit/head: " + mutation);
        }
        var reset = Make(); reset.TryWaitForImminentHead(); reset.ResetImminentHeadVisit();
        Check(reset.imminentHead is null && reset.finalSauceHead is null && reset.imminentAttempted.Count == 0, "ordinary completed portal return clears the visit lifecycle");
        foreach (string mutation in new[] { "displaced", "cached-overspeed", "actual-overspeed", "dash", "impact", "application-expired",
            "changed-target", "changed-work", "walking-speed-changed", "fixed-clock-changed" })
        {
            var q = Make(); q.TryWaitForImminentHead(); var original = q.imminentHead!; var ownerWork = q.workers[3];
            Apply(q, at11104);
            switch (mutation)
            {
                case "displaced": q.chefs[3]["position"]!["x"] = q.finalSauceHead!.AdmissionPose.X + .24001; q.chefs[3]["position"]!["z"] = q.finalSauceHead.AdmissionPose.Z; break;
                case "cached-overspeed": q.chefs[3]["lastVelocity"]!["x"] = 6.01; break;
                case "actual-overspeed": q.chefs[3]["velocity"]!["x"] = 6.01; break;
                case "dash": q.chefs[3]["dashTimer"] = .1; break;
                case "impact": q.chefs[3]["impactVelocity"]!["x"] = .1; break;
                case "application-expired": q.state["gameplayFrame"] = 11111; break;
                case "changed-target": q.chefs[3]["placementTargetId"] = 33; break;
                case "changed-work": q.workers[3] = new Work(ownerWork!.Name, [], locks, null); break;
                case "walking-speed-changed": q.chefs[3]["surfaceSpeedMultiplier"] = .9; break;
                case "fixed-clock-changed": q.state["fixedDeltaTime"] = .01; break;
            }
            // The captured service chef departed in the original experiment;
            // inspect only the central owner's native evidence here, without
            // replacing that native motion with a hypothetical waiting pose.
            Check(!q.ValidateImminentHead(original, out _, out _) && original.Start == 11099 &&
                original.Work.OwnedResources.Order().SequenceEqual(locks),
                "pending-application contact exception rejects bounded mutation without changing owner inputs/leases/deadline: " + mutation);
        }
        return checks;
    }
}
