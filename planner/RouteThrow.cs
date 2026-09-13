using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    private sealed class ThrowState
    {
        public int ItemId, ThrowerId, Recipient = -1, PreviousThrower, ReleaseFrame;
        public int SourceOrdinal = -1;
        public long SourceRegistration = -1;
        public Point2 Target, ReleasedTarget, LastItemPosition;
        public bool FlightObserved, ThrowObserved;
        public bool ConditionalGuardArmed;
        public int SettledFrames;
        public int VesselId, VesselAnchorId, MissingSourceFrame = -1;
        public Dictionary<int, int> IngredientCounts = [], BeforeVesselCounts = [];
    }
    private readonly ConditionalWeakTable<Active, ThrowState> throwStates = new();

    // This action controls only the throwing chef. A separately controlled
    // recipient must have empty hands and face the incoming trajectory.
    private void Throw(Active action, JsonNode state, JsonObject input)
    {
        var data = throwStates.GetValue(action, _ => new ThrowState());
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        RequireControlTelemetry(chef);
        if(!CheckConditionalFarPot(action,data,state))return;
        if (action.Spec["nativeSupplyHome"] is JsonObject homeGuard && action.Stage is not ("throw-await-flight" or "throw-await-arrival"))
        {
            string? invalid = IdOf(action.Spec, "targetEntityId") != homeGuard["vessel"]?.GetValue<int>() ? "throw target differs from the recorded native home" : NativeSupplyHome.Invalid(state, homeGuard);
            if (invalid is not null)
            {
                bool armed = data.ConditionalGuardArmed || Flag(chef, "aimingThrow") ||
                    action.Stage == "throw-clear-use-press" && !Flag(chef, "useSuppressed");
                trace?.Event("nativeSupplyHomeRejected", new JsonObject { ["reason"] = invalid, ["armed"] = armed,
                    ["stage"] = action.Stage, ["cleanupMayReleaseIngredient"] = armed, ["frame"] = state["frame"]?.DeepClone() });
                throw ThrowFailure(action, data, state, "native receiving home changed; candidate failed; neutral cleanup may release an armed ingredient: " + invalid);
            }
        }
        if (action.Stage == "resolve") InitializeThrow(action, data, state);
        if (Flag(chef, "respawning") || !Flag(chef, "controlsEnabled") || !Flag(chef, "directlyControlled"))
            throw ThrowFailure(action, data, state, "thrower lost ordinary chef control");

        if (action.Stage == "throw-clear-use-press")
        {
            RequireThrowHeld(action, data, state);
            if (!Flag(chef, "useSuppressed"))
            {
                // If native state already cleared, keep holding: releasing an
                // unsuppressed use button would itself throw the ingredient.
                input["use"] = true;
                data.ConditionalGuardArmed = true;
                Stage(action, "throw-arm");
            }
            else Stage(action, "throw-clear-use-release");
            return;
        }
        if (action.Stage == "throw-clear-use-release")
        {
            RequireThrowHeld(action, data, state);
            if (!Flag(chef, "useSuppressed")) Stage(action, "throw-await-recipient");
            else if (action.Frames - action.StageStarted > 15)
                throw ThrowFailure(action, data, state, "native use suppression did not clear after its normal release edge");
            return;
        }

        if (action.Stage == "throw-await-recipient")
        {
            RequireThrowHeld(action, data, state);
            if (!FreeControl(chef) || !ThrowRecipientReady(data, state))
            {
                if (action.Frames - action.StageStarted >= (action.Spec["recipientWaitFrames"]?.GetValue<int>() ?? 180))
                    throw ThrowFailure(action, data, state, "recipient did not become empty-handed and face the incoming trajectory");
                return;
            }
            input["use"] = true;
            if(!Flag(chef,"useSuppressed"))data.ConditionalGuardArmed = true;
            // Native ClearEvents can suppress use across cannon/portal flows.
            // A suppressed press and release clears that native flag. Native
            // Update_Throw reads suppression before the release clears it, so
            // this preparatory cycle cannot throw the held ingredient.
            Stage(action, Flag(chef, "useSuppressed") ? "throw-clear-use-press" : "throw-arm");
            return;
        }
        if (action.Stage == "throw-arm")
        {
            RequireThrowHeld(action, data, state);
            input["use"] = true;
            if (Flag(chef, "useSuppressed"))
            {
                Stage(action, "throw-clear-use-press");
                return;
            }
            if (Flag(chef, "aimingThrow") && Flag(chef, "inputSuppressed"))
            {
                if (++action.StableFrames >= 2) Stage(action, "throw-aim");
            }
            else action.StableFrames = 0;
            if (action.Frames - action.StageStarted >= (action.Spec["armTimeoutFrames"]?.GetValue<int>() ?? 60))
                throw ThrowFailure(action, data, state, "native aiming and movement suppression did not engage");
            return;
        }
        if (action.Stage == "throw-aim")
        {
            RequireThrowHeld(action, data, state);
            input["use"] = true;
            if (!Flag(chef, "aimingThrow") || !Flag(chef, "inputSuppressed"))
                throw ThrowFailure(action, data, state, "native aiming or movement suppression ended before release");
            data.Target = ThrowTarget(action, data, state);
            Point2 origin = ChefPosition(state, player);
            var direction = new Point2(data.Target.X - origin.X, data.Target.Z - origin.Z);
            double distance = direction.Distance(default);
            if (distance < .25) throw ThrowFailure(action, data, state, "target moved too close to define a throw direction");
            input["x"] = direction.X / distance;
            input["y"] = -direction.Z / distance;
            var forward = KitchenModel.Position(chef["forward"]);
            double length = forward.Distance(default);
            double cosine = length < .01 ? -1 : (forward.X * direction.X + forward.Z * direction.Z) / (length * distance);
            bool aligned = cosine >= Math.Cos((action.Spec["aimToleranceDegrees"]?.GetValue<double>() ?? 1) * Math.PI / 180);
            if (aligned && ThrowRecipientReady(data, state))
            {
                if (++action.StableFrames >= 2)
                {
                    data.ReleasedTarget = data.Target;
                    // A neutral stick keeps the already verified heading and
                    // avoids queued movement when the use button is released.
                    input["x"] = 0; input["y"] = 0;
                    Stage(action, "throw-release");
                }
            }
            else action.StableFrames = 0;
            if (action.Frames - action.StageStarted >= (action.Spec["aimTimeoutFrames"]?.GetValue<int>() ?? 120))
                throw ThrowFailure(action, data, state, "aim or recipient readiness did not stabilize");
            return;
        }
        if (action.Stage == "throw-release")
        {
            RequireThrowHeld(action, data, state);
            if (data.VesselId != 0) data.BeforeVesselCounts = ThrowVesselContents(RequireThrowVessel(data, state));
            // Default input is neutral. Native Update_Throw uses this new use
            // release edge, the actual chef heading, and native throw velocity.
            data.ReleaseFrame = action.Frames;
            Stage(action, "throw-await-flight");
            trace?.Event("throwReleased", new JsonObject { ["player"] = player, ["itemId"] = data.ItemId,
                ["recipientPlayer"] = data.Recipient, ["target"] = ThrowPoint(data.ReleasedTarget), ["frame"] = state["frame"]?.DeepClone() });
            return;
        }

        var item = Entity(state, data.ItemId);
        if (data.VesselId != 0)
        {
            ObserveVesselThrow(action, data, state, item);
            return;
        }
        if (item is null) throw ThrowFailure(action, data, state, "thrown item disappeared before arrival could be verified");
        RequireThrowTelemetry(item);
        int held = Held(state, player);
        if (held != 0 && held != data.ItemId) throw ThrowFailure(action, data, state, "thrower acquired an unrelated item during the throw");
        bool flying = Flag(item, "throwFlying");
        bool ownFlight = flying && IdOf(item, "throwerEntityId") == data.ThrowerId;
        if (ownFlight) data.FlightObserved = true;
        var holder = (state["chefs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c => IdOf(c, "heldEntityId") == data.ItemId);
        bool caughtByRecipient = data.Recipient >= 0 && holder is not null && IdOf(holder, "playerId") == data.Recipient;
        bool previous = IdOf(item, "previousThrowerEntityId") == data.ThrowerId;
        // A very close native catch can begin and end between captures. Its
        // recipient attachment and previous thrower identify that transfer.
        bool immediateCatch = held == 0 && caughtByRecipient && previous;
        bool freshFinishedThrow = held == 0 && previous && data.PreviousThrower != data.ThrowerId;
        data.ThrowObserved |= data.FlightObserved || immediateCatch || freshFinishedThrow;
        if (action.Stage == "throw-await-flight")
        {
            if (held == 0 && data.ThrowObserved)
            {
                Stage(action, "throw-await-arrival");
                trace?.Event("throwObserved", new JsonObject { ["player"] = player, ["itemId"] = data.ItemId,
                    ["flightObserved"] = data.FlightObserved, ["immediateCatch"] = immediateCatch });
            }
            else if (action.Frames - data.ReleaseFrame >= (action.Spec["releaseTimeoutFrames"]?.GetValue<int>() ?? 30))
                throw ThrowFailure(action, data, state, "release did not clear the held item with native throw evidence");
            else return;
        }
        if (held != 0) throw ThrowFailure(action, data, state, "thrown item returned to the throwing chef before arrival");
        if (Number(item["position"]?["y"]) < -2) throw ThrowFailure(action, data, state, "thrown item fell below the kitchen");
        if (data.Recipient >= 0)
        {
            if (holder is not null && !caughtByRecipient) throw ThrowFailure(action, data, state, "an unexpected chef caught the item");
            if (caughtByRecipient && !flying && FreeControl(chef))
            {
                if (++data.SettledFrames >= 2) CompleteThrow(action, data, state, "native recipient catch");
            }
            else data.SettledFrames = 0;
        }
        else
        {
            if (holder is not null) throw ThrowFailure(action, data, state, "a chef caught an item intended for the specified landing area");
            Point2 position = KitchenModel.Position(item["position"]);
            double speed = ThrowSpeed(item["velocity"]);
            bool stable = !flying && speed < .15 && position.Distance(data.LastItemPosition) < .01;
            data.LastItemPosition = position;
            data.SettledFrames = stable ? data.SettledFrames + 1 : 0;
            if (data.SettledFrames >= (action.Spec["landingStableFrames"]?.GetValue<int>() ?? 6))
            {
                if (!ThrowInsideLanding(action, data, item)) throw ThrowFailure(action, data, state, "item settled outside its requested landing area");
                if (FreeControl(chef)) CompleteThrow(action, data, state, "native throw finished and item settled inside the landing area");
            }
        }
        if (!action.Done && action.Frames - data.ReleaseFrame >= (action.Spec["flightTimeoutFrames"]?.GetValue<int>() ?? 360))
            throw ThrowFailure(action, data, state, "native flight, catch, or settled landing was not confirmed in time");
    }

    private void InitializeThrow(Active action, ThrowState data, JsonNode state)
    {
        int player = Player(action.Spec);
        var chef = Chef(state, player);
        RequireProperty(chef, "aimingThrow"); RequireProperty(chef, "inputSuppressed");
        data.ItemId = Held(state, player); data.ThrowerId = IdOf(chef, "entityId");
        if (data.ItemId == 0) throw new InvalidOperationException("throw requires a native ThrowableItem in this chef's hands.");
        var item = RequireEntity(state, data.ItemId);
        data.SourceOrdinal = item["observedOrdinal"]?.GetValue<int>() ?? -1;
        data.SourceRegistration = ThrowRegistrationSequence(state, data.ItemId);
        if ((item["components"] as JsonArray)?.Any(c => c?.ToString() is "ThrowableItem" or "ServerThrowableItem" or "ClientThrowableItem") != true)
            throw new InvalidOperationException("throw requires a native ThrowableItem in this chef's hands.");
        RequireThrowTelemetry(item);
        if (Flag(item, "throwFlying")) throw new InvalidOperationException("Held item is already marked in native throw flight.");
        if (new[] { "target", "recipientPlayer", "targetEntityId", "station" }.Count(k => action.Spec[k] is not null) != 1)
            throw new ArgumentException("throw requires exactly one target {x,z}, recipientPlayer, targetEntityId, or station.");
        if (action.Spec["recipientPlayer"] is { } recipient)
        {
            data.Recipient = recipient.GetValue<int>();
            if (data.Recipient is < 0 or > 3 || data.Recipient == player) throw new ArgumentException("Throw recipient must be another chef from 0 through 3.");
            RequireProperty(Chef(state, data.Recipient), "catchAngleMax");
        }
        if (action.Spec["targetEntityId"] is not null || action.Spec["station"] is not null)
        {
            InitializeThrowVessel(action, data, state);
            if (item["composition"] is not JsonObject && (item["contents"] as JsonArray)?.Count is not > 0)
                throw new InvalidOperationException("Vessel throw requires an observed native ingredient composition; raw unchopped WorkableItems cannot be verified as container ingredients.");
            data.IngredientCounts = ThrowIngredientCounts(CarnivalRecipes.ClassifyEntity(item).Food.IngredientIds);
            if (data.IngredientCounts.Count == 0) throw new InvalidOperationException("Thrown item has no observable native ingredient IDs.");
        }
        double tolerance = action.Spec["aimToleranceDegrees"]?.GetValue<double>() ?? 1;
        if (tolerance <= 0 || tolerance > 10) throw new ArgumentException("aimToleranceDegrees must be greater than zero and no more than 10.");
        if ((action.Spec["landingRadius"]?.GetValue<double>() ?? 1) <= 0) throw new ArgumentException("landingRadius must be positive.");
        if ((action.Spec["landingStableFrames"]?.GetValue<int>() ?? 6) < 2) throw new ArgumentException("landingStableFrames must be at least 2.");
        if (action.Spec["landingBounds"] is JsonObject bounds)
        {
            foreach (var field in new[] { "minX", "maxX", "minZ", "maxZ" }) RequireProperty(bounds, field);
            if (Number(bounds["minX"]) > Number(bounds["maxX"]) || Number(bounds["minZ"]) > Number(bounds["maxZ"])) throw new ArgumentException("Throw landingBounds are reversed.");
        }
        data.PreviousThrower = IdOf(item, "previousThrowerEntityId");
        data.Target = ThrowTarget(action, data, state);
        if (data.Target.Distance(ChefPosition(state, player)) < .25) throw new ArgumentException("Throw target is too close to define an aim direction.");
        action.WorkEntityId = data.ItemId;
        Stage(action, "throw-await-recipient");
        trace?.Event("throwResolved", new JsonObject { ["player"] = player, ["itemId"] = data.ItemId,
            ["recipientPlayer"] = data.Recipient, ["targetEntityId"] = data.VesselId, ["target"] = ThrowPoint(data.Target),
            ["nativeThrowForce"] = chef["throwForce"]?.DeepClone(), ["nativeThrowInclination"] = chef["throwInclination"]?.DeepClone() });
    }
    private static Point2 ThrowTarget(Active action, ThrowState data, JsonNode state)
    {
        if (data.Recipient >= 0) return ChefPosition(state, data.Recipient);
        if (data.VesselId != 0) return KitchenModel.Position(RequireThrowVessel(data, state)["position"]);
        var target = action.Spec["target"] as JsonObject ?? throw new ArgumentException("Throw target must be an object with x and z.");
        return new(RequiredNumber(target, "x"), RequiredNumber(target, "z"));
    }
    private static bool ThrowRecipientReady(ThrowState data, JsonNode state)
    {
        if (data.Recipient < 0) return true;
        var recipient = Chef(state, data.Recipient);
        if (!FreeControl(recipient) || IdOf(recipient, "heldEntityId") != 0 || IdOf(recipient, "interactingEntityId") != 0) return false;
        var thrower = ChefByEntity(state, data.ThrowerId);
        Point2 incoming = KitchenModel.Position(thrower["position"]), position = KitchenModel.Position(recipient["position"]);
        var direction = new Point2(incoming.X - position.X, incoming.Z - position.Z);
        var forward = KitchenModel.Position(recipient["forward"]);
        double denominator = direction.Distance(default) * forward.Distance(default);
        return denominator > .001 && (forward.X * direction.X + forward.Z * direction.Z) / denominator >=
            Math.Cos(RequiredNumber(recipient, "catchAngleMax") * Math.PI / 180);
    }
    private static void InitializeThrowVessel(Active action, ThrowState data, JsonNode state)
    {
        int target;
        if (action.Spec["targetEntityId"] is { } id) target = id.GetValue<int>();
        else target = KitchenModel.Build(state.AsObject()).Resolve(action.Spec["station"]!.ToString()).EntityId;
        var entity = RequireEntity(state, target);
        if (!IsThrowVessel(entity))
        {
            data.VesselAnchorId = target;
            entity = RequireEntity(state, IdOf(entity, "attachedEntityId"));
        }
        if (!IsThrowVessel(entity)) throw new ArgumentException("Throw target must resolve to cookware with native IngredientContainer and IngredientCatcher components.");
        data.VesselId = IdOf(entity, "id");
        if (data.VesselId == data.ItemId) throw new ArgumentException("The thrown ingredient and receiving vessel must be different entities.");
        if (data.VesselAnchorId == 0)
            data.VesselAnchorId = (state["entities"] as JsonArray)?.OfType<JsonObject>().Where(e => IdOf(e, "attachedEntityId") == data.VesselId)
                .Select(e => IdOf(e, "id")).FirstOrDefault() ?? 0;
        _ = RequireThrowVessel(data, state);
    }
    private static bool IsThrowVessel(JsonObject entity)
    {
        var components = (entity["components"] as JsonArray)?.Select(c => c?.ToString() ?? "").ToHashSet() ?? [];
        return components.Contains("ServerIngredientContainer") && components.Contains("ServerIngredientCatcher");
    }
    private static JsonObject RequireThrowVessel(ThrowState data, JsonNode state)
    {
        var vessel = RequireEntity(state, data.VesselId);
        if (vessel["active"]?.GetValue<bool>() == false) throw new InvalidOperationException("Receiving vessel became inactive during the throw.");
        if ((state["chefs"] as JsonArray)?.OfType<JsonObject>().Any(c => IdOf(c, "heldEntityId") == data.VesselId) == true)
            throw new InvalidOperationException("Receiving vessel is being carried; place it on its station before throwing ingredients into it.");
        if (data.VesselAnchorId != 0 && IdOf(RequireEntity(state, data.VesselAnchorId), "attachedEntityId") != data.VesselId)
            throw new InvalidOperationException("Receiving station no longer contains the reserved vessel.");
        return vessel;
    }
    private static Dictionary<int, int> ThrowIngredientCounts(IEnumerable<int> ingredients) => ingredients.Where(id => id > 0).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    private static Dictionary<int, int> ThrowVesselContents(JsonObject vessel)
    {
        var contents = RequireProperty(vessel, "contents") as JsonArray ?? throw new InvalidOperationException("Vessel contents telemetry is not an array.");
        return ThrowIngredientCounts(contents.SelectMany(food => CarnivalRecipes.ClassifyFood(food).IngredientIds));
    }
    private static bool ThrowVesselDeltaMatches(ThrowState data, JsonObject vessel)
    {
        var expected = new Dictionary<int, int>(data.BeforeVesselCounts);
        foreach (var ingredient in data.IngredientCounts) expected[ingredient.Key] = expected.GetValueOrDefault(ingredient.Key) + ingredient.Value;
        var actual = ThrowVesselContents(vessel);
        return expected.Count == actual.Count && expected.All(p => actual.GetValueOrDefault(p.Key) == p.Value);
    }
    private void ObserveVesselThrow(Active action, ThrowState data, JsonNode state, JsonObject? item)
    {
        int player = Player(action.Spec), held = Held(state, player);
        var vessel = RequireThrowVessel(data, state);
        if (held != 0 && held != data.ItemId) throw ThrowFailure(action, data, state, "thrower acquired a different item before vessel arrival");
        if (item is not null)
        {
            RequireThrowTelemetry(item);
            if (Flag(item, "throwFlying") && IdOf(item, "throwerEntityId") == data.ThrowerId)
                data.FlightObserved = data.ThrowObserved = true;
        }
        bool consumed = item is null || item["active"]?.GetValue<bool>() == false;
        bool matchingContents = ThrowVesselDeltaMatches(data, vessel);
        if (consumed && data.MissingSourceFrame < 0) data.MissingSourceFrame = action.Frames;
        if (held == 0 && consumed && matchingContents)
        {
            // Native IngredientCatcher transfers the thrown composition and
            // destroys the loose object. Destruction alone is never success.
            data.ThrowObserved = true;
            if (FreeControl(Chef(state, player)) && ++data.SettledFrames >= 2)
                CompleteThrow(action, data, state, "source consumed and reserved vessel gained exactly the original native ingredients");
        }
        else data.SettledFrames = 0;
        if (action.Done) return;
        if (action.Stage == "throw-await-flight" && held == 0 && data.ThrowObserved) Stage(action, "throw-await-arrival");
        if (held == data.ItemId && action.Frames - data.ReleaseFrame >= (action.Spec["releaseTimeoutFrames"]?.GetValue<int>() ?? 30))
            throw ThrowFailure(action, data, state, "native release left the original ingredient held; throwing may be blocked");
        if (consumed && !matchingContents && action.Frames - data.MissingSourceFrame >= 15)
            throw ThrowFailure(action, data, state, "source disappeared without the reserved vessel gaining the expected ingredient counts");
        if (item is not null && Number(item["position"]?["y"]) < -2) throw ThrowFailure(action, data, state, "ingredient fell below the kitchen before reaching its vessel");
        if (action.Frames - data.ReleaseFrame >= (action.Spec["flightTimeoutFrames"]?.GetValue<int>() ?? 360))
            throw ThrowFailure(action, data, state, "reserved vessel did not receive the expected original ingredients in time");
    }
    private static void RequireThrowTelemetry(JsonObject item)
    {
        foreach (var field in new[] { "throwFlying", "throwFlightTime", "throwerEntityId", "previousThrowerEntityId" }) RequireProperty(item, field);
        if (RequiredNumber(item, "throwFlightTime") < 0) throw new InvalidOperationException("Item has no native ServerThrowableItem telemetry.");
    }
    private static void RequireThrowHeld(Active action, ThrowState data, JsonNode state)
    {
        if (Held(state, Player(action.Spec)) != data.ItemId) throw new InvalidOperationException("Held throwable changed before the planned release edge.");
    }
    private static double ThrowSpeed(JsonNode? velocity) => Math.Sqrt(Math.Pow(Number(velocity?["x"]), 2) + Math.Pow(Number(velocity?["y"]), 2) + Math.Pow(Number(velocity?["z"]), 2));
    private static JsonObject ThrowPoint(Point2 point) => new() { ["x"] = point.X, ["z"] = point.Z };
    private static bool ThrowInsideLanding(Active action, ThrowState data, JsonObject item)
    {
        Point2 point = KitchenModel.Position(item["position"]);
        double y = Number(item["position"]?["y"]);
        if (action.Spec["landingBounds"] is JsonObject bounds)
        {
            return point.X >= Number(bounds["minX"]) && point.X <= Number(bounds["maxX"]) && point.Z >= Number(bounds["minZ"]) && point.Z <= Number(bounds["maxZ"])
                && y >= (bounds["minY"] is null ? -.2 : Number(bounds["minY"])) && y <= (bounds["maxY"] is null ? 2 : Number(bounds["maxY"]));
        }
        return point.Distance(data.ReleasedTarget) <= (action.Spec["landingRadius"]?.GetValue<double>() ?? 1)
            && y >= -.2 && y <= (action.Spec["maximumLandingHeight"]?.GetValue<double>() ?? 2);
    }
    private InvalidOperationException ThrowFailure(Active action, ThrowState data, JsonNode state, string reason)
    {
        var chef = Chef(state, Player(action.Spec));
        var item = Entity(state, data.ItemId);
        var evidence = new JsonObject { ["reason"] = reason, ["stage"] = action.Stage, ["player"] = Player(action.Spec), ["itemId"] = data.ItemId,
            ["heldEntityId"] = chef["heldEntityId"]?.DeepClone(), ["aimingThrow"] = chef["aimingThrow"]?.DeepClone(), ["inputSuppressed"] = chef["inputSuppressed"]?.DeepClone(), ["useSuppressed"] = chef["useSuppressed"]?.DeepClone(),
            ["itemPosition"] = item?["position"]?.DeepClone(), ["itemVelocity"] = item?["velocity"]?.DeepClone(), ["throwFlying"] = item?["throwFlying"]?.DeepClone(),
            ["throwerEntityId"] = item?["throwerEntityId"]?.DeepClone(), ["previousThrowerEntityId"] = item?["previousThrowerEntityId"]?.DeepClone(), ["actionFrames"] = action.Frames,
            ["targetEntityId"] = data.VesselId, ["targetContents"] = data.VesselId == 0 ? null : Entity(state, data.VesselId)?["contents"]?.DeepClone() };
        trace?.Event("throwFailure", evidence);
        return new InvalidOperationException("Throw failed: " + evidence.ToJsonString());
    }
    private void CompleteThrow(Active action, ThrowState data, JsonNode state, string evidence)
    {
        action.Done = true;
        trace?.Event("throwComplete", new JsonObject { ["player"] = Player(action.Spec), ["itemId"] = data.ItemId,
            ["recipientPlayer"] = data.Recipient, ["targetEntityId"] = data.VesselId, ["evidence"] = evidence, ["flightObserved"] = data.FlightObserved,
            ["position"] = Entity(state, data.ItemId)?["position"]?.DeepClone(), ["frame"] = state["frame"]?.DeepClone() });
        throwStates.Remove(action);
    }

    /// <summary>Pure synthetic state-transition checks; no game connection or physics edits.</summary>
    public static int ThrowSelfTest()
    {
        JsonObject MakeChef(int player, double x) => new()
        {
            ["playerId"] = player, ["entityId"] = 100 + player, ["heldEntityId"] = 0,
            ["position"] = new JsonObject { ["x"] = x, ["y"] = .05, ["z"] = 0 },
            ["forward"] = new JsonObject { ["x"] = 1, ["y"] = 0, ["z"] = 0 },
            ["controlsEnabled"] = true, ["directlyControlled"] = true, ["canAcceptInput"] = true,
            ["aimingThrow"] = false, ["inputSuppressed"] = false, ["useSuppressed"] = false, ["respawning"] = false,
            ["interactingEntityId"] = 0, ["catchAngleMax"] = 45, ["catchDistance"] = 1,
            ["throwForce"] = 12, ["throwInclination"] = 10
        };
        var source = MakeChef(0, 0); var recipient = MakeChef(1, 5); var bystander = MakeChef(2, 8);
        var item = new JsonObject { ["id"] = 9001, ["name"] = "synthetic fixture ingredient",
            ["components"] = new JsonArray("ThrowableItem", "ServerThrowableItem"),
            ["position"] = new JsonObject { ["x"] = .6, ["y"] = .65, ["z"] = 0 },
            ["velocity"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
            ["throwFlying"] = false, ["throwFlightTime"] = 0, ["throwerEntityId"] = 0, ["previousThrowerEntityId"] = 0 };
        var state = new JsonObject { ["chefs"] = new JsonArray(source, recipient, bystander, MakeChef(3, 12)), ["entities"] = new JsonArray(item), ["frame"] = 0 };
        var runner = new RouteRunner(_ => throw new InvalidOperationException("Throw fixture must never call the game."), null);
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Throw fixture check failed: " + label);
            checks++;
        }
        JsonObject Tick(Active action)
        {
            var input = Inputs.Neutral(Player(action.Spec)); runner.Throw(action, state, input); action.Frames++; return input;
        }
        source["heldEntityId"] = 9001;
        var action = new Active(new JsonObject { ["type"] = "throw", ["player"] = 0, ["recipientPlayer"] = 1 });
        Check(!Flag(Tick(action), "use") && action.Stage == "throw-await-recipient", "recipient must face the incoming throw before arming");
        recipient["forward"]!["x"] = -1; recipient["heldEntityId"] = 7777;
        Check(!Flag(Tick(action), "use"), "recipient must have empty hands before arming");
        recipient["heldEntityId"] = 0;
        var arm = Tick(action);
        Check(Flag(arm, "use") && Number(arm["x"]) == 0 && Number(arm["y"]) == 0, "arm uses no movement input");
        source["aimingThrow"] = true;
        var suppress = Tick(action);
        Check(action.Stage == "throw-arm" && Number(suppress["x"]) == 0, "aim axes wait for native movement suppression");
        source["inputSuppressed"] = true;
        Tick(action); Tick(action);
        Check(action.Stage == "throw-aim", "two native suppression observations enable aiming");
        var aim = Tick(action);
        Check(Flag(aim, "use") && Number(aim["x"]) > .99 && action.Stage == "throw-aim", "first aligned observation continues holding use");
        Check(Flag(Tick(action), "use") && action.Stage == "throw-release", "second aligned observation schedules release without releasing early");
        var release = Tick(action);
        Check(!Flag(release, "use") && Number(release["x"]) == 0 && action.Stage == "throw-await-flight", "one neutral release edge begins the native throw");
        Check(!Flag(Tick(action), "use") && !action.Done, "release alone cannot complete a throw");
        source["heldEntityId"] = 0; source["aimingThrow"] = false; source["inputSuppressed"] = false;
        item["throwFlying"] = true; item["throwerEntityId"] = 100; item["throwFlightTime"] = .02;
        Tick(action);
        Check(action.Stage == "throw-await-arrival" && !action.Done, "native flight waits for catch or landing");
        recipient["heldEntityId"] = 9001; item["throwFlying"] = false; item["throwerEntityId"] = 0; item["previousThrowerEntityId"] = 100;
        Tick(action); Tick(action);
        Check(action.Done, "exact recipient attachment confirms the observed native throw");

        source["heldEntityId"] = 9001; recipient["heldEntityId"] = 0; item["previousThrowerEntityId"] = 0;
        var landing = new Active(new JsonObject { ["type"] = "throw", ["player"] = 0,
            ["target"] = new JsonObject { ["x"] = 5, ["z"] = 0 }, ["landingRadius"] = .5 });
        var landingData = runner.throwStates.GetValue(landing, _ => new ThrowState());
        runner.InitializeThrow(landing, landingData, state); landingData.ReleasedTarget = new(5, 0);
        runner.Stage(landing, "throw-await-flight"); source["heldEntityId"] = 0;
        item["position"] = new JsonObject { ["x"] = 5, ["y"] = .5, ["z"] = 0 };
        Tick(landing);
        Check(!landing.Done && !landingData.ThrowObserved, "stationary item at target is not evidence of a throw");
        item["previousThrowerEntityId"] = 100;
        for (int i = 0; i < 8 && !landing.Done; i++) Tick(landing);
        Check(landing.Done, "fresh native previous thrower plus stable bounded landing confirms a short flight");

        source["heldEntityId"] = 9001; item["previousThrowerEntityId"] = 0;
        var missed = new Active(landing.Spec.DeepClone().AsObject());
        var missedData = runner.throwStates.GetValue(missed, _ => new ThrowState());
        runner.InitializeThrow(missed, missedData, state); missedData.ReleasedTarget = new(5, 0);
        runner.Stage(missed, "throw-await-flight"); source["heldEntityId"] = 0;
        item["previousThrowerEntityId"] = 100; item["position"]!["x"] = 8;
        bool rejected = false;
        try { for (int i = 0; i < 10; i++) Tick(missed); }
        catch (InvalidOperationException error) when (error.Message.Contains("outside its requested landing area", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "settled landing outside requested bounds fails instead of correcting position");
        source["heldEntityId"] = 9001; item["components"] = new JsonArray("Plate");
        rejected = false;
        try { Tick(new Active(new JsonObject { ["type"] = "throw", ["player"] = 0, ["recipientPlayer"] = 1 })); }
        catch (InvalidOperationException error) when (error.Message.Contains("native ThrowableItem", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "plate is rejected before any use input");

        JsonObject Ingredient(int id) => new() { ["type"] = "IngredientAssembledNode", ["id"] = id, ["children"] = new JsonArray() };
        item["components"] = new JsonArray("ThrowableItem", "ServerThrowableItem");
        item["composition"] = Ingredient(CarnivalRecipes.Frankfurter.Id); item["active"] = true;
        item["position"]!["x"] = .6; item["previousThrowerEntityId"] = 0;
        var vessel = new JsonObject { ["id"] = 9002, ["active"] = true, ["components"] = new JsonArray("ServerIngredientContainer", "ServerIngredientCatcher"),
            ["position"] = new JsonObject { ["x"] = 5, ["y"] = 1, ["z"] = 0 }, ["contents"] = new JsonArray() };
        var anchor = new JsonObject { ["id"] = 9003, ["active"] = true, ["attachedEntityId"] = 9002, ["components"] = new JsonArray("AttachStation") };
        state["entities"]!.AsArray().Add(vessel); state["entities"]!.AsArray().Add(anchor);
        (Active Action, ThrowState Data) VesselAction()
        {
            source["heldEntityId"] = 9001; item["active"] = true;
            var test = new Active(new JsonObject { ["type"] = "throw", ["player"] = 0, ["targetEntityId"] = 9003 });
            var data = runner.throwStates.GetValue(test, _ => new ThrowState());
            runner.InitializeThrow(test, data, state);
            data.BeforeVesselCounts = ThrowVesselContents(vessel); data.ReleasedTarget = new(5, 0);
            runner.Stage(test, "throw-await-flight");
            return (test, data);
        }
        var missing = VesselAction(); source["heldEntityId"] = 0; item["active"] = false;
        Tick(missing.Action);
        Check(!missing.Action.Done && !missing.Data.ThrowObserved, "vessel source disappearance alone is not success");
        vessel["contents"] = new JsonArray(Ingredient(CarnivalRecipes.Egg.Id));
        Tick(missing.Action);
        Check(!missing.Action.Done, "wrong ingredient increase does not prove the intended vessel catch");
        missing.Action.Frames = 20; rejected = false;
        try { Tick(missing.Action); }
        catch (InvalidOperationException error) when (error.Message.Contains("without the reserved vessel gaining", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "missing source with wrong contents terminates with explicit evidence failure");
        vessel["contents"] = new JsonArray(Ingredient(CarnivalRecipes.Frankfurter.Id));
        var duplicate = VesselAction(); source["heldEntityId"] = 0; item["active"] = false;
        Tick(duplicate.Action); Tick(duplicate.Action);
        Check(!duplicate.Action.Done, "preexisting matching ingredient is not a newly caught ingredient");
        vessel["contents"]!.AsArray().Add(Ingredient(CarnivalRecipes.Frankfurter.Id));
        Tick(duplicate.Action); Tick(duplicate.Action);
        Check(duplicate.Action.Done && duplicate.Data.VesselId == 9002, "station resolves reserved vessel and confirms exact ingredient multiplicity increase");
        vessel["contents"] = new JsonArray();
        var blocked = VesselAction(); blocked.Action.Frames = 31; rejected = false;
        try { Tick(blocked.Action); }
        catch (InvalidOperationException error) when (error.Message.Contains("left the original ingredient held", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "blocked release retaining original held ingredient fails within release timeout");
        source["heldEntityId"] = 9001; source["useSuppressed"] = true;
        var suppressed = new Active(new JsonObject { ["type"] = "throw", ["player"] = 0, ["recipientPlayer"] = 1 });
        Check(Flag(Tick(suppressed), "use") && suppressed.Stage == "throw-clear-use-press", "observed native suppression receives a short ordinary press");
        Check(!Flag(Tick(suppressed), "use") && suppressed.Stage == "throw-clear-use-release", "suppressed use is released before arming");
        source["useSuppressed"] = false;
        Check(!Flag(Tick(suppressed), "use") && suppressed.Stage == "throw-await-recipient", "guard verifies suppression cleared and preserves a neutral frame");
        Check(Flag(Tick(suppressed), "use") && suppressed.Stage == "throw-arm" && IdOf(source, "heldEntityId") == 9001,
            "fresh unsuppressed press arms the same ingredient");
        return checks;
    }
}
