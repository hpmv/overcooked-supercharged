using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured loaded V7 cannon plus synthetic native transitions. No game calls or process control.</summary>
    public static int CannonFlightSelfTest(JsonObject loaded1287)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Cannon flight regression: " + message); count++; }
        void Reject(Action action, string message) { bool failed = false; try { action(); } catch (InvalidOperationException) { failed = true; } Check(failed, message); }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline flight fixture attempted I/O."), null)
            { response = loaded1287.DeepClone().AsObject(), options = new(ReleaseFiringChefOnLaunch: enabled), recipes = CarnivalRecipes.All.ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline flight fixture attempted I/O."), null);
            p.chefs[0]["useTargetId"] = 78; p.chefs[0]["lastVelocity"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
            return p;
        }
        void AdvanceFrame(CarnivalPlanner p)
        { p.state["gameplayFrame"] = p.Frame + 1; p.state["clientTime"] = N(p.state["clientTime"]) + 1d / 60; p.state["timer"] = N(p.state["timer"]) - 1d / 60; }
        JsonObject TickFire(CarnivalPlanner p)
        {
            var work = p.workers[0]!;
            if (work.Active is null) { var spec = work.Actions.Dequeue(); spec["player"] = 0; work.Active = p.runner.CreateAction(spec); }
            p.AdvanceCannonFlights();
            var input = p.runner.Tick(work.Active, p.response);
            var all = Inputs.AllNeutral(); all[0] = input; p.response["inputs"] = new JsonArray(all.Select(i => (JsonNode?)i!.DeepClone()).ToArray());
            if (work.Active.Error is { } error) throw new InvalidOperationException("Fixture fire action: " + error);
            return input;
        }
        CannonFlight Launch(CarnivalPlanner p)
        {
            if (!p.FireLoaded(0)) throw new InvalidOperationException("Captured native Load could not start firing.");
            var flight = p.cannonFlights[84];
            if (!B(TickFire(p)["use"])) throw new InvalidOperationException("Fixture did not emit its verified fire edge.");
            AdvanceFrame(p); p.Entity(84)!["cannonState"] = "Launched"; p.Entity(84)!["cannonFlying"] = true;
            TickFire(p); AdvanceFrame(p); p.AdvanceCannonFlights(); p.CompleteWork(); return flight;
        }
        void Land(CarnivalPlanner p, CannonFlight flight)
        {
            p.Entity(flight.Cannon)!["cannonFlying"] = false;
            var chef = p.chefs[flight.Passenger]; chef["controlsEnabled"] = true; chef["directlyControlled"] = true;
            chef["canAcceptInput"] = true; chef["inputSuppressed"] = false;
            chef["position"] = new JsonObject { ["x"] = flight.Landing.X, ["y"] = .3, ["z"] = flight.Landing.Z };
        }
        var baseline = Make(false);
        Check(baseline.FireLoaded(0) && baseline.cannonFlights.Count == 0 && baseline.workers[0]!.OwnedResources.SetEquals(new[] { 84, 78 }) &&
            baseline.workers[0]!.Actions.Single()["completeOnLaunch"] is null, "default keeps existing fire action and cannon/button ownership until native arrival");
        var p = Make(); int originalHeld = p.Held(2);
        Check(p.Frame == 1287 && originalHeld == 10 && p.Entity(84)?["cannonState"]?.ToString() == "Load", "actual fixture contains the disabled loaded chef carrying the original plate");
        Check(p.FireLoaded(0), "explicit option admits a separate passenger-arrival obligation");
        var flight = p.cannonFlights[84]; var fireWork = p.workers[0]!;
        Check(flight.Phase == CannonFlightPhase.Firing && flight.Owned.SetEquals(new[] { 84, 105, 10 }) && fireWork.OwnedResources.SetEquals(new[] { 78 }),
            "persistent flight owns cannon/passenger/held item while firing work alone owns button");
        Check(flight.Destination == "lower-right" && flight.Landing.Distance(KitchenModel.Position(p.Entity(84)?["cannonTarget"])) == 0 && p.IsCannonArrivalPassenger(2),
            "landing reservation binds the actual native target and exact passenger slot");
        Check(!p.FireLoaded(3) && !p.Start(1, "steal-passenger-item", [p.A("take", 10)], [10]), "independent jobs cannot steal cannon or passenger item");
        var edge = TickFire(p);
        Check(B(edge["use"]) && !B(edge["pickup"]) && !B(edge["dash"]) && p.workers[0]!.Active!.Stage == "await-cannon-launch", "exact ready-button target emits one native fire edge");
        AdvanceFrame(p); p.Entity(84)!["cannonState"] = "Launched"; p.Entity(84)!["cannonFlying"] = false;
        Check(!B(TickFire(p)["use"]) && !p.workers[0]!.Active!.IsDone && p.runner.CannonLaunchReceipt(p.workers[0]!.Active!) is null,
            "stale Launched without actual native flying cannot free the firing chef");
        AdvanceFrame(p); p.Entity(84)!["cannonFlying"] = true;
        var release = TickFire(p); var completed = p.workers[0]!.Active!;
        Check(completed.IsDone && completed.Error is null && !B(release["use"]) && N(release["x"]) == 0 && N(release["y"]) == 0,
            "verified native launch completes opt-in action with a neutral release input");
        Check(p.runner.CannonLaunchReceipt(completed) is { } receipt && I(receipt["held"]) == originalHeld &&
            I(receipt["passenger"]) == 105 && B(receipt["nativeFlying"]), "completed action retains its immutable native launch receipt");
        AdvanceFrame(p); p.AdvanceCannonFlights(); p.CompleteWork();
        Check(p.workers[0] is null && !p.reserved.Contains(78) && flight.Phase == CannonFlightPhase.Flying &&
            p.reserved.IsSupersetOf(flight.Owned), "accepted launch releases firing chef/button while preserving cannon and landing obligations");
        Check(p.Start(0, "independent-center-work", [p.A("navigate", 33)], [33]), "firing chef can start useful work during the native flight");
        var independent = p.workers[0];
        p.chefs[2]["controlsEnabled"] = true; p.chefs[2]["canAcceptInput"] = true;
        Check(p.NativeCannonFlight(2), "temporary native launch control restoration remains blocked by exact passenger obligation");
        p.PantryAndService(p.Region(2)); Check(p.workers[2] is null, "outer chef cannot dispatch pantry work during native launch glitch");
        p.AdvanceCannonFlights(); Check(flight.StableArrivalSamples == 0, "free control alone cannot establish arrival while native flight is active");
        Land(p, flight); AdvanceFrame(p); p.AdvanceCannonFlights();
        Check(flight.StableArrivalSamples == 1 && p.IsCannonArrivalPassenger(2) && !p.Free(84, 105, 10), "first landed sample preserves passenger and landing ownership");
        p.AdvanceCannonFlights(); Check(flight.StableArrivalSamples == 1, "repeated paused inspection cannot create second arrival sample");
        AdvanceFrame(p); p.AdvanceCannonFlights();
        Check(p.cannonFlights.Count == 0 && p.Free(84, 105, 10) && p.reserved.SetEquals(new[] { 33 }) && ReferenceEquals(p.workers[0], independent),
            "two controlled native arrival samples release only flight resources and preserve independent firing-chef work");
        Check(!p.NativeCannonFlight(2), "native stale loaded ID after completed arrival does not block later outer work");

        foreach (string defect in new[] { "held-id", "held-ordinal", "passenger-ordinal", "cannon-ordinal", "destination", "loaded-id", "reservation", "passenger-job", "respawn" })
        {
            var bad = Make(); var f = Launch(bad);
            switch (defect)
            {
                case "held-id": bad.chefs[2]["heldEntityId"] = 11; break;
                case "held-ordinal": bad.Entity(10)!["observedOrdinal"] = 9000; break;
                case "passenger-ordinal": bad.Entity(105)!["observedOrdinal"] = 9000; break;
                case "cannon-ordinal": bad.Entity(84)!["observedOrdinal"] = 9000; break;
                case "destination": bad.Entity(84)!["cannonTarget"]!["x"] = f.Landing.X + 1; break;
                case "loaded-id": bad.Entity(84)!["cannonLoadedEntityId"] = 104; break;
                case "reservation": bad.reserved.Remove(10); break;
                case "passenger-job": bad.workers[2] = new Work("illegal-outer-job", [], [], null); break;
                case "respawn": bad.chefs[2]["respawning"] = true; break;
            }
            Reject(bad.AdvanceCannonFlights, "arrival rejects exact identity/ownership mutation " + defect);
        }
        foreach (string defect in new[] { "still-flying", "disabled", "suppressed", "wrong-region", "high", "outside-landing" })
        {
            var bad = Make(); var f = Launch(bad); Land(bad, f);
            switch (defect)
            {
                case "still-flying": bad.Entity(84)!["cannonFlying"] = true; break;
                case "disabled": bad.chefs[2]["controlsEnabled"] = false; break;
                case "suppressed": bad.chefs[2]["inputSuppressed"] = true; break;
                case "wrong-region": bad.chefs[2]["position"]!["x"] = 12; break;
                case "high": bad.chefs[2]["position"]!["y"] = 1.1; break;
                case "outside-landing": bad.chefs[2]["position"]!["x"] = 30; break;
            }
            AdvanceFrame(bad); bad.AdvanceCannonFlights(); AdvanceFrame(bad); bad.AdvanceCannonFlights();
            Check(bad.cannonFlights.Count == 1 && f.StableArrivalSamples == 0, "arrival requires complete native evidence: " + defect);
        }
        var late = Make(); var lateFlight = Launch(late); late.state["gameplayFrame"] = lateFlight.LaunchFrame + CannonArrivalTimeoutFrames + 1;
        bool timedOut = false; try { late.AdvanceCannonFlights(); } catch (TimeoutException) { timedOut = true; }
        Check(timedOut, "missing exact native arrival stops at bounded two-second deadline");
        var tamper = Make(); tamper.FireLoaded(0); var t = tamper.cannonFlights[84];
        tamper.workers[0] = null; tamper.reserved.Remove(78);
        Reject(() => tamper.ConfirmCannonLaunch(t), "native Launched state without this action's launch receipt cannot release chef ownership");
        var heldDuringLaunch = Make(); heldDuringLaunch.FireLoaded(0); TickFire(heldDuringLaunch);
        AdvanceFrame(heldDuringLaunch); heldDuringLaunch.Entity(84)!["cannonState"] = "Launched"; heldDuringLaunch.Entity(84)!["cannonFlying"] = true;
        heldDuringLaunch.chefs[2]["heldEntityId"] = 11;
        var heldAction = heldDuringLaunch.workers[0]!.Active!;
        var heldInput = heldDuringLaunch.runner.Tick(heldAction, heldDuringLaunch.response);
        Check(heldAction.IsDone && heldAction.Error is not null && heldDuringLaunch.runner.CannonLaunchReceipt(heldAction) is null && !B(heldInput["use"]),
            "route-level launch completion rejects a exchanged passenger item and releases input without fabricating a receipt");
        var unreleased = Make(); unreleased.FireLoaded(0); TickFire(unreleased);
        AdvanceFrame(unreleased); unreleased.Entity(84)!["cannonState"] = "Launched"; unreleased.Entity(84)!["cannonFlying"] = true;
        TickFire(unreleased); AdvanceFrame(unreleased); unreleased.AdvanceCannonFlights();
        unreleased.response["inputs"]![0]!["use"] = true;
        Reject(unreleased.CompleteWork, "planner cannot free firer when the recorded completed frame still holds the use button");

        // Reconstruct a real completed assembly boundary but explicitly remove
        // unrelated heat urgency in this cloned fixture. Mandatory heat takes
        // precedence in the untouched native1287 snapshot; this tests only the
        // optional interruption's exact owner/queue continuation.
        var interrupted = Make(); interrupted.options = new(ReleaseFiringChefOnLaunch: true, CannonBoundaryPreemption: true);
        foreach (var station in interrupted.Stations("pot").Concat(interrupted.Stations("pan")).Concat(interrupted.Stations("basket")))
            interrupted.Entity(station.EntityId)!["cookingProgress"] = 0;
        foreach (var station in interrupted.Stations("bowl")) interrupted.Entity(station.EntityId)!["mixingProgress"] = 0;
        var remaining = new[] { interrupted.A("take", 33), interrupted.A("apply", 72), interrupted.A("place", 49) };
        Check(interrupted.Start(0, "assemble-meal-2-Hotdog_Ketchup_Mustard", remaining, [11, 153, 33, 49, 72, 79]),
            "interruption fixture reconstructs original serial assembly ownership");
        interrupted.Start(3, "existing-dough-work", [interrupted.A("place", 18)], [6, 5, 18]);
        var original = interrupted.workers[0]!; original.NeutralBoundaryFrame = interrupted.Frame;
        original.CompletedBoundaryAction = new JsonObject { ["type"] = "switch-condiment", ["index"] = 1 };
        string queue = System.Text.Json.JsonSerializer.Serialize(original.Actions); var originalResources = original.OwnedResources.ToArray();
        Check(interrupted.TryBeginCannonBoundaryPreemption() && interrupted.cannonFlights.ContainsKey(84) &&
            ReferenceEquals(interrupted.cannonInterruptions[0].Original, original), "optional preemption acquires a separate flight while retaining the exact original work");
        TickFire(interrupted); AdvanceFrame(interrupted); interrupted.Entity(84)!["cannonState"] = "Launched"; interrupted.Entity(84)!["cannonFlying"] = true;
        TickFire(interrupted); AdvanceFrame(interrupted); interrupted.AdvanceCannonFlights(); interrupted.CompleteWork();
        Check(ReferenceEquals(interrupted.workers[0], original) && original.Active is null && interrupted.cannonInterruptions.Count == 0 &&
            System.Text.Json.JsonSerializer.Serialize(original.Actions) == queue && original.OwnedResources.SetEquals(originalResources),
            "verified launch resumes the identical original queue with no active action reset or premature callback");
        Check(interrupted.cannonFlights[84].Phase == CannonFlightPhase.Flying && interrupted.reserved.Contains(84) &&
            !interrupted.reserved.Contains(78) && originalResources.All(interrupted.reserved.Contains),
            "resumed assembly retains its own leases beside the independent cannon/passenger landing obligation");
        return count;
    }
}
