using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    private sealed class TransportState
    {
        public KitchenModel Model = null!;
        public KitchenStation Source = null!;
        public KitchenStation Interaction = null!;
        public Active? Navigation;
        public int ChefEntityId, InitialHeld, PassengerId, PassengerHeld, DestinationId, Retries;
        public string? SourceRegion, DestinationRegion;
        public double TargetAngle, LastAngle = double.NaN;
        public int LastAngleFrame;
        public bool TransitionObserved, CannonSideCleared, ClearingCannonSide;
        public bool PortalFloorApproached, ApproachingPortalFloor;
        public JsonObject? LaunchIdentity;
    }

    private readonly ConditionalWeakTable<Active, TransportState> transportStates = new();
    private readonly ConditionalWeakTable<Active, JsonObject> cannonLaunchReceipts = new();

    /// <summary>Receipt exists only after a verified native launch from this action's ordinary fire edge.</summary>
    public JsonObject? CannonLaunchReceipt(RouteActionHandle handle) => ReferenceEquals(handle.Owner, this) &&
        cannonLaunchReceipts.TryGetValue(handle.Action, out var receipt) ? receipt.DeepClone().AsObject() : null;

    // The transport action emits only ordinary chef inputs. All positions below
    // are navigation targets derived from measured geometry, never position edits.
    // The native state, not elapsed nominal flight time, establishes completion.
    private void Transport(Active action, JsonNode state, JsonObject input, string type)
    {
        var transport = transportStates.GetValue(action, _ => new TransportState());
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        RequireControlTelemetry(chef);
        if (Flag(chef, "respawning")) throw new InvalidOperationException("Transport chef is respawning; the route has lost its expected state.");
        if (action.Stage == "resolve") InitializeTransport(action, transport, state, type);

        if (type == "portal")
        {
            PortalTransport(action, transport, state, input);
            return;
        }

        // A waiting passenger must not receive navigation or dash input while
        // inside the cannon, even if a fire action on another chef has begun.
        if (type == "board-cannon" && Boarded(transport, state))
        {
            RequireHeld(state, player, transport.InitialHeld, "boarding");
            CompleteTransport(action, transport, state, "native cannon contains this chef");
            return;
        }
        if (action.Stage is "transport-navigate" or "transport-face" && FreeControl(chef) &&
            TransportNavigationSettled(transport, chef) &&
            NativeTransportTargetVerified(type, transport, state, chef))
        {
            // A currently observed native target is stronger than an inferred
            // approach point. This also avoids needless movement when another
            // action has already brought the chef to the correct interaction.
            Stage(action, "transport-verify");
            trace?.Event("transportNativeTarget", new JsonObject { ["player"] = player,
                ["type"] = type, ["interactionId"] = transport.Interaction.EntityId });
        }
        if (action.Stage is "transport-navigate" or "transport-face")
        {
            if (!FreeControl(chef)) return;
            if (type == "aim-cannon" && Held(state, player) != 0) throw new InvalidOperationException("Aiming a cannon requires empty hands.");
            RequireHeld(state, player, transport.InitialHeld, "approaching transport station");
            if (ApproachTransport(action, transport, state, input)) Stage(action, "transport-verify");
            return;
        }

        switch (type)
        {
            case "aim-cannon": AimCannon(action, transport, state, input); break;
            case "board-cannon": BoardCannon(action, transport, state, input); break;
            case "fire-cannon": FireCannon(action, transport, state, input); break;
            default: throw new ArgumentException("Unknown transport action: " + type);
        }
    }

    private void InitializeTransport(Active action, TransportState transport, JsonNode state, string type)
    {
        transport.Model = KitchenModel.Build(state.AsObject());
        int player = Player(action.Spec);
        transport.ChefEntityId = IdOf(Chef(state, player), "entityId");
        transport.InitialHeld = Held(state, player);
        transport.SourceRegion = transport.Model.RegionAt(ChefPosition(state, player));
        if (transport.ChefEntityId == 0 || transport.SourceRegion is null)
            throw new InvalidOperationException("Transport requires a chef on a measured platform.");
        if (type == "portal")
        {
            string selector = action.Spec["station"]?.ToString() ?? "portal";
            transport.Source = SelectStation(transport.Model, state, selector, "portal", player);
            transport.Interaction = transport.Source;
            var portal = RequireEntity(state, transport.Source.EntityId);
            if (IdOf(portal, "portalSenderCount") <= 0) throw new InvalidOperationException("Selected portal has no native sender.");
            transport.DestinationId = IdOf(portal, "portalDestinationId");
            var destination = RequireEntity(state, transport.DestinationId);
            if (IdOf(destination, "portalReceiverCount") <= 0) throw new InvalidOperationException("Native portal destination has no receiver.");
            transport.DestinationRegion = LandingRegion(transport.Model, KitchenModel.Position(destination["position"]));
        }
        else
        {
            string selector = (action.Spec["cannon"] ?? action.Spec["station"])?.ToString() ?? "cannon";
            transport.Source = SelectCannon(transport.Model, state, selector, player);
            var cannon = RequireEntity(state, transport.Source.EntityId);
            RequireProperty(cannon, "cannonState");
            RequireProperty(cannon, "cannonLoadedEntityId");
            if (type == "aim-cannon")
            {
                if (transport.InitialHeld != 0) throw new InvalidOperationException("aim-cannon requires empty hands.");
                transport.Interaction = transport.Model.Stations.SingleOrDefault(s => s.Role == "cannon-control" &&
                    IdOf(Entity(state, s.EntityId), "controlTargetId") == transport.Source.EntityId)
                    ?? throw new InvalidOperationException("No native controlTargetId links a terminal to this cannon; update telemetry.");
                double start = RequiredNumber(cannon, "cannonStartAngle"), min = RequiredNumber(cannon, "cannonMinAngle"), max = RequiredNumber(cannon, "cannonMaxAngle");
                if (max <= min || max - min > 90) throw new InvalidOperationException("Native cannon angle limits are inconsistent.");
                transport.TargetAngle = action.Spec["targetAngle"] is { } requested ? Number(requested) :
                    Math.Cos(min * Math.PI / 180) < Math.Cos(max * Math.PI / 180) ? min : max;
                double relative = AngleDifference(transport.TargetAngle, start);
                if (relative < min - start - .01 || relative > max - start + .01)
                    throw new ArgumentException("Requested cannon angle lies outside its native limits.");
                if (Math.Abs(Math.Sin(start * Math.PI / 180)) < .1)
                    throw new InvalidOperationException("This cannon cannot be aimed reliably with the verified Carnival Y-axis convention.");
            }
            else if (type == "fire-cannon")
            {
                int button = IdOf(cannon, "cannonButtonEntityId");
                transport.Interaction = transport.Model.Stations.SingleOrDefault(s => s.EntityId == button && s.Role == "cannon-switch")
                    ?? throw new InvalidOperationException("Cannon has no observed native cannonButtonEntityId link; update telemetry.");
                // FindNearbyObjects scans world use targets only with empty
                // hands; a held usable item instead owns the use interaction.
                if (transport.InitialHeld != 0)
                    throw new InvalidOperationException("Put down the carried item before using the cannon fire button.");
            }
            else transport.Interaction = transport.Source;
        }
        if (type == "portal" && transport.DestinationRegion is null)
            throw new InvalidOperationException("Native portal receiver does not map to a measured destination platform.");
        ValidateDestinationExpectation(action, transport.DestinationRegion);
        Stage(action, "transport-navigate");
        trace?.Event("transportResolved", new JsonObject
        {
            ["player"] = player, ["type"] = type, ["sourceId"] = transport.Source.EntityId,
            ["interactionId"] = transport.Interaction.EntityId, ["destinationId"] = transport.DestinationId,
            ["destinationRegion"] = transport.DestinationRegion, ["targetAngle"] = type == "aim-cannon" ? transport.TargetAngle : null
        });
    }

    private static KitchenStation SelectStation(KitchenModel model, JsonNode state, string selector, string role, int player)
    {
        var options = model.Candidates(selector).Where(s => s.Role == role).ToArray();
        if (options.Length == 0) throw new ArgumentException("No measured " + role + " matches " + selector);
        string? region = model.RegionAt(ChefPosition(state, player));
        var reachable = options.Where(s => s.Regions.Contains(region) || LandingRegion(model, s.Position) == region).ToArray();
        if (reachable.Length == 0) throw new InvalidOperationException("Selected " + role + " is not on this chef's platform.");
        return reachable.OrderBy(s => s.Position.Distance(ChefPosition(state, player))).ThenBy(s => s.Key, StringComparer.Ordinal).First();
    }

    private static KitchenStation SelectCannon(KitchenModel model, JsonNode state, string selector, int player)
    {
        var cannons = model.Stations.Where(s => s.Role == "cannon").OrderBy(s => s.Position.X).ToArray();
        if (selector is "left" or "right")
        {
            if (cannons.Length != 2) throw new InvalidOperationException("Side selection requires exactly two measured Carnival cannons.");
            return selector == "left" ? cannons[0] : cannons[1];
        }
        var options = model.Candidates(selector);
        var direct = options.Where(s => s.Role == "cannon").ToArray();
        if (direct.Length > 0) return direct.OrderBy(s => s.Position.Distance(ChefPosition(state, player))).First();
        if (options.Length == 1 && options[0].Role == "cannon-control")
        {
            int target = IdOf(Entity(state, options[0].EntityId), "controlTargetId");
            return cannons.SingleOrDefault(c => c.EntityId == target) ?? throw new InvalidOperationException("Terminal native cannon link is absent.");
        }
        if (options.Length == 1 && options[0].Role == "cannon-switch")
            return cannons.SingleOrDefault(c => IdOf(Entity(state, c.EntityId), "cannonButtonEntityId") == options[0].EntityId)
                ?? throw new InvalidOperationException("Switch native cannon link is absent.");
        throw new ArgumentException("Select one cannon, linked terminal, or linked fire button.");
    }

    private bool ApproachTransport(Active action, TransportState transport, JsonNode state, JsonObject input)
    {
        if (action.Stage == "transport-face")
        {
            // Native movement normalizes even a small stick vector. Turn with
            // one input frame, then release while its queued velocity settles.
            if (action.Frames - action.StageStarted == 1) Face(transport.Navigation!, state, input);
            return action.Frames - action.StageStarted >= (action.Spec["faceFrames"]?.GetValue<int>() ?? 3);
        }
        if (!EnsureTransportNavigation(action, transport, state)) return false;
        transport.Navigation!.Frames = action.Frames;
        bool arrived = DriveNavigation(transport.Navigation, state, input);
        action.Motion = transport.Navigation.Motion;
        if (arrived && transport.ClearingCannonSide)
        {
            transport.CannonSideCleared = true;
            transport.ClearingCannonSide = false;
            transport.Navigation = null;
            action.Motion = null;
            trace?.Event("cannonSideCleared", new JsonObject { ["player"] = Player(action.Spec), ["sourceId"] = transport.Source.EntityId });
        }
        else if (arrived) Stage(action, "transport-face");
        return false;
    }

    private bool EnsureTransportNavigation(Active action, TransportState transport, JsonNode state)
    {
        if (transport.Navigation is not null) return true;
        var navigationSpec = action.Spec.DeepClone().AsObject();
        navigationSpec["type"] = "navigate";
        navigationSpec.Remove("targetEntityId");
        navigationSpec.Remove("target");
        navigationSpec.Remove("offset");
        SetTransportDashFlags(action.Spec, navigationSpec,
            transport.Interaction.Role == "cannon-switch" && transport.InitialHeld == 0 && transport.Retries == 0);
        navigationSpec["tolerance"] = .06;
        navigationSpec["stableFrames"] = 2;
        if (transport.Interaction.Role is "cannon" or "portal")
        {
            var point = TransitApproach(action, transport, state);
            if (point is null) return false;
            if (transport.Interaction.Role == "portal" && !transport.PortalFloorApproached &&
                PortalFloorApproach(action, transport, state, point.Value) is { } floorPoint)
            {
                point = floorPoint;
                transport.ApproachingPortalFloor = true;
                SetTransportDashFlags(action.Spec, navigationSpec, true);
            }
            if (transport.Interaction.Role == "cannon" && !transport.CannonSideCleared)
            {
                // A carried plate projects in front of the chef and can hit the
                // cannon base while a diagonal path hugs the adjacent counter.
                // Move into its open inner lane first, then approach in that lane.
                Point2 current = ChefPosition(state, Player(action.Spec));
                if (Math.Abs(current.X - point.Value.X) <= .06) transport.CannonSideCleared = true;
                else
                {
                    var liveModel = KitchenModel.Build(state.AsObject());
                    var others = OtherChefs(state, Player(action.Spec), liveModel, false);
                    var projected = new Point2(point.Value.X, current.Z);
                    var staging = CannonSideApproach(liveModel, current, point.Value, transport.SourceRegion!, others);
                    if (staging is null) return false;
                    if (staging.Value != projected)
                        trace?.Event("cannonSideApproachAdjusted", new JsonObject { ["player"] = Player(action.Spec),
                            ["sourceId"] = transport.Source.EntityId,
                            ["projected"] = new JsonObject { ["x"] = projected.X, ["z"] = projected.Z },
                            ["target"] = new JsonObject { ["x"] = staging.Value.X, ["z"] = staging.Value.Z },
                            ["reason"] = "The horizontal projection is blocked; use a reachable point on the inner approach lane." });
                    point = staging;
                    transport.ClearingCannonSide = true;
                }
            }
            navigationSpec.Remove("station");
            navigationSpec["target"] = new JsonObject { ["x"] = point.Value.X, ["z"] = point.Value.Z };
        }
        else navigationSpec["station"] = transport.Interaction.Key;
        var navigation = new Active(navigationSpec) { Frames = action.Frames, Retries = transport.Retries };
        if (!InitializeMotion(navigation, state)) return false;
        navigation.Motion!.Station = transport.Interaction;
        transport.Navigation = navigation;
        action.Motion = navigation.Motion;
        return true;
    }

    private static Point2? CannonSideApproach(KitchenModel model, Point2 current, Point2 entry,
        string region, IReadOnlyList<KitchenObstacle> others)
    {
        bool Safe(Point2 p) => Navigation.IsWalkable(model, p, region, others) &&
            Navigation.SegmentClear(model, p, entry, region, others) &&
            Navigation.FindPath(model, current, p, .15, others).Success;
        // A chef emerging from the upper return portal can be level with the
        // ingredient crates. Projecting that Z onto the cannon's inner lane
        // puts the staging target inside a crate. Keep the old projection when
        // valid; otherwise step toward the already validated cannon approach.
        var projected = new Point2(entry.X, current.Z);
        if (Safe(projected)) return projected;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(entry.Z - current.Z) / .15));
        for (int step = 1; step <= steps; step++)
        {
            var candidate = new Point2(entry.X, current.Z + (entry.Z - current.Z) * step / steps);
            if (Safe(candidate)) return candidate;
        }
        return null;
    }

    public static int CannonSideApproachSelfTest(JsonObject fixture)
    {
        var state = KitchenModel.SnapshotState(fixture.DeepClone().AsObject());
        var runner = new RouteRunner(_ => throw new InvalidOperationException("Fixture attempted game I/O."), null);
        var action = new Active(new JsonObject { ["type"] = "board-cannon", ["cannon"] = "left", ["player"] = 2 });
        var transit = new TransportState();
        runner.InitializeTransport(action, transit, state, "board-cannon");
        var model = KitchenModel.Build(state);
        var current = ChefPosition(state, 2);
        var entry = runner.TransitApproach(action, transit, state)!.Value;
        var others = OtherChefs(state, 2, model, false);
        string region = model.RegionAt(current)!;
        int checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("Cannon side regression: " + label); checks++; }
        var old = new Point2(entry.X, current.Z);
        Check(!Navigation.IsWalkable(model, old, region, others), "actual portal-height projection is obstructed");
        var target = CannonSideApproach(model, current, entry, region, others);
        Check(target is not null && target.Value != old, "selects a different staging point");
        Check(target!.Value.X == entry.X, "retains the measured inner lane");
        Check(Navigation.IsWalkable(model, target.Value, region, others), "target retains all physical clearance");
        Check(Navigation.FindPath(model, current, target.Value, .15, others).Success, "actual chef can reach staging");
        Check(Navigation.SegmentClear(model, target.Value, entry, region, others), "final lane is clear");
        Check(runner.EnsureTransportNavigation(action, transit, state), "actual transport initializes native-input navigation");
        Check(transit.ClearingCannonSide && transit.Navigation?.Motion is not null, "retains two-leg boarding");
        var ordinary = new Point2(12, -13.2);
        var ordinaryProjection = new Point2(entry.X, ordinary.Z);
        Check(CannonSideApproach(model, ordinary, entry, region, others) == ordinaryProjection, "preserves valid prior projection");
        var block = new KitchenObstacle("test-lane-block", -1,
            new Rect2(entry.X - 1, entry.X + 1, model.Regions.Single(r => r.Id == region).FloorBounds.MinZ,
                model.Regions.Single(r => r.Id == region).FloorBounds.MaxZ), "Offline blocked-lane fixture");
        Check(CannonSideApproach(model, current, entry, region, others.Append(block).ToArray()) is null,
            "does not force a target through an obstructed lane");
        Check(KitchenModel.SnapshotState(fixture)["chefs"]!.ToJsonString() == state["chefs"]!.ToJsonString(),
            "fixture chef state is unchanged");
        return checks;
    }

    private static void SetTransportDashFlags(JsonObject parent, JsonObject child, bool eligibleLeg)
    {
        bool enabled = eligibleLeg && parent["dash"]?.GetValue<bool>() == true;
        child["dash"] = enabled;
        child["shortDash"] = enabled && parent["shortDash"]?.GetValue<bool>() == true;
    }

    private static bool TransportNavigationSettled(TransportState transport, JsonObject chef)
    {
        // A native use target may appear during dash inertia. Keep executing the
        // child navigation until its dash and queued velocity settle, instead
        // of abandoning its coast handler to press an interaction early.
        if (transport.Navigation?.Motion?.Dash is not null || Number(chef["dashTimer"]) > 0) return false;
        return transport.Navigation?.Spec["dash"]?.GetValue<bool>() != true ||
            KitchenModel.Position(chef["lastVelocity"]).Distance(default) <= .01;
    }

    private static Point2? PortalFloorApproach(Active action, TransportState transport, JsonNode state, Point2 entry)
    {
        if (action.Spec["dash"]?.GetValue<bool>() != true || transport.InitialHeld != 0 || transport.Retries != 0) return null;
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        if (!NativeDashProfile.TryRead(chef, out var profile, out _)) return null;
        Point2 current = ChefPosition(state, player);
        double distance = current.Distance(entry), cached = KitchenModel.Position(chef["lastVelocity"]).Distance(default);
        double required = action.Spec["shortDash"]?.GetValue<bool>() == true
            ? profile!.ShortCoastTravelBound(cached) : profile!.FullTravelBound(cached);
        // Keep the final 1.2 units of entry walking. The first leg is useful
        // only if it also has room for native dash travel and normal margins.
        const double walkingEntry = 1.2;
        double margin = Math.Max(.15, Number(action.Spec["dashSafetyMargin"]));
        if (distance < walkingEntry + required + margin) return null;
        Point2 staging = new(entry.X + (current.X - entry.X) * walkingEntry / distance,
            entry.Z + (current.Z - entry.Z) * walkingEntry / distance);
        var model = transport.Model;
        string? region = model.RegionAt(current);
        var portal = RequireEntity(state, transport.Source.EntityId);
        var trigger = (portal["colliders"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(c => Flag(c, "enabled") && Flag(c, "trigger"));
        if (trigger is null || region is null) return null;
        Point2 center = KitchenModel.Position(trigger["center"]), size = KitchenModel.Position(trigger["size"]);
        double outside = size.Distance(default) / 2 + model.ChefRadius + .25;
        var others = OtherChefs(state, player, model, false);
        if (staging.Distance(center) < outside ||
            !Navigation.SegmentClear(model, current, staging, region, others) ||
            !Navigation.SegmentClear(model, staging, entry, region, others)) return null;
        return staging;
    }

    private Point2? TransitApproach(Active action, TransportState transport, JsonNode state)
    {
        int player = Player(action.Spec);
        var model = transport.Model;
        var source = RequireEntity(state, transport.Interaction.EntityId);
        string? region = model.RegionAt(ChefPosition(state, player));
        if (region != transport.SourceRegion) throw new InvalidOperationException("Chef changed platform before entering transport.");
        var collider = (source["colliders"] as JsonArray)?.OfType<JsonObject>()
            .Where(c => Flag(c, "enabled") && Flag(c, "trigger"))
            .OrderByDescending(c => c["type"]?.ToString() == "CapsuleCollider").FirstOrDefault()
            ?? throw new InvalidOperationException("Transport entry trigger geometry is missing.");
        Point2 center = KitchenModel.Position(collider["center"]), size = KitchenModel.Position(collider["size"]);
        var dynamic = OtherChefs(state, player, model, action.Spec["ignoreChefs"]?.GetValue<bool>() == true);
        var candidates = new List<Point2>();
        // Cannons sit at platform corners, where the generic axis-only station
        // approaches can be empty. Sample safe diagonal approaches around the
        // actual trigger, retaining the normal navigation collision checks.
        for (int ix = -34; ix <= 34; ix++)
        for (int iz = -34; iz <= 34; iz++)
        {
            Point2 point = new(center.X + ix * .05, center.Z + iz * .05);
            double distance = point.Distance(center);
            if (transport.Interaction.Role == "portal")
            {
                double overlapDistance = Math.Min(size.X, size.Z) / 2 + model.ChefRadius - .025;
                if (distance > overlapDistance || !WithinPortalArc(source, center, point)) continue;
            }
            else
            {
                // Cannon interaction is an overlap query in the chef's forward
                // arc (native radius 1), not physical entry into the root trigger.
                // The aimed solid barrel can extend beyond that trigger, so keep
                // its collision margin and approach within interaction reach.
                double dx = Math.Max(0, Math.Abs(point.X - center.X) - size.X / 2);
                double dz = Math.Max(0, Math.Abs(point.Z - center.Z) - size.Z / 2);
                if (dx * dx + dz * dz > .8 * .8) continue;
                double inward = Math.Sign(center.X - transport.Source.Position.X);
                double sideDistance = (point.X - center.X) * inward - size.X / 2;
                if (inward == 0 || sideDistance < .72 || sideDistance > .8 || Math.Abs(point.Z - center.Z) > .075) continue;
            }
            if (Navigation.IsWalkable(model, point, region!, dynamic)) candidates.Add(point);
        }
        var ordered = candidates.OrderBy(p => p.Distance(ChefPosition(state, player))).ThenBy(p => p.Distance(center)).ToArray();
        if (ordered.Length == 0) throw new InvalidOperationException("Measured platform/collider geometry has no safe transport entry approach.");
        int skip = Math.Min(transport.Retries * 3, ordered.Length - 1);
        foreach (var point in ordered.Skip(skip).Concat(ordered.Take(skip)).Take(24))
        {
            var path = Navigation.FindPath(model, ChefPosition(state, player), point, .15, dynamic);
            if (path.Success) return point;
        }
        if (action.Frames % 30 == 0) trace?.Event("transportWaiting", new JsonObject { ["reason"] = "No collision-free path to measured entry trigger", ["sourceId"] = transport.Source.EntityId });
        return null;
    }

    private void AimCannon(Active action, TransportState transport, JsonNode state, JsonObject input)
    {
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        var cannon = RequireEntity(state, transport.Source.EntityId);
        RequireHeld(state, player, 0, "aiming");
        if (action.Stage == "transport-verify")
        {
            if (IdOf(chef, "useTargetId") == transport.Interaction.EntityId && FreeControl(chef))
            {
                input["use"] = true;
                Stage(action, "await-terminal");
            }
            else RetryAfterTargetWait(action, transport, state, input, "terminal use target is not verified");
            return;
        }
        if (action.Stage == "await-terminal")
        {
            if (!Flag(chef, "controlsEnabled") && IdOf(chef, "interactingEntityId") == transport.Interaction.EntityId)
            {
                transport.LastAngle = RequiredNumber(cannon, "cannonAngle");
                transport.LastAngleFrame = action.Frames;
                Stage(action, "aiming");
            }
            else if (action.Frames - action.StageStarted > 30) RetryTransport(action, transport, "native terminal session did not begin");
            return;
        }
        if (action.Stage == "aiming")
        {
            if (Flag(chef, "controlsEnabled") || IdOf(chef, "interactingEntityId") != transport.Interaction.EntityId)
                throw new InvalidOperationException("Chef lost the native cannon terminal session while aiming.");
            double angle = RequiredNumber(cannon, "cannonAngle"), error = AngleDifference(transport.TargetAngle, angle);
            if (Math.Abs(error) <= (action.Spec["angleTolerance"]?.GetValue<double>() ?? .2))
            {
                action.StableFrames++;
                if (action.StableFrames >= 3) Stage(action, "exit-terminal");
                return;
            }
            action.StableFrames = 0;
            if (Math.Abs(AngleDifference(angle, transport.LastAngle)) > .01)
            {
                transport.LastAngle = angle;
                transport.LastAngleFrame = action.Frames;
            }
            if (action.Frames - transport.LastAngleFrame > 90) throw new InvalidOperationException("Native cannon aim did not advance under terminal input.");
            double sign = Math.Sign(Math.Sin(RequiredNumber(cannon, "cannonStartAngle") * Math.PI / 180));
            input["y"] = Math.Sign(error) * sign;
            return;
        }
        if (action.Stage == "exit-terminal")
        {
            // SessionBase exits on a new press, not on release. The preceding
            // stable-angle frames were neutral, so this is an explicit edge.
            input["use"] = true;
            Stage(action, "await-terminal-exit");
            return;
        }
        if (action.Stage == "await-terminal-exit")
        {
            bool restored = FreeControl(chef) && IdOf(chef, "interactingEntityId") != transport.Interaction.EntityId;
            if (restored && Math.Abs(AngleDifference(transport.TargetAngle, RequiredNumber(cannon, "cannonAngle"))) <= (action.Spec["angleTolerance"]?.GetValue<double>() ?? .2))
            {
                if (++action.StableFrames >= 2) CompleteTransport(action, transport, state, "native angle reached and terminal session ended");
            }
            else action.StableFrames = 0;
            if (action.Frames - action.StageStarted > 90) throw new InvalidOperationException("Chef did not regain direct control after leaving the terminal.");
        }
    }

    private void BoardCannon(Active action, TransportState transport, JsonNode state, JsonObject input)
    {
        int player = Player(action.Spec);
        RequireHeld(state, player, transport.InitialHeld, "boarding");
        var chef = Chef(state, player);
        var cannon = RequireEntity(state, transport.Source.EntityId);
        if (action.Stage == "transport-verify")
        {
            if (Flag(cannon, "cannonFlying") || cannon["cannonState"]?.ToString() == "Load") return;
            var allowed = InteractionTargets(state, transport.Source.EntityId);
            int pickup = IdOf(chef, "pickupTargetId"), use = IdOf(chef, "useTargetId"), placement = IdOf(chef, "placementTargetId");
            // Empty hands use the native UsePlacementButton fallback. A nearby
            // pickup target takes precedence, so do not press if it points elsewhere.
            bool target = transport.InitialHeld == 0 ? allowed.Contains(use) && (pickup == 0 || allowed.Contains(pickup)) : allowed.Contains(placement);
            if (target && FreeControl(chef))
            {
                input["pickup"] = true;
                Stage(action, "await-cannon-load");
            }
            else RetryAfterTargetWait(action, transport, state, input, "cannon placement target is not verified");
            return;
        }
        if (action.Stage == "await-cannon-load" && action.Frames - action.StageStarted > 30)
        {
            if (!FreeControl(chef)) throw new InvalidOperationException("Chef became disabled without this cannon confirming its load.");
            RetryTransport(action, transport, "cannon did not load chef after the verified pickup edge");
        }
    }

    private static bool Boarded(TransportState transport, JsonNode state)
    {
        var cannon = RequireEntity(state, transport.Source.EntityId);
        var chef = ChefByEntity(state, transport.ChefEntityId);
        return IdOf(cannon, "cannonLoadedEntityId") == transport.ChefEntityId && cannon["cannonState"]?.ToString() == "Load"
            && !Flag(cannon, "cannonFlying") && !Flag(chef, "controlsEnabled") && IdOf(chef, "interactingEntityId") == transport.Source.EntityId;
    }

    private static bool NativeTransportTargetVerified(string type, TransportState transport, JsonNode state, JsonObject chef)
    {
        if (type is "aim-cannon" or "fire-cannon")
            return IdOf(chef, "heldEntityId") == 0 && IdOf(chef, "useTargetId") == transport.Interaction.EntityId;
        if (type != "board-cannon") return false;
        var allowed = InteractionTargets(state, transport.Source.EntityId);
        if (IdOf(chef, "heldEntityId") != 0) return allowed.Contains(IdOf(chef, "placementTargetId"));
        int pickup = IdOf(chef, "pickupTargetId");
        return allowed.Contains(IdOf(chef, "useTargetId")) && (pickup == 0 || allowed.Contains(pickup));
    }

    private void FireCannon(Active action, TransportState transport, JsonNode state, JsonObject input)
    {
        int player = Player(action.Spec);
        RequireHeld(state, player, transport.InitialHeld, "firing");
        var chef = Chef(state, player);
        var cannon = RequireEntity(state, transport.Source.EntityId);
        if (action.Stage == "transport-verify")
        {
            // m_loadedObject and m_readyToLaunch remain populated after a flight.
            // Load state and a disabled passenger distinguish an occupied cannon.
            if (!Flag(cannon, "cannonReady") || Flag(cannon, "cannonFlying") || cannon["cannonState"]?.ToString() != "Load") return;
            int passengerId = IdOf(cannon, "cannonLoadedEntityId");
            var passenger = ChefByEntity(state, passengerId);
            RequireControlTelemetry(passenger);
            if (Flag(passenger, "controlsEnabled") || IdOf(passenger, "interactingEntityId") != transport.Source.EntityId) return;
            if (action.Spec["passengerPlayer"] is { } expectedPlayer && IdOf(passenger, "playerId") != expectedPlayer.GetValue<int>())
                throw new InvalidOperationException("Cannon contains a different chef from the expected passenger.");
            if (IdOf(chef, "useTargetId") != transport.Interaction.EntityId || !FreeControl(chef))
            {
                RetryAfterTargetWait(action, transport, state, input, "cannon fire button use target is not verified");
                return;
            }
            transport.DestinationRegion = LandingRegion(transport.Model, KitchenModel.Position(RequireProperty(cannon, "cannonTarget")));
            if (transport.DestinationRegion is null) throw new InvalidOperationException("Native cannon target lies between measured platforms; aim before firing.");
            ValidateDestinationExpectation(action, transport.DestinationRegion);
            transport.PassengerId = passengerId;
            transport.PassengerHeld = IdOf(passenger, "heldEntityId");
            if (Flag(action.Spec, "completeOnLaunch"))
                transport.LaunchIdentity = CaptureCannonLaunchIdentity(action, transport, state);
            input["use"] = true;
            Stage(action, "await-cannon-launch");
            return;
        }
        if (action.Stage == "await-cannon-launch")
        {
            if (IdOf(cannon, "cannonLoadedEntityId") == transport.PassengerId && cannon["cannonState"]?.ToString() == "Launched")
            {
                if (Flag(action.Spec, "completeOnLaunch"))
                {
                    // Launched IDs persist after landing. This opt-in early
                    // completion additionally requires an actual flying edge.
                    if (!Flag(cannon, "cannonFlying"))
                    {
                        if (action.Frames - action.StageStarted > 30)
                            throw new InvalidOperationException("Early cannon completion never observed native flight after its fire edge.");
                        return;
                    }
                    var identity = transport.LaunchIdentity ?? throw new InvalidOperationException("Cannon launch has no captured passenger identity.");
                    var current = CaptureCannonLaunchIdentity(action, transport, state);
                    foreach (string field in new[] { "cannon", "cannonOrdinal", "button", "buttonOrdinal", "passenger", "passengerOrdinal", "passengerPlayer", "held", "heldOrdinal", "destinationRegion" })
                        if (!JsonNode.DeepEquals(identity[field], current[field])) throw new InvalidOperationException("Cannon launch changed captured " + field + ".");
                    if (KitchenModel.Position(identity["landingTarget"]).Distance(KitchenModel.Position(current["landingTarget"])) > .0001)
                        throw new InvalidOperationException("Native cannon destination changed after the fire edge.");
                    var receipt = identity.DeepClone().AsObject(); receipt["launchFrame"] = RequireProperty(state.AsObject(), "gameplayFrame").DeepClone();
                    receipt["launchClientTime"] = RequireProperty(state.AsObject(), "clientTime").DeepClone();
                    receipt["nativeFlying"] = true; receipt["completion"] = "launch-confirmed-arrival-still-pending";
                    cannonLaunchReceipts.Add(action, receipt);
                    trace?.Event("transportLaunchConfirmed", receipt.DeepClone().AsObject());
                    CompleteTransport(action, transport, state, "native launch confirmed; firing input released, caller retains passenger arrival obligation");
                    return;
                }
                transport.TransitionObserved = true;
                Stage(action, "await-cannon-arrival");
            }
            else if (action.Frames - action.StageStarted > 30)
            {
                if (cannon["cannonState"]?.ToString() != "Load") throw new InvalidOperationException("Cannon left Load state without a confirmed launch.");
                RetryTransport(action, transport, "cannon did not launch after verified fire edge");
            }
            return;
        }
        if (action.Stage == "await-cannon-arrival")
        {
            var passenger = ChefByEntity(state, transport.PassengerId);
            if (Flag(passenger, "respawning")) throw new InvalidOperationException("Cannon passenger is respawning instead of arriving.");
            if (IdOf(passenger, "heldEntityId") != transport.PassengerHeld) throw new InvalidOperationException("Cannon passenger lost or exchanged the carried item.");
            if (transport.TransitionObserved && !Flag(cannon, "cannonFlying") && Arrived(action, transport, passenger))
            {
                if (++action.StableFrames >= 2) CompleteTransport(action, transport, state, "native launch finished and passenger regained control on destination platform");
            }
            else action.StableFrames = 0;
        }
    }

    private static JsonObject CaptureCannonLaunchIdentity(Active action, TransportState transport, JsonNode state)
    {
        var cannon = RequireEntity(state, transport.Source.EntityId);
        var passenger = ChefByEntity(state, transport.PassengerId);
        if (Flag(passenger, "respawning") || IdOf(passenger, "heldEntityId") != transport.PassengerHeld)
            throw new InvalidOperationException("Cannon launch passenger respawned or changed its carried item.");
        int Ordinal(int id) => (int)RequiredNumber(RequireEntity(state, id), "observedOrdinal");
        return new JsonObject { ["cannon"] = transport.Source.EntityId, ["cannonOrdinal"] = Ordinal(transport.Source.EntityId),
            ["button"] = transport.Interaction.EntityId, ["buttonOrdinal"] = Ordinal(transport.Interaction.EntityId),
            ["passenger"] = transport.PassengerId, ["passengerOrdinal"] = Ordinal(transport.PassengerId),
            ["passengerPlayer"] = IdOf(passenger, "playerId"), ["held"] = transport.PassengerHeld,
            ["heldOrdinal"] = transport.PassengerHeld == 0 ? -1 : Ordinal(transport.PassengerHeld),
            ["destinationRegion"] = transport.DestinationRegion, ["landingTarget"] = RequireProperty(cannon, "cannonTarget").DeepClone(),
            ["fireEdgeFrame"] = RequireProperty(state.AsObject(), "gameplayFrame").DeepClone(), ["firingPlayer"] = Player(action.Spec) };
    }

    private void PortalTransport(Active action, TransportState transport, JsonNode state, JsonObject input)
    {
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        RequireHeld(state, player, transport.InitialHeld, "portal transit");
        var portal = RequireEntity(state, transport.Source.EntityId);
        var receiver = RequireEntity(state, transport.DestinationId);
        string? region = transport.Model.RegionAt(ChefPosition(state, player));
        bool disabled = !Flag(chef, "controlsEnabled");
        if (!transport.TransitionObserved && disabled && (Flag(portal, "portalTeleporting") || Flag(receiver, "portalReceiving") || region != transport.SourceRegion))
        {
            transport.TransitionObserved = true;
            Stage(action, "await-portal-arrival");
        }
        if (transport.TransitionObserved)
        {
            if (Arrived(action, transport, chef))
            {
                if (++action.StableFrames >= 2) CompleteTransport(action, transport, state, "native portal transit finished and chef regained control on linked platform");
            }
            else action.StableFrames = 0;
            return;
        }
        if (!FreeControl(chef)) return;
        if (action.Stage == "transport-navigate")
        {
            if (!EnsureTransportNavigation(action, transport, state)) return;
            transport.Navigation!.Frames = action.Frames;
            bool arrived = DriveNavigation(transport.Navigation, state, input);
            action.Motion = transport.Navigation.Motion;
            if (arrived && transport.ApproachingPortalFloor)
            {
                transport.PortalFloorApproached = true;
                transport.ApproachingPortalFloor = false;
                transport.Navigation = null;
                action.Motion = null;
                trace?.Event("portalFloorApproached", new JsonObject { ["player"] = player,
                    ["sourceId"] = transport.Source.EntityId, ["remainingEntry"] = "walking" });
            }
            else if (arrived) Stage(action, "await-portal-entry");
            return;
        }
        if (action.Stage == "await-portal-entry" && action.Frames - action.StageStarted > 20)
            RetryTransport(action, transport, "chef reached entry overlap but native portal has not accepted it");
    }

    private static bool Arrived(Active action, TransportState transport, JsonObject chef)
    {
        return FreeControl(chef) && transport.Model.RegionAt(KitchenModel.Position(chef["position"])) == transport.DestinationRegion
            && Number(chef["position"]?["y"]) <= (action.Spec["maximumArrivalHeight"]?.GetValue<double>() ?? .9);
    }

    private void RetryAfterTargetWait(Active action, TransportState transport, JsonNode state, JsonObject input, string reason)
    {
        if (transport.Navigation is null)
        {
            RetryTransport(action, transport, reason);
            return;
        }
        if (action.Frames - action.StageStarted == 1) Face(transport.Navigation!, state, input);
        if (action.Frames - action.StageStarted >= 15) RetryTransport(action, transport, reason);
    }

    private void RetryTransport(Active action, TransportState transport, string reason)
    {
        if (++transport.Retries > (action.Spec["maxRetries"]?.GetValue<int>() ?? 6))
            throw new InvalidOperationException("Transport retries exhausted: " + reason);
        transport.Navigation = null;
        transport.CannonSideCleared = false;
        transport.ClearingCannonSide = false;
        transport.PortalFloorApproached = false;
        transport.ApproachingPortalFloor = false;
        action.Motion = null;
        Stage(action, "transport-navigate");
        trace?.Event("transportRetry", new JsonObject { ["player"] = Player(action.Spec), ["retry"] = transport.Retries, ["reason"] = reason });
    }

    private void CompleteTransport(Active action, TransportState transport, JsonNode state, string evidence)
    {
        action.Done = true;
        trace?.Event("transportComplete", new JsonObject { ["player"] = Player(action.Spec), ["sourceId"] = transport.Source.EntityId,
            ["passengerId"] = transport.PassengerId, ["destinationRegion"] = transport.DestinationRegion,
            ["evidence"] = evidence, ["frame"] = state["frame"]?.DeepClone() });
        transportStates.Remove(action);
    }

    private static void RequireHeld(JsonNode state, int player, int expected, string operation)
    {
        if (Held(state, player) != expected) throw new InvalidOperationException("Held item changed unexpectedly while " + operation + ".");
    }
    private static void RequireControlTelemetry(JsonObject chef)
    {
        RequireProperty(chef, "controlsEnabled");
        RequireProperty(chef, "directlyControlled");
        RequireProperty(chef, "canAcceptInput");
    }
    private static bool FreeControl(JsonObject chef) => Flag(chef, "controlsEnabled") && Flag(chef, "directlyControlled") && Flag(chef, "canAcceptInput")
        && !Flag(chef, "respawning") && !Flag(chef, "inputSuppressed");
    private static bool Flag(JsonNode? node, string field) => node?[field]?.GetValue<bool>() == true;
    private static int IdOf(JsonNode? node, string field) => node?[field]?.GetValue<int>() ?? 0;
    private static JsonNode RequireProperty(JsonObject node, string name) => node[name] ?? throw new InvalidOperationException("Native telemetry field is missing: " + name);
    private static double RequiredNumber(JsonObject node, string name) => Number(RequireProperty(node, name));
    private static JsonObject RequireEntity(JsonNode state, int id) => id > 0 ? Entity(state, id) ?? throw new InvalidOperationException("Transport entity " + id + " disappeared.") : throw new InvalidOperationException("Native transport entity link is absent.");
    private static JsonObject ChefByEntity(JsonNode state, int id) => (state["chefs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c => IdOf(c, "entityId") == id)
        ?? throw new InvalidOperationException("Transport passenger " + id + " is not an observed chef.");
    private static double AngleDifference(double target, double current) => ((target - current + 540) % 360 + 360) % 360 - 180;
    private static string? LandingRegion(KitchenModel model, Point2 target)
    {
        string? direct = model.RegionAt(target);
        if (direct is not null) return direct;
        // Native target transforms can differ from the rounded measured edge by
        // a few millimeters. This only labels the target, never moves the chef.
        var near = model.Regions.Where(r => new Point2(Math.Clamp(target.X, r.FloorBounds.MinX, r.FloorBounds.MaxX),
            Math.Clamp(target.Z, r.FloorBounds.MinZ, r.FloorBounds.MaxZ)).Distance(target) < .05).ToArray();
        return near.Length == 1 ? near[0].Id : null;
    }
    private static void ValidateDestinationExpectation(Active action, string? destination)
    {
        if (destination is not null && action.Spec["destinationRegion"] is { } expected && expected.ToString() != destination)
            throw new InvalidOperationException("Native transport points to " + destination + ", not the requested destination " + expected + ".");
    }
    private static bool WithinPortalArc(JsonObject portal, Point2 center, Point2 point)
    {
        var q = portal["rotation"] ?? throw new InvalidOperationException("Portal rotation is absent.");
        double x = Number(q["x"]), y = Number(q["y"]), z = Number(q["z"]), w = Number(q["w"]);
        Point2 right = new(1 - 2 * y * y - 2 * z * z, 2 * x * z - 2 * y * w);
        Point2 direction = new(point.X - center.X, point.Z - center.Z);
        double length = direction.Distance(default);
        return length > 0 && (right.X * direction.X + right.Z * direction.Z) / length >
            Math.Cos(RequiredNumber(portal, "portalArc") * Math.PI / 180);
    }

    /// <summary>Pure fixture checks: this clones telemetry and never connects to or advances the game.</summary>
    public static int TransportSelfTest(JsonObject fixture)
    {
        var state = KitchenModel.SnapshotState(fixture.DeepClone().AsObject());
        var runner = new RouteRunner(_ => throw new InvalidOperationException("Fixture tests must not call the game."), null);
        var model = KitchenModel.Build(state);
        var cannons = model.Stations.Where(s => s.Role == "cannon").OrderBy(s => s.Position.X).ToArray();
        var chef = Chef(state, 2);
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Transport fixture check failed: " + label);
            checks++;
        }
        void Free(JsonObject player)
        {
            player["controlsEnabled"] = true; player["directlyControlled"] = true; player["canAcceptInput"] = true;
            player["respawning"] = false; player["inputSuppressed"] = false; player["interactingEntityId"] = 0;
        }
        void Position(JsonObject player, double x, double z, double y = .05) =>
            player["position"] = new JsonObject { ["x"] = x, ["y"] = y, ["z"] = z };
        Active Action(string type, int player = 2) => new(new JsonObject { ["type"] = type, ["player"] = player, ["cannon"] = "left", ["ignoreChefs"] = true });
        JsonObject Tick(Active action)
        {
            var input = Inputs.Neutral(Player(action.Spec));
            runner.Transport(action, state, input, action.Spec["type"]!.ToString());
            action.Frames++;
            return input;
        }

        Free(chef); chef["heldEntityId"] = 0; Position(chef, 12, -12.5);
        var requestedDash = new JsonObject { ["dash"] = true, ["shortDash"] = true };
        var childDash = new JsonObject();
        SetTransportDashFlags(requestedDash, childDash, false);
        Check(!Flag(childDash, "dash") && !Flag(childDash, "shortDash"), "fine transport legs disable both inherited flags");
        SetTransportDashFlags(new JsonObject(), childDash, true);
        Check(!Flag(childDash, "dash") && !Flag(childDash, "shortDash"), "transport navigation defaults to walking");
        SetTransportDashFlags(new JsonObject { ["shortDash"] = true }, childDash, true);
        Check(!Flag(childDash, "dash"), "short modifier alone does not enable transport dash");

        foreach (string kind in new[] { "aim-cannon", "board-cannon", "fire-cannon" })
        {
            int player = kind == "fire-cannon" ? 3 : 2;
            var testChef = Chef(state, player); Free(testChef); testChef["heldEntityId"] = 0;
            Position(testChef, player == 3 ? 20.4 : 12, player == 3 ? -19.2 : -12.5);
            var action = Action(kind, player); action.Spec["dash"] = true; action.Spec["shortDash"] = true;
            var transit = new TransportState(); runner.InitializeTransport(action, transit, state, kind);
            Check(runner.EnsureTransportNavigation(action, transit, state), "fixture can initialize child navigation: " + kind);
            bool permitted = kind == "fire-cannon";
            Check(Flag(transit.Navigation!.Spec, "dash") == permitted && Flag(transit.Navigation.Spec, "shortDash") == permitted,
                "only fire-button child receives requested dash flags: " + kind);
            if (permitted)
            {
                testChef["dashTimer"] = .2;
                Check(!TransportNavigationSettled(transit, testChef), "native target cannot bypass active dash inertia");
                testChef["dashTimer"] = -.5;
                testChef["lastVelocity"] = new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 };
                Check(!TransportNavigationSettled(transit, testChef), "native target cannot bypass queued dash velocity");
                testChef["lastVelocity"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
                Check(TransportNavigationSettled(transit, testChef), "settled requested-dash navigation may use verified native target");
                transit.Navigation = null; transit.Retries = 1;
                Check(runner.EnsureTransportNavigation(action, transit, state) && !Flag(transit.Navigation!.Spec, "dash") &&
                    !Flag(transit.Navigation.Spec, "shortDash"), "target recovery retries walk");
            }
        }

        Position(chef, 26.7, -20.35);
        // The earliest kitchen fixture predates movement-profile telemetry.
        // Supply the separately verified native profile in this cloned state.
        foreach (var (field, value) in new (string, double)[] { ("runSpeed", 6), ("dashSpeed", 18),
            ("dashDuration", .3), ("dashCooldown", .4), ("movementScale", 1), ("maxSpeed", 18),
            ("surfaceSpeedMultiplier", 1), ("surfaceSlippiness", 0), ("surfaceSlidiness", 0) }) chef[field] = value;
        chef["groundNormal"] = new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 };
        chef["surfaceVelocity"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
        chef["windVelocity"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 };
        var floorAction = Action("portal"); floorAction.Spec.Remove("cannon");
        floorAction.Spec["dash"] = true; floorAction.Spec["shortDash"] = true;
        var floorTransit = new TransportState(); runner.InitializeTransport(floorAction, floorTransit, state, "portal");
        bool floorInitialized = runner.EnsureTransportNavigation(floorAction, floorTransit, state);
        Check(floorInitialized && floorTransit.ApproachingPortalFloor &&
            Flag(floorTransit.Navigation!.Spec, "dash") && Flag(floorTransit.Navigation.Spec, "shortDash"),
            "long empty-handed portal floor leg inherits both explicit flags; target=" + floorTransit.Navigation?.Motion?.Target +
            "; profile=" + NativeDashProfile.TryRead(chef, out var testedProfile, out var profileReason) + ":" + profileReason +
            "; required=" + testedProfile?.ShortCoastTravelBound(0));
        var floorTarget = floorTransit.Navigation!.Motion!.Target;
        var entry = runner.TransitApproach(floorAction, floorTransit, state)!.Value;
        floorTransit.InitialHeld = 10;
        Check(PortalFloorApproach(floorAction, floorTransit, state, entry) is null, "carried items keep portal approach walking");
        floorTransit.InitialHeld = 0;
        Position(chef, floorTarget.X, floorTarget.Z);
        floorTransit.Navigation = null; floorTransit.PortalFloorApproached = true; floorTransit.ApproachingPortalFloor = false;
        Check(runner.EnsureTransportNavigation(floorAction, floorTransit, state) && !Flag(floorTransit.Navigation!.Spec, "dash") &&
            !Flag(floorTransit.Navigation.Spec, "shortDash"), "final portal entry always walks after floor staging");

        foreach (var source in model.Stations.Where(s => s.Role is "cannon" or "portal"))
        {
            bool left = source.Position.X < model.Regions.Single(r => r.Id == "center").FloorBounds.MinX;
            bool upper = source.Role == "cannon";
            Position(chef, left ? 12 : 28.8, upper ? -12.5 : -20);
            var transport = new TransportState { Model = model, Source = source, Interaction = source,
                SourceRegion = model.RegionAt(ChefPosition(state, 2)) };
            Check(runner.TransitApproach(Action(upper ? "board-cannon" : "portal"), transport, state) is not null,
                "measured trigger has a reachable approach: " + source.Key);
        }

        Position(chef, 12, -12.5);
        var aim = Action("aim-cannon");
        var aimed = new TransportState();
        runner.InitializeTransport(aim, aimed, state, "aim-cannon");
        var cannon = RequireEntity(state, aimed.Source.EntityId);
        Check(Math.Abs(aimed.TargetAngle - RequiredNumber(cannon, "cannonMaxAngle")) < .01, "left down target uses native maximum");
        aim.Spec["cannon"] = "right";
        var aimedRight = new TransportState();
        runner.InitializeTransport(aim, aimedRight, state, "aim-cannon");
        Check(Math.Abs(aimedRight.TargetAngle - RequiredNumber(RequireEntity(state, aimedRight.Source.EntityId), "cannonMinAngle")) < .01,
            "right down target uses native minimum");
        aim.Spec["dash"] = true; aim.Spec["shortDash"] = true;
        runner.Stage(aim, "exit-terminal");
        var exitInput = Inputs.Neutral(Player(aim.Spec));
        runner.AimCannon(aim, aimedRight, state, exitInput);
        Check(Flag(exitInput, "use") && !Flag(exitInput, "dash") && Number(exitInput["x"]) == 0 && Number(exitInput["y"]) == 0,
            "terminal exit remains a neutral-motion use edge despite parent dash options");

        cannon = RequireEntity(state, cannons[0].EntityId);
        cannon["cannonLoadedEntityId"] = IdOf(chef, "entityId"); cannon["cannonReady"] = true;
        cannon["cannonFlying"] = false; cannon["cannonState"] = "Launched";
        var boarded = new TransportState { Source = cannons[0], ChefEntityId = IdOf(chef, "entityId") };
        Check(!Boarded(boarded, state), "stale passenger ID and ready flag are not proof of boarding");
        cannon["cannonState"] = "Load";
        Check(!Boarded(boarded, state), "Load requires native passenger session");
        chef["controlsEnabled"] = false; chef["interactingEntityId"] = cannons[0].EntityId;
        Check(Boarded(boarded, state), "Load plus disabled native cannon session confirms boarding");

        var fireChef = Chef(state, 3); Free(fireChef); fireChef["heldEntityId"] = 0; Position(fireChef, 20.4, -16.8);
        var fire = Action("fire-cannon", 3);
        var firing = runner.transportStates.GetValue(fire, _ => new TransportState());
        runner.InitializeTransport(fire, firing, state, "fire-cannon");
        fireChef["useTargetId"] = firing.Interaction.EntityId;
        cannon["cannonReady"] = false;
        Check(!Flag(Tick(fire), "use") && fire.Stage == "transport-verify" && firing.Navigation is null,
            "verified fire target skips navigation but waits for native ready");
        cannon["cannonReady"] = true;
        cannon["cannonTarget"] = new JsonObject { ["x"] = 28, ["y"] = .3, ["z"] = -20 };
        Check(Flag(Tick(fire), "use") && fire.Stage == "await-cannon-launch", "one fire edge with ready loaded passenger and button target");
        Check(!Flag(Tick(fire), "use"), "fire edge is released while waiting for launch");
        cannon["cannonState"] = "Launched"; cannon["cannonFlying"] = true;
        Tick(fire);
        Check(fire.Stage == "await-cannon-arrival" && !fire.Done, "native launch does not complete while flying");
        Position(chef, 28, -20); Free(chef);
        Tick(fire);
        Check(!fire.Done, "destination position and free control still wait for cannon flight completion");
        cannon["cannonFlying"] = false;
        Tick(fire); Tick(fire);
        Check(fire.Done, "completed native flight plus settled destination control confirms arrival");

        var portal = Action("portal"); portal.Spec.Remove("cannon");
        var porting = runner.transportStates.GetValue(portal, _ => new TransportState());
        runner.InitializeTransport(portal, porting, state, "portal");
        runner.Stage(portal, "await-portal-entry");
        Position(chef, 12, -12.5);
        Tick(portal);
        Check(!portal.Done && !porting.TransitionObserved, "destination position alone is not portal proof");
        chef["controlsEnabled"] = false;
        RequireEntity(state, porting.DestinationId)["portalReceiving"] = true;
        Tick(portal);
        Check(porting.TransitionObserved && !portal.Done, "native receiver session begins portal transit evidence");
        Free(chef); Tick(portal); Tick(portal);
        Check(portal.Done, "native portal transition and restored destination control confirm arrival");
        return checks;
    }
}
