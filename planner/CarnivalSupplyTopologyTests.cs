using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Native V13 interception and explicit mutations of native start geometry; no game I/O.</summary>
    public static int SupplyTopologySelfTest(JsonObject initial, JsonObject beforeInterception, JsonObject intercepted)
    {
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException("Supply topology fixture: " + reason); checks++; }
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline I/O prohibited."), null)
            { response = initial.DeepClone().AsObject(), options = new(ConditionalFarPotThrows: true), recipes = CarnivalRecipes.All.ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.CaptureSupplyTopology();
            p.response = snapshot.DeepClone().AsObject(); if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline I/O prohibited."), null);
            return p;
        }
        bool Handoff(CarnivalPlanner p, int player, int target, int pass) => p.workers[player] is { } w &&
            w.Actions.All(a => a["type"]?.ToString() != "throw") && I(w.Actions.Last()["station"]) == pass &&
            p.counterSupplies.TryGetValue(pass, out var address) && address.Vessel == target;
        var before = Make(beforeInterception); var after = Make(intercepted);
        Check(before.Frame == 7848 && after.Frame == 7851, "actual frame fixtures bracket the native thrown sausage interception");
        Check(before.Attached(38) == 7 && before.Attached(19) == 0 && after.Attached(38) == 7 && after.Attached(19) == 271,
            "the parked pot stayed on38 while raw271 occupied its reserved original stove19");
        Check(PotCompositionOf(before, 7) == PotCompositionOf(after, 7) && N(before.Entity(7)!["cookingProgress"]) == N(after.Entity(7)!["cookingProgress"]),
            "the demonstrated failure did not change the parked pot food or clock");
        Check(before.Stations("pot").OrderBy(s => s.Position.X).First().EntityId == 2 && before.NearSupplyVessel(2) == 7,
            "captured topology preserves near7 when the old current-X sort mislabels far2");
        var originalWork = new Work("existing-native-rescue", [], [4, 16, 37], null);
        before.workers[0] = originalWork; foreach (int r in originalWork.OwnedResources) before.reserved.Add(r);
        foreach (int r in new[] { 7, 19, 38 }) before.reserved.Add(r);
        Check(before.Supply(2, CarnivalRecipes.Frankfurter, 2, true) && Handoff(before, 2, 2, 45), "actual far-pot refill selects exact addressed handoff with the near pot parked");
        Check(ReferenceEquals(before.workers[0], originalWork) && new[] { 7, 19, 38 }.All(before.reserved.Contains) &&
            !before.workers[2]!.OwnedResources.Overlaps([7, 19, 38]), "fallback preserves the independent cooked-pot rescue and active chef leases");
        var held = Make(beforeInterception); held.chefs[2]["heldEntityId"] = 271;
        held.SupplyHeld(2, 271);
        Check(Handoff(held, 2, 2, 45), "already-held sausage also uses original topology rather than reclassifying the far pot");

        var movedBowl = Make(initial); movedBowl.Entity(18)!["attachedEntityId"] = 0; movedBowl.Entity(32)!["attachedEntityId"] = 6;
        movedBowl.Entity(6)!["position"] = movedBowl.Entity(32)!["position"]!.DeepClone(); movedBowl.model = KitchenModel.Build(movedBowl.state);
        Check(movedBowl.Stations("bowl").OrderByDescending(s => s.Position.X).First().EntityId == 3 && movedBowl.NearSupplyVessel(1) == 6,
            "parked near bowl cannot turn the actual far bowl into the near receiver");
        Check(movedBowl.Supply(1, CarnivalRecipes.Flour, 3, true) && Handoff(movedBowl, 1, 3, 48), "far bowl remains an exact addressed handoff when near bowl is parked");
        Check(movedBowl.DirectSupplyGuard(1, 6) is null, "the parked near bowl itself is ineligible for an unproven direct throw");
        Check(after.DirectSupplyGuard(2, 7) is null && after.DirectSupplyGuard(2, 2) is null, "neither a parked near pot nor actual far pot gains direct near-lane admission");

        foreach (int player in new[] { 2, 1 })
        {
            var good = Make(initial); int vessel = good.NearSupplyVessel(player), home = good.supplyHomes[vessel].Home;
            var guard = good.DirectSupplyGuard(player, vessel);
            Check(guard is not null && NativeSupplyHome.Invalid(good.state, guard) is null, "intact native near receiving home remains eligible for player" + player);
            var ingredient = player == 2 ? CarnivalRecipes.Frankfurter : CarnivalRecipes.Flour;
            Check(good.Supply(player, ingredient, vessel, true) && good.workers[player]!.Actions.Last()["nativeSupplyHome"] is JsonObject &&
                good.workers[player]!.OwnedResources.Contains(home), "direct job leases the native processing home and carries its runtime barrier");
            foreach (string mutation in new[] { "detached", "reused-home", "counter-proxy", "moved-vessel", "held-vessel" })
            {
                var changed = Make(initial);
                if (mutation == "detached") changed.Entity(home)!["attachedEntityId"] = 0;
                if (mutation == "reused-home") changed.Entity(home)!["observedOrdinal"] = 123456;
                if (mutation == "counter-proxy") changed.Entity(home)!["components"] = new JsonArray("PhysicalAttachment");
                if (mutation == "moved-vessel") changed.Entity(vessel)!["position"]!["x"] = 20;
                if (mutation == "held-vessel") changed.chefs[0]["heldEntityId"] = vessel;
                Check(NativeSupplyHome.Invalid(changed.state, guard!) is not null && changed.DirectSupplyGuard(player, vessel) is null,
                    "direct lane rejects " + mutation + " for player" + player);
            }
        }
        return checks;
    }

    private static string PotCompositionOf(CarnivalPlanner planner, int id) => planner.Entity(id)!["composition"]!.ToJsonString();
}

