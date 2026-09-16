using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual V7 native observations plus offline overlapping-owner regressions. Does not call the game.</summary>
    public static int StagedReleaseSelfTest(JsonObject start958, JsonObject picked1038, JsonObject combined1071, JsonObject finished1127)
    {
        int count = 0;
        void Check(bool condition, string description)
        { if (!condition) throw new InvalidOperationException("Staged release regression: " + description); count++; }
        void Observe(CarnivalPlanner p, JsonObject snapshot)
        {
            p.response = snapshot.DeepClone().AsObject();
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh();
        }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline staged-release fixture attempted I/O."), null)
            { options = new(StagedResourceRelease: enabled), recipes = [CarnivalRecipes.GetRecipe(158500), CarnivalRecipes.GetRecipe(125780)] };
            Observe(p, start958);
            p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline staged-release action attempted I/O."), null);
            // Recorded chef3 unplated-hotdog-1 still owns counter32 at958.
            // Reconstruct that selection constraint, then isolate the tested
            // job's resource lifecycle from unrelated planner work.
            p.assembling.Add(0); p.reserved.Add(32);
            if (!p.BuildUnplatedHotdog(0)) throw new InvalidOperationException("Recorded V7 base assembly did not reconstruct.");
            p.reserved.Remove(32);
            return p;
        }
        void CompleteNextAction(CarnivalPlanner p)
        {
            var work = p.workers[0]!;
            var spec = work.Actions.Dequeue(); spec["player"] = 0;
            work.Active = p.runner.CreateAction(spec); work.Active.IsDone = true;
            p.CompleteWork();
        }
        CarnivalPlanner AfterPickup(bool enabled = true)
        {
            var p = Make(enabled); Observe(p, picked1038); CompleteNextAction(p); return p;
        }
        CarnivalPlanner AfterCombine()
        {
            var p = AfterPickup(); p.ReleaseCompletedUnplatedResources();
            Observe(p, combined1071); CompleteNextAction(p); return p;
        }

        var p = Make(); var work = p.workers[0]!;
        Check(p.Frame == 958 && work.Name == "unplated-hotdog-2" && work.Resources.Order().SequenceEqual(new[] { 2, 23, 33, 153 }),
            "actual start snapshot reconstructs the measured second-hotdog job and exact four native resources (observed " +
            p.Frame + ", " + work.Name + ", resources " + string.Join(',', work.Resources) + ")");
        Check(work.OwnedResources.SetEquals(work.Resources) && work.ReleasableTransfer is { Food: 153, Board: 23, Pot: 2, PotHome: 17 },
            "new job owns every initial resource and records exact native food, board, pot and original stove");
        p.ReleaseCompletedUnplatedResources();
        Check(work.Resources.All(p.reserved.Contains), "no resources release before their native consumers finish");
        Observe(p, picked1038);
        Check(p.Held(0) == 153 && p.Attached(23) == 0 && !p.EmptyFood(2),
            "actual frame1038 shows the exact bun held and cleared source while sausage is still in the pot");
        p.ReleaseCompletedUnplatedResources();
        Check(!p.Free(23), "native attachment evidence alone cannot bypass unfinished pickup action completion");
        CompleteNextAction(p); p.ReleaseCompletedUnplatedResources();
        Check(p.Free(23) && !work.OwnedResources.Contains(23) && new[] { 153, 2, 33 }.All(p.reserved.Contains),
            "completed observed pickup releases only the no-longer-used source board");
        Check(work.Resources.Contains(23) && work.Actions.Count == 2 && p.workers[0] == work,
            "historical job resources and original continuation remain intact after early board release");
        Check(p.Start(2, "refill-released-bun-board", [p.A("place", 23)], [23]),
            "pantry can acquire the actual cleared board before old hotdog job finishes");
        var refillBoard = p.workers[2]!;
        p.ReleaseCompletedUnplatedResources();
        Check(p.reserved.Contains(23) && refillBoard.OwnedResources.Contains(23) && !work.OwnedResources.Contains(23),
            "repeat observation never removes the replacement board owner's reservation");
        Observe(p, combined1071);
        Check(p.Held(0) == 153 && p.EmptyFood(2) && N(p.Entity(2)?["cookingProgress"]) == 0 &&
            CarnivalRecipes.MatchRecipe(p.Entity(153), 296560, false).ReadyToDeliver,
            "actual frame1071 proves the native cooked sausage moved into exact held bun and reset the same pot");
        p.ReleaseCompletedUnplatedResources();
        Check(!p.Free(2), "empty pot observation still waits for its native combine action completion");
        CompleteNextAction(p); p.ReleaseCompletedUnplatedResources();
        Check(p.Free(2) && work.OwnedResources.SetEquals(new[] { 153, 33 }),
            "completed native combination releases pot while held food and final destination stay exclusively owned");
        Check(p.Start(3, "refill-released-pot", [p.A("place", 2)], [2]),
            "a refill can acquire the exact empty pot before the original meal is parked");
        var refillPot = p.workers[3]!;
        Check(!p.Start(1, "conflicting-refill", [p.A("place", 2)], [2]) && !p.Start(1, "conflicting-board", [p.A("place", 23)], [23]),
            "successor reservations still reject competing new jobs");
        Observe(p, finished1127); CompleteNextAction(p);
        Check(p.workers[0] is null && p.mealFoods.GetValueOrDefault(1) == 153 && p.Attached(33) == 153,
            "actual final native snapshot completes the unchanged hotdog validation and meal bookkeeping");
        Check(p.reserved.SetEquals(new[] { 2, 23 }) && work.OwnedResources.Count == 0 &&
            refillBoard.OwnedResources.SetEquals(new[] { 23 }) && refillPot.OwnedResources.SetEquals(new[] { 2 }),
            "old completion releases only its remaining food/destination and cannot erase either overlapping successor lease");
        p.ReleaseRemainingWorkResources(work);
        Check(p.reserved.SetEquals(new[] { 2, 23 }), "releasing an already-finished old ownership set is idempotent even with new owners");
        p.ReleaseRemainingWorkResources(refillBoard);
        Check(p.reserved.SetEquals(new[] { 2 }), "successor board completion releases only its own resource");
        p.ReleaseRemainingWorkResources(refillPot);
        Check(p.reserved.Count == 0, "all resource lifecycles end without a leaked reservation");

        var baseline = AfterPickup(false); baseline.ReleaseCompletedUnplatedResources();
        Check(baseline.workers[0]!.ReleasableTransfer is null && baseline.reserved.SetEquals(new[] { 2, 23, 33, 153 }),
            "default-off preserves original full-job ownership even after the actual cleared-board observation");
        var returnedBoard = AfterPickup(); returnedBoard.workers[0]!.Actions.Enqueue(returnedBoard.A("place", 23));
        returnedBoard.ReleaseCompletedUnplatedResources();
        Check(!returnedBoard.Free(23), "buffer or fallback source reused as final destination remains reserved");
        var activeBoard = AfterPickup(); activeBoard.workers[0]!.Active = activeBoard.runner.CreateAction(activeBoard.A("navigate", 23));
        activeBoard.ReleaseCompletedUnplatedResources();
        Check(!activeBoard.Free(23), "an active action still referencing source prevents early release");
        var occupiedBoard = AfterPickup(); occupiedBoard.Entity(23)!["attachedEntityId"] = 998;
        occupiedBoard.ReleaseCompletedUnplatedResources();
        Check(!occupiedBoard.Free(23), "source must be natively empty, not merely have lost the old bun ID");
        var unknownBoard = AfterPickup(); unknownBoard.Entity(23)!.Remove("attachedEntityId");
        unknownBoard.ReleaseCompletedUnplatedResources();
        Check(!unknownBoard.Free(23), "missing attachment telemetry cannot establish a cleared source");
        var wrongHeld = AfterPickup(); wrongHeld.chefs[0]["heldEntityId"] = 998;
        wrongHeld.ReleaseCompletedUnplatedResources();
        Check(!wrongHeld.Free(23), "another held item cannot prove the original native pickup");
        foreach (int changed in new[] { 23, 153 })
        {
            var reused = AfterPickup(); reused.Entity(changed)!["observedOrdinal"] = I(reused.Entity(changed)?["observedOrdinal"]) + 1;
            reused.ReleaseCompletedUnplatedResources();
            Check(!reused.Free(23), "source release refuses native ID reuse for observed entity " + changed);
        }
        foreach (string defect in new[] { "contents", "progress", "missing-progress", "missing-composition", "changed-id", "moved-pot", "wrong-food", "future-action", "held-pot" })
        {
            var guarded = AfterCombine();
            switch (defect)
            {
                case "contents":
                    guarded.Entity(2)!["composition"] = ((start958["state"] ?? start958)["entities"] as JsonArray)!.OfType<JsonObject>()
                        .Single(e => I(e["id"]) == 2)["composition"]!.DeepClone(); break;
                case "progress": guarded.Entity(2)!["cookingProgress"] = .01; break;
                case "missing-progress": guarded.Entity(2)!.Remove("cookingProgress"); break;
                case "missing-composition": guarded.Entity(2)!.Remove("composition"); break;
                case "changed-id": guarded.Entity(2)!["observedOrdinal"] = 9999; break;
                case "moved-pot": guarded.Entity(17)!["attachedEntityId"] = 0; break;
                case "wrong-food": guarded.Entity(153)!["composition"] = ((picked1038["state"] ?? picked1038)["entities"] as JsonArray)!.OfType<JsonObject>()
                    .Single(e => I(e["id"]) == 153)["composition"]!.DeepClone(); break;
                case "future-action": guarded.workers[0]!.Actions.Enqueue(guarded.A("combine", 2)); break;
                case "held-pot": guarded.chefs[3]["heldEntityId"] = 2; break;
            }
            guarded.ReleaseCompletedUnplatedResources();
            Check(!guarded.Free(2) && guarded.workers[0]!.OwnedResources.Contains(2), "pot native completion guard retains ownership for " + defect);
        }
        return count;
    }
}
