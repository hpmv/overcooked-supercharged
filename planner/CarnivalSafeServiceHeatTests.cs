using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int SafeServiceHeatSelfTest(JsonObject at1872, JsonObject? evidence = null)
    {
        int count = 0; void Check(bool yes, string message) { if (!yes) throw new InvalidOperationException("Safe service heat regression: " + message); count++; }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline safe-service test attempted game I/O."), null)
            { response = at1872.DeepClone().AsObject(), options = new(ServeBeforeSafeHeat: true, ReleaseFiringChefOnLaunch: true),
                recipes = new[] { 158500, 125780, 224216, 228996, 47642, 472326, 257844, 130976 }.Select(CarnivalRecipes.GetRecipe).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline safe-service action attempted I/O."), null);
            p.CaptureFryerHomes(); p.CapturePotHomes(); p.bowlHomes[3] = 14; p.bowlHomes[6] = 18;
            p.bowlAssignments[3] = 7; p.bowlFlavors[3] = CarnivalRecipes.Raspberry.Id;
            p.mealPlates[0] = 10; p.mealPlates[3] = 12;
            p.Start(3, "captured-ongoing-meal2", [p.A("place", 38), p.A("switch-condiment", 79), p.A("take", 38)], [13, 149, 37, 52, 72, 79, 38]);
            p.workers[3]!.Active = p.runner.CreateAction(new JsonObject { ["type"] = "place", ["station"] = 38, ["player"] = 3 });
            return p;
        }
        bool Admit(CarnivalPlanner p) => p.TryServeBeforeSafeHeat(new[] { 0, 3 }.Where(p.HeatCookAvailable).ToArray(), p.NativeHeatObligations().OrderBy(h => h.Remaining).ToArray());
        var actual = Make();
        Check(actual.Frame == 1872 && actual.Attached(47) == 10 && I(actual.Entity(84)?["cannonLoadedEntityId"]) == 105 &&
            actual.NativeHeatObligations().Single().Vessel == 3 && N(actual.Entity(3)?["mixingProgress"]) is > 10.09 and < 10.11,
            "actual loaded empty service chef, completed FIFO plate, and sole mature future Raspberry bowl");
        var clocks = actual.SafeServiceUnownedClocks(3);
        Check(clocks.Select(c => c.Vessel).Order().SequenceEqual(new[] { 2, 3, 7 }), "every unowned clock includes both young raw pots before normal warning thresholds");
        string stateBefore = actual.state.ToJsonString(); var other = actual.workers[3]!; var otherAction = other.Active;
        string otherQueue = JsonSerializer.Serialize(other.Actions); int[] otherLocks = other.OwnedResources.Order().ToArray();
        bool admitted = Admit(actual);
        if (!admitted)
        {
            var path = Navigation.ToStation(actual.model, actual.Position(0), actual.Station(78)!, actual.TrafficObstacles(0));
            double minimum = path.Length / 6 + 1;
            actual.SafeServiceBudget(0,3,14,5,path.Points[^1],minimum,out var diagnostic);
            throw new InvalidOperationException("Safe service diagnostic: " + new JsonObject { ["minimumFire"] = minimum, ["buttonPath"] = JsonSerializer.SerializeToNode(path), ["budget"] = diagnostic });
        }
        Check(admitted, "captured1872 admits bounded service before exact ready-soon dough transfer");
        var detour = actual.safeServiceHeat!; var original = detour.Original;
        if (evidence is not null) evidence["capturedAdmission"] = actual.SafeServiceHeatStatus(detour);
        Check(actual.state.ToJsonString() == stateBefore, "admission changes no native snapshot state");
        Check(detour.Dough.Bowl == 3 && detour.Dough.Basket == 5 && original.OwnedResources.SetEquals(new[] { 3, 14, 5, 15 }) && original.Active is null,
            "the untouched original transfer owns exact bowl/mixer and empty basket/fryer");
        Check(actual.workers[0]!.Name == "safe-heat-fire-left" && actual.workers[0]!.OwnedResources.SetEquals(new[] { 78 }) &&
            actual.cannonFlights[84].Owned.SetEquals(new[] { 84, 105 }) && actual.reserved.IsSupersetOf(new[] { 10, 47, 3, 14, 5, 15, 78, 84, 105 }),
            "separate fire, flight, meal and original heat reservations are all retained");
        var fire = actual.workers[0]!.Actions.Single();
        Check(B(fire["completeOnLaunch"]) && !B(fire["dash"]) && !B(fire["shortDash"]) && I(fire["timeoutFrames"]) is > 0 and <= 180,
            "fire releases on native launch with bounded walking input policy");
        Check(actual.HeatHasOwner(3) && !actual.NativeHeatObligations().Any(), "suspended exact heat remains owned and cannot be dispatched twice");
        actual.ObserveSafeServiceHeat(); actual.ObserveNearReadyDoughTransfers(); actual.AdvanceCannonFlights();
        Check(ReferenceEquals(other, actual.workers[3]) && ReferenceEquals(otherAction, other.Active) && JsonSerializer.Serialize(other.Actions) == otherQueue && other.OwnedResources.SetEquals(otherLocks),
            "other central active action, queue and all existing resources remain unchanged");
        Check(!actual.Start(1, "conflicting-original-basket", [actual.A("take", 5)], [5]) && !Admit(actual), "no conflicting basket work or nested detour can start");
        Check(detour.Evidence["deadlines"]!.AsArray().Count == 3 && detour.Evidence["deadlines"]!.AsArray().All(e => N(e?["slackSeconds"]) > 1),
            "captured budget proves separate margin for all three observed processing clocks");
        var scheduled = detour.Evidence["deadlines"]!.AsArray().Skip(1).Select(n => n!.AsObject()).ToArray();
        Check(scheduled.Select(n => I(n["vessel"])).SequenceEqual(new[] { 7, 2 }) &&
            scheduled.Select(n => I(n["parkingWitness"])).Distinct().Count() == 2 &&
            N(scheduled[1]["estimatedSeconds"]) > N(scheduled[0]["serialSecondsAfterParking"]),
            "younger pots are budgeted serially in deadline order with distinct native parking before the next pickup");
        // Resume callback fixture: native launch receipt itself is covered by
        // the existing CannonFlightSelfTest; no synthetic result is called a
        // native launch here. Simulate only the verified callback boundary.
        actual.workers[0] = null; actual.reserved.Remove(78); actual.cannonFlights[84].Phase = CannonFlightPhase.Flying;
        actual.Entity(84)!["cannonState"] = "Launched"; actual.Entity(84)!["cannonFlying"] = true;
        actual.ResumeSafeServiceHeat(detour);
        Check(ReferenceEquals(actual.workers[0], original) && original.Active is null && JsonSerializer.Serialize(original.Actions) == detour.Queue &&
            original.OwnedResources.SetEquals(new[] { 3, 14, 5, 15 }), "verified-launch callback restores the exact original queue and heat leases");
        Check(actual.safeServiceHeat is null && actual.Free(10, 47) && actual.cannonFlights.ContainsKey(84) && !actual.Free(84, 105),
            "FIFO source is released at launch while the existing passenger arrival obligation remains");
        actual.ObserveNearReadyDoughTransfers(); Check(actual.nearReadyDoughTransfers.ContainsKey(original), "normal native dough observer continues after exact work resume");

        foreach (string defect in new[] { "default-off", "without-launch-release", "head-reserved", "head-food", "head-not-fifo", "head-detached", "not-ready", "wrong-passenger", "flying", "passenger-held", "bowl-incomplete", "basket-reserved", "other-mature", "young-pot-slack", "late-bowl", "moving-firer", "held-input", "busy-firer", "two-idle", "order-expiring", "identity-missing", "button-unreachable", "storage-exhausted", "one-parking-counter" })
        {
            var p = Make();
            switch (defect)
            {
                case "default-off": p.options = p.options with { ServeBeforeSafeHeat = false }; break;
                case "without-launch-release": p.options = p.options with { ReleaseFiringChefOnLaunch = false }; break;
                case "head-reserved": p.reserved.Add(10); break;
                case "head-food": p.Entity(10)!["composition"]!["children"]!.AsArray().Single(n => I(n?["id"]) == CarnivalRecipes.Mustard.Id)!["id"] = CarnivalRecipes.Ketchup.Id; break;
                case "head-not-fifo": p.mealPlates[0] = 12; break;
                case "head-detached": p.Entity(47)!["attachedEntityId"] = 0; break;
                case "not-ready": p.Entity(84)!["cannonReady"] = false; break;
                case "wrong-passenger": p.Entity(84)!["cannonLoadedEntityId"] = 104; break;
                case "flying": p.Entity(84)!["cannonFlying"] = true; break;
                case "passenger-held": p.chefs[2]["heldEntityId"] = 11; break;
                case "bowl-incomplete": p.Entity(3)!["composition"]!["children"]!.AsArray().RemoveAt(2); break;
                case "basket-reserved": p.reserved.UnionWith(new[] { 5, 8 }); break;
                case "other-mature": p.Entity(7)!["cookingProgress"] = 11; break;
                case "young-pot-slack": p.Entity(7)!["cookingProgress"] = 8; break;
                case "late-bowl": p.Entity(3)!["mixingProgress"] = 18; break;
                case "moving-firer": p.chefs[0]["lastVelocity"]!["x"] = 6; break;
                case "held-input": p.response["inputs"]!.AsArray().OfType<JsonObject>().Single(i => I(i["player"]) == 0)["use"] = true; break;
                case "busy-firer": p.Start(0, "existing-active-work", [p.A("take", 23)], [23]); break;
                case "two-idle": p.workers[3] = null; p.chefs[3]["heldEntityId"] = 0; break;
                case "order-expiring": p.state["orders"]![0]!["remaining"] = 1; break;
                case "identity-missing": p.Entity(47)!.Remove("observedOrdinal"); break;
                case "button-unreachable": p.Entity(78)!["position"]!["x"] = 999; p.Refresh(); break;
                case "storage-exhausted": p.reserved.UnionWith(p.Stations("counter").Where(s => s.Regions.SequenceEqual(new[] { "center" })).Select(s => s.EntityId)); break;
                case "one-parking-counter": p.reserved.UnionWith(p.Stations("counter").Where(s => s.EntityId != 32 && s.Regions.SequenceEqual(new[] { "center" })).Select(s => s.EntityId)); break;
            }
            var reservations = p.reserved.ToHashSet(); var jobs = p.workers.ToArray();
            Check(!Admit(p) && p.safeServiceHeat is null && p.reserved.SetEquals(reservations) && p.workers.SequenceEqual(jobs), "unsafe admission preserves ordinary heat-first dispatch: " + defect);
        }
        var baseline = Make(); baseline.options = baseline.options with { ServeBeforeSafeHeat = false }; baseline.DispatchNativeHeatSafety();
        Check(baseline.workers[0]?.Name == "wait-then-transfer-mixed-dough" && baseline.safeServiceHeat is null, "default-off executes existing native heat-first behavior at the same captured frame");
        var integrated = Make(); integrated.DispatchNativeHeatSafety();
        Check(integrated.workers[0]?.Name == "safe-heat-fire-left" && integrated.safeServiceHeat?.Original.Active is null,
            "ordinary arbiter hook admits this bounded service case before starting any heat action");
        var bounded = Make(); bounded.Entity(7)!["cookingProgress"] = 7.4;
        Check(Admit(bounded) && bounded.safeServiceHeat!.Timeout is >= 141 and < 180,
            "younger raw-pot slack shortens the actual fire timeout below its three-second ceiling");
        if (evidence is not null) evidence["shortenedTimeoutAdmission"] = bounded.SafeServiceHeatStatus(bounded.safeServiceHeat!);
        foreach (string defect in new[] { "plate-replaced", "heat-replaced", "young-pot-replaced", "home-detached", "lost-resource", "queue-changed", "lost-work", "clock-jumped", "timed-out", "unverified-resume", "parking-capacity-lost" })
        {
            var p = Make(); Check(Admit(p), "prepare observer mutation: " + defect); var plan = p.safeServiceHeat!;
            switch (defect)
            {
                case "plate-replaced": p.Entity(10)!["observedOrdinal"] = 999; break;
                case "heat-replaced": p.Entity(3)!["observedOrdinal"] = 999; break;
                case "young-pot-replaced": p.Entity(7)!["observedOrdinal"] = 999; break;
                case "home-detached": p.Entity(14)!["attachedEntityId"] = 0; break;
                case "lost-resource": p.reserved.Remove(5); break;
                case "queue-changed": plan.Original.Actions.Dequeue(); break;
                case "lost-work": p.workers[0] = null; break;
                case "clock-jumped": p.Entity(7)!["cookingProgress"] = 15; break;
                case "timed-out": p.state["gameplayFrame"] = 1872 + plan.Timeout + 2; break;
                case "parking-capacity-lost": p.reserved.UnionWith(p.Stations("counter").Where(s => s.Regions.SequenceEqual(new[] { "center" })).Select(s => s.EntityId)); break;
            }
            bool rejected = false;
            try { if (defect == "unverified-resume") p.ResumeSafeServiceHeat(plan); else { p.ObserveSafeServiceHeat(); p.ObserveNearReadyDoughTransfers(); } }
            catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, "active exact-identity/ownership/deadline observer rejects: " + defect);
        }
        return count;
    }
}