public sealed partial class RouteRunner
{
    public static int SupplyHomeBarrierSelfTest(JsonObject actualPrearm)
    {
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException("Direct supply home barrier: " + reason); checks++; }
        var observed = KitchenModel.SnapshotState(actualPrearm).DeepClone().AsObject();
        var lane = FarPotLane.Describe(observed); int near = FarPotLane.Id(lane, "nearPot"), far = FarPotLane.Id(lane, "farPot");
        int home = lane["nearHome"]!.GetValue<int>();
        var guard = new JsonObject { ["vessel"] = near, ["home"] = home, ["role"] = "pot",
            ["vesselOrdinal"] = Entity(observed, near)!["observedOrdinal"]!.DeepClone(), ["homeOrdinal"] = Entity(observed, home)!["observedOrdinal"]!.DeepClone(),
            ["vesselPosition"] = Entity(observed, near)!["position"]!.DeepClone(), ["homePosition"] = Entity(observed, home)!["position"]!.DeepClone() };
        // Explicit mutation: the genuine prearm chef/source state, with the
        // near pot made empty using the observed far pot's native empty tree.
        Entity(observed, near)!["contents"] = Entity(observed, far)!["contents"]!.DeepClone();
        Entity(observed, near)!["composition"] = Entity(observed, far)!["composition"]!.DeepClone();
        var runner = new RouteRunner(_ => throw new InvalidOperationException("Offline I/O prohibited."), null);
        RouteActionHandle Action() => runner.CreateAction(new JsonObject { ["type"] = "throw", ["player"] = 2,
            ["targetEntityId"] = near, ["nativeSupplyHome"] = guard.DeepClone() });
        var detached = observed.DeepClone().AsObject(); Entity(detached, home)!["attachedEntityId"] = 0;
        var before = Action(); var neutral = runner.Tick(before, detached);
        Check(before.IsDone && before.Error?.Contains("native receiving home changed") == true && !Flag(neutral, "use"), "detachment before arming fails without emitting Use");
        foreach (string stage in new[] { "throw-arm", "throw-aim", "throw-release" })
        {
            var state = observed.DeepClone().AsObject(); var action = Action();
            Check(Flag(runner.Tick(action, state), "use") && action.Stage == "throw-arm", "valid receiving home emits the ordinary arming edge");
            runner.Stage(action.Action, stage); Chef(state, 2)["aimingThrow"] = true;
            Entity(state, home)!["attachedEntityId"] = 0;
            var input = runner.Tick(action, state);
            Check(action.IsDone && action.Error?.Contains("neutral cleanup may release an armed ingredient") == true && !Flag(input, "use"),
                stage + " detachment is a failed attempt, not a claimed safe cancellation or delivery");
        }
        return checks;
    }
}
