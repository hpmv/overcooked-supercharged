using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured V11 deadline geometry plus explicitly synthetic lifecycle transitions; no game calls.</summary>
    public static int PotRescueSelfTest(JsonObject boundary2384)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Pot rescue regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
        void Timeout(Action action, string message)
        { bool rejected = false; try { action(); } catch (TimeoutException) { rejected = true; } Check(rejected, message); }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline pot fixture attempted I/O."), null)
            { response = boundary2384.DeepClone().AsObject(), options = new(StagedResourceRelease: true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 20).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline pot fixture attempted I/O."), null);
            p.CapturePotHomes(); return p;
        }
        void FinishWork(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var spec = work.Actions.Last().DeepClone().AsObject(); spec["player"] = player;
            work.Active = p.runner.CreateAction(spec); work.Active.IsDone = true; work.Actions.Clear(); p.CompleteWork();
        }
        void CompleteNext(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var spec = work.Actions.Dequeue(); spec["player"] = player;
            work.Active = p.runner.CreateAction(spec); work.Active.IsDone = true; p.CompleteWork();
        }
        PotRescue Park(CarnivalPlanner p)
        {
            if (!p.TryRescuePot(3, 7)) throw new InvalidOperationException("Captured native pot candidate was not admitted.");
            var lease = p.potRescues[7]; p.Entity(lease.Home)!["attachedEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = 7;
            FinishWork(p, 3); return lease;
        }
        void Prove(CarnivalPlanner p)
        {
            for (int i = 0; i < 2; i++) { p.state["gameplayFrame"] = p.Frame + 1; p.state["timer"] = N(p.state["timer"]) - 1d / 60; p.AdvancePotRescues(); }
        }
        int AddBun(CarnivalPlanner p)
        {
            // The captured board23 is empty. This synthetic future observation
            // adds a pure chopped bun after, rather than before, safe parking.
            int id = 10000;
            var bunLeaf = p.Entity(161)!["composition"]!["children"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == CarnivalRecipes.Bun.Id).DeepClone();
            p.entities[id] = new JsonObject { ["id"] = id, ["observedOrdinal"] = 9999, ["active"] = true, ["name"] = "HotdogBun_Chopped",
                ["components"] = new JsonArray("CarryableItem", "ServerPreparationContainer"), ["position"] = p.Entity(23)!["position"]!.DeepClone(),
                ["composition"] = new JsonObject { ["type"] = "CompositeAssembledNode", ["children"] = new JsonArray(bunLeaf) } };
            p.Entity(23)!["attachedEntityId"] = id; return id;
        }
        void EmptyPot(CarnivalPlanner p)
        {
            p.Entity(7)!["composition"] = p.Entity(2)!["composition"]!.DeepClone();
            p.Entity(7)!["contents"] = new JsonArray(); p.Entity(7)!["ingredientIds"] = new JsonArray(); p.Entity(7)!["cookingProgress"] = 0;
        }

        var p = Make(); int score = I(p.state["score"]); double timer = N(p.state["timer"]);
        Check(p.Frame == 2384 && p.Held(3) == 0 && p.NativeSausagePot(7, true) && N(p.Entity(7)?["cookingProgress"]) > 18.5,
            "actual completed cannon boundary exposes an empty chef and mature native cooked pot");
        Check(p.potHomes.Count == 2 && p.potHomes[7].Home == p.AttachmentParent(7) && p.PendingPotRescues().SequenceEqual(new[] { 7 }),
            "admission resolves both original native homes and only the actual mature sausage candidate");
        Check(!p.Stations("chop").Any(s => p.Chopped(p.Attached(s.EntityId), CarnivalRecipes.Bun.Id)),
            "actual captured scene has no chopped bun on a board for immediate hotdog harvest");
        Check(p.TryRescuePot(3, 7), "a mature cooked pot can park before any bun or recipe claim is available");
        var lease = p.potRescues[7]; var rescueWork = p.workers[3]!;
        Check(lease.Index == -1 && lease.Owner == 3 && p.reserved.SetEquals(lease.Resources) && rescueWork.OwnedResources.Count == 0,
            "pot/home/counter have one persistent owner and no premature recipe assignment");
        Check(rescueWork.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "navigate", "cook", "take", "place" }) &&
            rescueWork.Actions.All(a => !B(a["dash"]) && !B(a["shortDash"])), "rescue uses native cooking and ordinary whole-pot inputs");
        Check(!p.TryRescuePot(0, 7) && !p.PotAvailableForHotdog(7), "another chef cannot acquire an active pot rescue");
        Check(p.EmptyAttachment(lease.Counter) && !NativeCookingStation(p.Entity(lease.Counter)), "destination is an empty ordinary offheat counter");
        p.Entity(lease.Home)!["attachedEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = 7; FinishWork(p, 3);
        Check(lease.Phase == PotPhase.Verifying && p.workers[3] is null && p.IsPotParticipant(3) && !p.PotCookAvailable(3),
            "completed native placement keeps the chef reserved through proof");
        p.AdvancePotRescues(); Check(lease.StableSamples == 0 && !p.PotAvailableForHotdog(7), "paused inspections cannot fabricate advancing offheat evidence");
        Prove(p);
        Check(lease.Phase == PotPhase.Parked && lease.Owner == -1 && lease.Index == -1 && p.PotAvailableForHotdog(7) && !p.Free(7),
            "two advancing unchanged samples release the chef while retaining all vessel resources");
        Check(I(p.state["score"]) == score && N(p.state["timer"]) < timer && N(p.Entity(7)?["cookingProgress"]) == lease.Progress,
            "offline fixture never awards score or changes the observed offheat cooking progress");

        int bun = AddBun(p), index = p.delivered;
        Check(p.BuildUnplatedHotdog(3), "the ordinary unplated-hotdog planner can consume a parked pot once a native bun exists");
        var harvest = p.workers[3]!;
        Check(lease.Phase == PotPhase.Consuming && lease.Index == index && lease.Owner == 3 && ReferenceEquals(lease.Work, harvest) &&
            harvest.Actions.Any(a => a["type"]?.ToString() == "combine" && StationIs(a, 7)), "exact native pot is assigned to one eligible hotdog at claim time");
        Check(harvest.OwnedResources.All(r => !lease.Resources.Contains(r)) && p.reserved.IsSupersetOf(lease.Resources),
            "consuming work never double-owns a persistent pot resource");
        Reject(() => p.BeginPotConsumption(7, 0, index + 1), "another meal cannot steal an already claimed parked pot");
        p.Entity(23)!["attachedEntityId"] = 0; p.chefs[3]["heldEntityId"] = bun; CompleteNext(p, 3); p.ReleaseCompletedUnplatedResources();
        Check(p.Free(23) && !harvest.OwnedResources.Contains(23) && !p.Free(7), "ordinary source-board staged release remains available while pot lease persists");
        Check(p.Start(0, "independent-board-refill", [p.A("place", 23)], [23]), "released source board can gain an independent successor owner");
        var successor = p.workers[0]!;
        p.Entity(bun)!["composition"] = p.Entity(161)!["composition"]!.DeepClone(); EmptyPot(p); CompleteNext(p, 3); p.ReleaseCompletedUnplatedResources();
        Check(p.EmptyFood(7) && !p.Free(7) && !harvest.OwnedResources.Contains(7) && p.reserved.IsSupersetOf(lease.Resources),
            "native sausage transfer cannot release or refill the parked empty pot");
        p.AdvancePotRescues();
        Check(!p.Start(1, "illegal-empty-pot-refill", [p.A("place", 7)], [7]) && !p.Start(1, "illegal-home-reuse", [p.A("place", lease.Home)], [lease.Home]),
            "raw loading and stove reuse are blocked until exact empty restoration");
        int destination = I(harvest.Actions.Last()["station"]); p.Entity(destination)!["attachedEntityId"] = bun; p.chefs[3]["heldEntityId"] = 0;
        CompleteNext(p, 3);
        Check(p.mealFoods[index] == bun && !p.assembling.Contains(index) && lease.Phase == PotPhase.Restoring &&
            p.workers[3]!.Name == "restore-empty-pot-7", "verified native unplated meal completion retains its chef for empty-pot restoration");
        Check(p.reserved.IsSupersetOf(lease.Resources) && p.reserved.Contains(23) && successor.OwnedResources.SetEquals(new[] { 23 }),
            "old harvest completion preserves both persistent pot lease and overlapping successor board ownership");
        p.Entity(lease.Counter)!["attachedEntityId"] = 0; p.Entity(lease.Home)!["attachedEntityId"] = 7; FinishWork(p, 3);
        Check(p.potRescues.Count == 0 && p.Free(lease.Resources) && p.reserved.SetEquals(new[] { 23 }) && ReferenceEquals(p.workers[0], successor),
            "original empty pot restoration releases only its own three resources");
        Check(p.Start(1, "legal-restored-pot-refill", [p.A("place", 7)], [7, lease.Home]), "restored original empty pot becomes available to normal native loading");

        foreach (string defect in new[] { "pot-ordinal", "home-ordinal", "counter-ordinal", "reservation", "double-owner", "lost-work", "wrong-food", "wrong-duration", "missing-composition", "missing-progress", "missing-home-attachment" })
        {
            var bad = Make(); bad.TryRescuePot(3, 7); var heldLease = bad.potRescues[7];
            switch (defect)
            {
                case "pot-ordinal": bad.Entity(7)!["observedOrdinal"] = 999; break;
                case "home-ordinal": bad.Entity(heldLease.Home)!["observedOrdinal"] = 999; break;
                case "counter-ordinal": bad.Entity(heldLease.Counter)!["observedOrdinal"] = 999; break;
                case "reservation": bad.reserved.Remove(heldLease.Counter); break;
                case "double-owner": bad.workers[0] = new Work("injected-conflicting-owner", [], [7], null); break;
                case "lost-work": bad.workers[3] = null; break;
                case "wrong-food": bad.Entity(7)!["composition"]!["children"]![0]!["id"] = CarnivalRecipes.Onion.Id; break;
                case "wrong-duration": bad.Entity(7)!["cookingTime"] = 10; break;
                case "missing-composition": bad.Entity(7)!.Remove("composition"); break;
                case "missing-progress": bad.Entity(7)!.Remove("cookingProgress"); break;
                case "missing-home-attachment": bad.Entity(heldLease.Home)!.Remove("attachedEntityId"); break;
            }
            Reject(bad.AdvancePotRescues, "lease rejects captured-observation ownership mutation " + defect);
        }
        foreach (string defect in new[] { "progress", "composition-progress", "stove-occupied", "counter-detached", "unexpected-pickup", "verification-job" })
        {
            var bad = Make(); var heldLease = Park(bad);
            switch (defect)
            {
                case "progress": bad.Entity(7)!["cookingProgress"] = heldLease.Progress + .016; break;
                case "composition-progress": bad.Entity(7)!["composition"]!["progress"] = 1.9; break;
                case "stove-occupied": bad.Entity(heldLease.Home)!["attachedEntityId"] = 2; break;
                case "counter-detached": bad.Entity(heldLease.Counter)!["attachedEntityId"] = 0; break;
                case "unexpected-pickup": bad.chefs[0]["heldEntityId"] = 7; break;
                case "verification-job": bad.workers[3] = new Work("unexpected-cold-job", [], [], null); break;
            }
            Reject(bad.AdvancePotRescues, "offheat proof rejects " + defect);
        }
        var deadline = Make(); deadline.Entity(7)!["cookingProgress"] = 23;
        deadline.workers[3] = new Work("ordinary-unleased-hotdog", [], [7], null); deadline.reserved.Add(7);
        Timeout(deadline.AdvancePotRescues, "ordinary active unleased hotdog work cannot bypass the23-second native deadline");
        var rescueDeadline = Make(); rescueDeadline.TryRescuePot(3, 7); rescueDeadline.Entity(7)!["cookingProgress"] = 23;
        Timeout(rescueDeadline.AdvancePotRescues, "failed pot rescue cleanly stops before native24-second burn");
        var budget = Make(); var budgetLease = Park(budget); budget.state["gameplayFrame"] = budgetLease.PhaseFrame + 721;
        Timeout(budget.AdvancePotRescues, "advancing phase budget is bounded without time restoration");
        var noStorage = Make(); foreach (var s in noStorage.Stations("counter")) noStorage.reserved.Add(s.EntityId);
        var before = noStorage.reserved.ToArray();
        Check(!noStorage.TryRescuePot(3, 7) && noStorage.potRescues.Count == 0 && noStorage.reserved.SetEquals(before),
            "unavailable ordinary counter causes no partial lease or resource theft");
        var busy = Make(); busy.workers[3] = new Work("existing-native-job", [], [24], null); busy.reserved.Add(24);
        var active = busy.workers[3]; Check(!busy.TryRescuePot(3, 7) && ReferenceEquals(busy.workers[3], active) && busy.reserved.SetEquals(new[] { 24 }),
            "rescue admission preserves an existing job and its resource ownership");
        var cold = Make(); cold.Entity(7)!["cookingProgress"] = 10.99;
        Check(!cold.PendingPotRescues().Any() && !cold.TryRescuePot(3, 7), "cold heat cannot outrank native urgent candidates through premature pot admission");
        var nearReady = Make(); nearReady.Entity(7)!["cookingProgress"] = 11; nearReady.Entity(7)!["composition"]!["state"] = "Raw";
        Check(nearReady.PendingPotRescues().Contains(7) && nearReady.TryRescuePot(3, 7) && nearReady.workers[3]!.Actions.Any(a => a["type"]?.ToString() == "cook"),
            "preapproach at native11 seconds retains the normal cook wait instead of inventing Cooked state");
        var wrongHome = Make(); wrongHome.Entity(wrongHome.potHomes[7].Home)!["attachedEntityId"] = 0;
        Check(!wrongHome.TryRescuePot(3, 7), "a pot outside its observed original stove is not silently adopted as a fresh heat rescue");
        var direct = Make(); AddBun(direct);
        // A second cooked pot is synthetic here so exact-vessel selection is
        // tested even when a different ordinary pot would sort first.
        direct.Entity(2)!["composition"] = direct.Entity(7)!["composition"]!.DeepClone(); direct.Entity(2)!["cookingProgress"] = 12.5;
        Check(direct.DirectPotHarvestHasTime(3, 7) && direct.TryRescuePot(3, 7) && direct.potRescues.Count == 0,
            "a ready bun and measured safe walking/input budget admit direct harvest instead of unnecessary parking");
        Check(direct.workers[3]!.Name.StartsWith("unplated-hotdog-", StringComparison.Ordinal) && direct.workers[3]!.OwnedResources.Contains(7) &&
            !direct.workers[3]!.OwnedResources.Contains(2) && direct.workers[3]!.Actions.Any(a => a["type"]?.ToString() == "combine" && StationIs(a, 7)),
            "direct harvest obeys the arbiter's exact selected pot despite another ready vessel");
        direct.Entity(7)!["cookingProgress"] = 23;
        Timeout(direct.AdvancePotRescues, "direct safe-budget admission remains subject to every advancing native23-second deadline");
        var lateDirect = Make(); AddBun(lateDirect); lateDirect.Entity(7)!["cookingProgress"] = 21.1;
        Check(!lateDirect.DirectPotHarvestHasTime(3, 7), "a late bun-pickup detour cannot pass the two-second minimum input budget");
        var sourceOwned = Make(); AddBun(sourceOwned); sourceOwned.reserved.Add(23);
        Check(!sourceOwned.DirectPotHarvestHasTime(3, 7) && sourceOwned.TryRescuePot(3, 7) && sourceOwned.potRescues.ContainsKey(7),
            "a bun owned by another job cannot force direct consumption or prevent mature-pot parking");
        return count;
    }
}
