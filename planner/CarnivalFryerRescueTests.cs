using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Recorded V10 Cooked-fryer geometry plus explicit synthetic lifecycle observations; no game I/O.</summary>
    public static int FryerRescueSelfTest(JsonObject cookedSnapshot)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Fryer rescue regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, message); }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline fryer fixture attempted I/O."), null)
                { response = cookedSnapshot.DeepClone().AsObject(), options = new(), recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(130976), 20).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline fryer fixture attempted I/O."), null);
            p.CaptureFryerHomes(); p.basketAssignments[8] = 7;
            // The recorded other central chef was already parking future dough.
            p.workers[3] = new Work("existing-future-bakery-job", [], [6], null); p.reserved.Add(6);
            return p;
        }
        void FinishWork(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var action = work.Actions.LastOrDefault()?.DeepClone().AsObject() ?? new JsonObject { ["type"] = "navigate" };
            action["player"] = player; var handle = p.runner.CreateAction(action); handle.IsDone = true;
            work.Active = handle; work.Actions.Clear(); p.CompleteWork();
        }
        FryerRescue Park(CarnivalPlanner p)
        {
            if (!p.TryRescueFryer(0)) throw new InvalidOperationException("Captured native fryer rescue was not admitted.");
            var lease = p.fryerRescues[8];
            p.Entity(lease.Home)!["attachedEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = 8;
            FinishWork(p, 0); return lease;
        }
        void Prove(CarnivalPlanner p)
        {
            for (int i = 0; i < 2; i++) { p.state["gameplayFrame"] = p.Frame + 1; p.state["timer"] = N(p.state["timer"]) - 1d / 60; p.AdvanceFryerRescues(); }
        }
        var p = Make(); var initialScore = I(p.state["score"]); var initialTimer = N(p.state["timer"]);
        Check(p.Frame == 2336 && p.Held(0) == 0 && p.Food(8).Preparation == FoodPreparation.Cooked && p.AvailablePlates().Length == 0,
            "recorded first idle opportunity has a native Cooked Raspberry basket and no allocatable plate");
        Check(p.TryRescueFryer(0), "existing native basket is rescued before dirty relay and speculative preparation");
        var lease = p.fryerRescues[8];
        Check(lease.Index == 7 && lease.Home == p.AttachmentParent(8) && lease.Owner == 0 && p.reserved.IsSupersetOf(lease.Resources),
            "rescue reserves exact observed basket, home stove, ordinary counter, recipe and chef");
        Check(p.workers[0]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "navigate", "cook", "take", "place" }) &&
            p.workers[0]!.Actions.All(a => !B(a["dash"]) && !B(a["shortDash"])), "rescue waits for native Cooked and uses ordinary whole-basket transfers");
        Check(p.workers[0]!.OwnedResources.Count == 0 && p.fryerRescues.Count == 1 && !p.Free(8), "lease retains sole vessel ownership across child job completion");
        Check(p.EmptyAttachment(lease.Counter) && !NativeCookingStation(p.Entity(lease.Counter)), "offheat destination is an empty ordinary measured counter");
        p.Entity(lease.Home)!["attachedEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = 8;
        FinishWork(p, 0);
        Check(lease.Phase == FryerPhase.Verifying && p.workers[0] is null && p.IsFryerParticipant(0) && !p.EarlyCookAvailable(0) && !p.BakeryCookAvailable(0) && !p.IdleTrafficHelper(0),
            "empty-handed chef remains exclusively reserved for advancing-frame offheat proof");
        p.AdvanceFryerRescues();
        Check(lease.StableSamples == 0 && !p.FryerAvailableForPlating(8, 7), "repeated paused inspections do not prove or release the parked basket");
        Prove(p);
        Check(lease.Phase == FryerPhase.Parked && lease.Owner == -1 && lease.StableSamples == 2 && p.FryerAvailableForPlating(8, 7) && !p.FryerAvailableForPlating(8, 10),
            "two advancing unchanged samples release the chef but preserve exact-order vessel ownership");
        Check(I(p.state["score"]) == initialScore && N(p.state["timer"]) < initialTimer && N(p.Entity(8)?["cookingProgress"]) == lease.Progress,
            "fixture leaves score and food untouched while native elapsed-time evidence advances");
        p.delivered = 7;
        int plate = 1000, plateCounter = p.EmptyCenterCounter(new Point2(20.4, -16.8));
        Check(plateCounter != 0 && plateCounter != lease.Counter, "plate fixture occupies a separate unreserved counter");
        p.entities[plate] = new JsonObject { ["id"] = plate, ["name"] = "equipment_plate_01", ["components"] = new JsonArray("Plate", "ServerPlate"),
            ["position"] = p.Entity(plateCounter)!["position"]!.DeepClone(), ["composition"] = new JsonObject { ["type"] = "CompositeAssembledNode", ["children"] = new JsonArray() } };
        p.Entity(plateCounter)!["attachedEntityId"] = plate;
        Check(p.TryAssemble(0, exactIndex: 7) && lease.Phase == FryerPhase.Plating && lease.Owner == 0,
            "ordinary FIFO-safe assembly can claim its exact parked native cooked basket");
        Check(!p.workers[0]!.OwnedResources.Contains(8) && p.reserved.IsSupersetOf(lease.Resources), "plate job never double-owns or prematurely frees rescue resources");
        int output = I(p.workers[0]!.Actions.Last()["station"]);
        p.Entity(plate)!["composition"] = new JsonObject { ["type"] = "CompositeAssembledNode", ["children"] = new JsonArray(p.Entity(8)!["composition"]!.DeepClone()) };
        p.Entity(plateCounter)!["attachedEntityId"] = 0; p.Entity(output)!["attachedEntityId"] = plate;
        p.Entity(8)!["composition"] = new JsonObject { ["type"] = "CookedCompositeAssembledNode", ["state"] = "Raw", ["children"] = new JsonArray(), ["cookingStepId"] = 17160, ["progress"] = 0 };
        p.Entity(8)!["contents"] = new JsonArray(); p.Entity(8)!["cookingProgress"] = 0;
        FinishWork(p, 0);
        Check(lease.Phase == FryerPhase.Restoring && p.mealPlates[7] == plate && p.workers[0]!.Name.StartsWith("restore-empty-fryer-", StringComparison.Ordinal),
            "validated plated meal starts ordinary restoration using the same chef");
        Check(p.workers[0]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }) && !p.Free(8),
            "empty basket cannot accept future dough until restored to its original heat");
        p.Entity(lease.Counter)!["attachedEntityId"] = 0; p.Entity(lease.Home)!["attachedEntityId"] = 8;
        FinishWork(p, 0);
        Check(p.fryerRescues.Count == 0 && p.Free(lease.Resources) && p.reserved.Contains(6) && p.workers[3]!.Name == "existing-future-bakery-job",
            "restoration releases only this basket/home/counter and preserves unrelated work");
        var changed = Make(); var changedLease = Park(changed); changed.Entity(8)!["cookingProgress"] = changedLease.Progress + .016;
        Reject(changed.AdvanceFryerRescues, "continued cooking off heat rejects the supposed parked state");
        var identity = Make(); Park(identity); identity.Entity(8)!["observedOrdinal"] = 9000;
        Reject(identity.AdvanceFryerRescues, "native basket ID reuse does not substitute another object");
        var reassigned = Make(); Park(reassigned); reassigned.basketAssignments[8] = 10;
        Reject(reassigned.AdvanceFryerRescues, "a repeated-flavor order cannot steal the parked basket assignment");
        var deadline = Make(); deadline.TryRescueFryer(0); deadline.Entity(8)!["cookingProgress"] = 19;
        bool failed = false; try { deadline.AdvanceFryerRescues(); } catch (TimeoutException) { failed = true; }
        Check(failed, "failed offheat removal terminates before the native20-second burn deadline");
        var ordinaryDeadline = Make(); ordinaryDeadline.workers[0] = new Work("ordinary-donut-plating", [], [8], null); ordinaryDeadline.reserved.Add(8);
        ordinaryDeadline.Entity(8)!["cookingProgress"] = 19;
        failed = false; try { ordinaryDeadline.AdvanceFryerRescues(); } catch (TimeoutException) { failed = true; }
        Check(failed && ordinaryDeadline.fryerRescues.Count == 0, "ordinary plate work cannot bypass the independent native on-heat deadline");
        var late = Make(); late.Entity(8)!["cookingProgress"] = 15;
        Check(!late.DirectFryerPlateHasTime(0, 8), "late cooking cannot start an unbounded plate-pickup detour");
        var occupied = Make(); occupied.workers[0] = new Work("existing-head-job", [], [], null);
        Check(!occupied.TryRescueFryer(0), "rescue cannot steal a working chef");
        var leased = Make(); leased.earlyOnion = new EarlyOnionLease(4, 4, 16, 61, 3, 0, leased.Frame);
        Check(!leased.TryRescueFryer(0), "native onion-rescue ownership is mutually exclusive");
        var noCounter = Make(); foreach (var counter in noCounter.Stations("counter")) noCounter.reserved.Add(counter.EntityId);
        Check(!noCounter.TryRescueFryer(0) && noCounter.fryerRescues.Count == 0, "absent exclusive offheat storage never creates a partial lease");
        var wrongDuration = Make(); wrongDuration.Entity(8)!["cookingTime"] = 12;
        Check(!wrongDuration.TryRescueFryer(0), "native recipe timing mismatch is not silently converted to the measured fryer timing");
        var wrongFood = Make(); wrongFood.recipes[7] = CarnivalRecipes.GetRecipe(228996);
        Check(!wrongFood.TryRescueFryer(0), "wrong native flavor cannot enter a rescue under another order identity");
        return count;
    }
}
