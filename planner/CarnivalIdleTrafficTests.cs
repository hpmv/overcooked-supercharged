using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private static CarnivalPlanner IdleTrafficFixture(JsonObject snapshot)
    {
        var planner = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline traffic fixture attempted I/O."), null)
            { response = snapshot.DeepClone().AsObject(), options = new() };
        if (planner.response["state"] is null) planner.response = new JsonObject { ["state"] = planner.response };
        planner.Refresh(); planner.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline traffic fixture attempted I/O."), null);
        var station = planner.Stations("counter").Single(s => s.Position.Distance(new Point2(15.6, -19.2)) < .05);
        var action = planner.runner.CreateAction(new JsonObject { ["type"] = "place", ["player"] = 0, ["station"] = station.Key });
        action.Action.Frames = 60;
        action.Action.Motion = new RouteRunner.RouteMotion { Model = planner.model, NoPathSince = 0 };
        planner.workers[0] = new Work("relay-whole-dirty-stack", [], [74, station.EntityId], null) { Active = action };
        planner.reserved.UnionWith(planner.workers[0]!.OwnedResources);
        return planner;
    }

    /// <summary>Plan-only legal continuation for the captured V9 dirty-stack blockage; no game connection.</summary>
    public static JsonObject IdleTrafficProbe(JsonObject snapshot)
    {
        var p = IdleTrafficFixture(snapshot);
        if (p.Held(0) == 0 || !p.TryBeginIdleTrafficYield()) throw new InvalidOperationException("Snapshot does not admit the measured dirty-stack yield.");
        var yield = p.trafficYield!;
        return new JsonObject {
            ["qualification"] = "Offline path proof only; native continuation must be run separately.",
            ["frame"] = p.Frame, ["heldDirtyStack"] = p.Held(0),
            ["yieldTarget"] = JsonSerializer.SerializeToNode(yield.Target),
            ["plan"] = new JsonObject { ["description"] = "Continue the paused V9 snapshot: walk the idle chef clear, then legally place the original held dirty stack.", ["timeoutFrames"] = 360,
                ["jobs"] = new JsonArray(
                    new JsonObject { ["id"] = "idle-chef-clear-counter", ["player"] = yield.Helper, ["dependencies"] = new JsonArray(),
                        ["resources"] = new JsonArray("lower-left-shared-counter-approach"), ["actions"] = new JsonArray(yield.Move.Specification) },
                    new JsonObject { ["id"] = "complete-original-dirty-relay", ["player"] = yield.Winner, ["dependencies"] = new JsonArray("idle-chef-clear-counter"),
                        ["resources"] = new JsonArray("lower-left-shared-counter-approach"),
                        ["actions"] = new JsonArray(new JsonObject { ["type"] = "place", ["station"] = yield.WinnerAction.Specification["station"]!.DeepClone(),
                            ["dash"] = false, ["shortDash"] = false, ["timeoutFrames"] = 180 }) }) }
        };
    }

    /// <summary>Captured native geometry plus controller-only ownership and input fixtures.</summary>
    public static int IdleTrafficSelfTest(JsonObject snapshot)
    {
        int count = 0;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException("Idle traffic regression: " + reason); count++; }
        CarnivalPlanner Make() => IdleTrafficFixture(snapshot);
        var p = Make(); var owner = p.workers[0]!; var action = owner.Active!; var originalResources = p.reserved.ToArray();
        var station = p.Station(46)!;
        Check(action.Stage == "resolve" && action.RuntimeEntityId is null, "actual initial approach failure has not resolved a runtime station");
        Check(Navigation.ToStation(p.model, p.Position(0), station).Success, "recorded static geometry is reachable");
        Check(!Navigation.ToStation(p.model, p.Position(0), station, p.TrafficObstacles(0)).Success, "recorded idle chef blocks all approaches");
        Check(p.TryBeginTrafficYield(), "thirty-frame station resolution failure admits a measured idle yield");
        var yielding = p.trafficYield!;
        Check(yielding.Helper == 3 && yielding.Winner == 0 && yielding.IdleWork == p.workers[3] && yielding.HeldAction is null,
            "idle helper receives a separate chef-slot reservation");
        Check(ReferenceEquals(owner, p.workers[0]) && ReferenceEquals(action, p.workers[0]!.Active) && p.reserved.SetEquals(originalResources),
            "requesting action, queue, and all entity leases remain unchanged");
        Check(p.workers[3]!.OwnedResources.Count == 0 && !p.EarlyCookAvailable(3) && !p.BakeryCookAvailable(3),
            "slot reservation excludes new heat and bakery work without borrowing entity leases");
        Check(Navigation.FindPath(p.model, p.Position(3), yielding.Target, .15, p.TrafficObstacles(3)).Success &&
            Navigation.ToStation(p.model, p.Position(0), station, p.TrafficObstacles(0, 3, yielding.Target)).Success,
            "both measured relocation and resumed path retain all collision obstacles");
        p.CompleteWork();
        Check(ReferenceEquals(p.workers[3], yielding.IdleWork), "ordinary completion cannot release a helper placeholder early");
        int originalFrame = p.Frame; Point2 cached = default; bool buttonsNeutral = true;
        for (int frame = 0; frame < 181 && !yielding.Move.IsDone; frame++)
        {
            p.state["framesSinceNoPhysics"] = (frame + 5) % 6;
            var input = p.runner.Tick(yielding.Move, p.response);
            buttonsNeutral &= new[] { "pickup", "use", "dash" }.All(k => !B(input[k]));
            var position = p.Position(3);
            if (frame % 6 != 0) position = new(position.X + cached.X * .02, position.Z + cached.Z * .02);
            var stick = new Point2(N(input["x"]), -N(input["y"])); double length = stick.Distance(default);
            cached = length < 1e-8 ? default : new(stick.X / length * 6, stick.Z / length * 6);
            p.chefs[3]["position"]!["x"] = position.X; p.chefs[3]["position"]!["z"] = position.Z;
            p.chefs[3]["lastVelocity"]!["x"] = cached.X; p.chefs[3]["lastVelocity"]!["z"] = cached.Z;
            p.state["gameplayFrame"] = originalFrame + frame + 1;
        }
        Check(yielding.Move.IsDone && yielding.Move.Error is null && buttonsNeutral, "queued native-rate fixture reaches the yield with ordinary walking only");
        p.CompleteWork(); p.AdvanceTrafficYield();
        Check(yielding.Arrived && ReferenceEquals(p.workers[3], yielding.IdleWork) && action.ElapsedFrames == 60,
            "arrival retains helper slot while the untouched winner resumes");
        p.reserved.Add(8888); action.IsDone = true; p.CompleteWork(); p.AdvanceTrafficYield();
        Check(p.trafficYield is null && p.workers[3] is null && p.reserved.SetEquals([8888]),
            "completion releases only the helper slot and the normally completed winner's own leases");
        var transient = Make(); transient.workers[0]!.Active!.Action.Frames = 29;
        Check(!transient.TryBeginIdleTrafficYield(), "short transient blockage is not an interruption");
        var held = Make(); held.chefs[3]["heldEntityId"] = 314;
        Check(!held.TryBeginIdleTrafficYield(), "a chef carrying an item is not an idle helper");
        var busy = Make(); busy.workers[3] = new Work("existing-job", [], [], null);
        Check(!busy.TryBeginIdleTrafficYield(), "existing work ownership prevents relocation");
        foreach (string flag in new[] { "controlsEnabled", "directlyControlled", "canAcceptInput" })
        {
            var disabled = Make(); disabled.chefs[3][flag] = false;
            Check(!disabled.TryBeginIdleTrafficYield(), "native disabled control gate " + flag + " prevents relocation");
        }
        foreach (string flag in new[] { "inputSuppressed", "respawning", "aimingThrow" })
        {
            var disabled = Make(); disabled.chefs[3][flag] = true;
            Check(!disabled.TryBeginIdleTrafficYield(), "native " + flag + " prevents relocation");
        }
        var unknown = Make(); unknown.chefs[3].Remove("lastVelocity");
        Check(!unknown.TryBeginIdleTrafficYield(), "missing stopping evidence does not imply stationarity");
        var moving = Make(); moving.chefs[3]["lastVelocity"]!["x"] = 1;
        Check(!moving.TryBeginIdleTrafficYield(), "queued movement cannot be interrupted as idle");
        var heat = Make(); heat.earlyOnion = new EarlyOnionLease(15, 4, 16, 61, 3, 3, heat.Frame);
        Check(!heat.TryBeginIdleTrafficYield(), "chef owned by native heat-rescue lease is protected");
        var bake = Make(); bake.bakeryLeases[6] = new BakeryLease(6, 20, 18, 33, 5, 17, 32, bake.Frame) { Owner = 3 };
        Check(!bake.TryBeginIdleTrafficYield(), "chef owned by native offmix-rescue lease is protected");
        var staticFailure = Make(); staticFailure.chefs[0]["position"]!["x"] = 20.4; staticFailure.chefs[0]["position"]!["z"] = -16.8;
        Check(!staticFailure.TryBeginIdleTrafficYield(), "yield cannot hide a physical overlap with the center station");
        var released = Make(); Check(released.TryBeginIdleTrafficYield(), "owner-loss fixture admits a valid yield");
        released.workers[3] = new Work("replacement", [], [], null); bool rejected = false;
        try { released.AdvanceTrafficYield(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "replacement helper ownership fails rather than stealing another job");
        var timeout = Make(); Check(timeout.TryBeginIdleTrafficYield(), "timeout fixture admits a valid yield"); timeout.state["gameplayFrame"] = timeout.Frame + 241;
        rejected = false; try { timeout.AdvanceTrafficYield(); } catch (TimeoutException) { rejected = true; }
        Check(rejected, "native time advances and a stuck yield terminates within its bounded allowance");
        Check(snapshot.ToJsonString() == Make().response.ToJsonString(), "controller fixtures never mutate the captured native snapshot");
        return count;
    }
}
