using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    // Optional ordinary-input optimization. Final interaction approaches still
    // brake and settle. An intermediate corner may continue only when both the
    // queued old velocity and the following new velocity have measured clearance.
    private bool TryContinueIntermediateWaypoint(Active action, JsonNode state, Point2 position, Point2 projected)
    {
        var motion = action.Motion!;
        if (action.Spec["continuousWaypoints"]?.GetValue<bool>() != true || motion.Model is not { } model ||
            motion.Braking || motion.Dash is not null || motion.Waypoint >= motion.Points.Length - 1 ||
            action.Spec["ignoreChefs"]?.GetValue<bool>() == true || action.Spec["invertX"]?.GetValue<bool>() == true ||
            action.Spec["invertY"]?.GetValue<bool>() == false) return false;
        var chef = Chef(state, Player(action.Spec));
        double logicalDelta = Number(state["unityDeltaTime"] ?? state["clientDeltaTime"]), fixedDelta = Number(state["fixedDeltaTime"]);
        if (chef["lastVelocity"] is null || !double.IsFinite(logicalDelta) || !double.IsFinite(fixedDelta) ||
            !double.IsFinite(position.X) || !double.IsFinite(position.Z) || !double.IsFinite(projected.X) || !double.IsFinite(projected.Z)) return false;
        if (!NativeDashProfile.TryRead(chef, out var profile, out _) || chef["dashTimer"] is null || Number(chef["dashTimer"]) > 0 ||
            chef["impactTimer"] is null || Number(chef["impactTimer"]) >= 0 || chef["controlsEnabled"]?.GetValue<bool>() != true ||
            chef["directlyControlled"]?.GetValue<bool>() != true || chef["canAcceptInput"]?.GetValue<bool>() != true ||
            chef["inputSuppressed"]?.GetValue<bool>() != false || chef["aimingThrow"]?.GetValue<bool>() != false ||
            chef["respawning"]?.GetValue<bool>() != false || chef["interactingEntityId"] is null || Number(chef["interactingEntityId"]) != 0 ||
            state["framesSinceNoPhysics"] is null || Number(state["framesSinceNoPhysics"]) is < 0 or > 5 ||
            Math.Abs(logicalDelta - 1.0 / 60) > .00001 || Math.Abs(fixedDelta - .02) > .00001) return false;
        var cached = KitchenModel.Position(chef["lastVelocity"]);
        if (!double.IsFinite(cached.X) || !double.IsFinite(cached.Z) || cached.Distance(default) > profile!.RunVelocity + .05) return false;
        var next = motion.Points[motion.Waypoint + 1]; double distance = position.Distance(next);
        if (!double.IsFinite(distance) || distance < .30 || model.RegionAt(position) is not { } region || model.RegionAt(next) != region) return false;
        var queued = NextFrameHasPhysics(state) ? projected : position;
        var firstNewStep = new Point2(queued.X + (next.X - position.X) / distance * profile!.RunVelocity * .02,
                                     queued.Z + (next.Z - position.Z) / distance * profile.RunVelocity * .02);
        var others = OtherChefs(state, Player(action.Spec), model, false);
        if (!Navigation.SegmentClear(model, position, queued, region, others) ||
            !Navigation.SegmentClear(model, queued, firstNewStep, region, others) ||
            !Navigation.SegmentClear(model, firstNewStep, next, region, others) ||
            // A no-physics Update may recompute the new direction from the
            // queued position before its next consumption. Check that direct
            // continuation too, not only the first cached-direction estimate.
            !Navigation.SegmentClear(model, queued, next, region, others) ||
            !Navigation.SegmentClear(model, position, next, region, others)) return false;
        int old = motion.Waypoint++;
        motion.LastDistance = double.PositiveInfinity; motion.StalledFrames = 0;
        trace?.Event("navigationIntermediateContinued", new JsonObject { ["player"] = Player(action.Spec),
            ["actionFrames"] = action.Frames, ["waypointIndex"] = old,
            ["position"] = JsonSerializer.SerializeToNode(position), ["oldWaypoint"] = JsonSerializer.SerializeToNode(motion.Points[old]),
            ["nextWaypoint"] = JsonSerializer.SerializeToNode(next), ["queuedPosition"] = JsonSerializer.SerializeToNode(queued),
            ["firstNewDirectionStep"] = JsonSerializer.SerializeToNode(firstNewStep), ["cachedVelocity"] = JsonSerializer.SerializeToNode(cached),
            ["directionRecomputationCorridorChecked"] = true,
            ["firstNewDirectionStepQualification"] = "Cached-direction estimate; an intervening no-physics Update can recompute toward the same next waypoint from the queued position",
            ["nextFrameHasPhysics"] = NextFrameHasPhysics(state) });
        return true;
    }
}
