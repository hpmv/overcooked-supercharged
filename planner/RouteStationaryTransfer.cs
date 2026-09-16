using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    private sealed record StationaryIdentity(int Id, int Ordinal, long Registration);
    private sealed class StationaryTransfer
    {
        public readonly Dictionary<int, StationaryIdentity> Identities = [];
        public readonly Dictionary<int, JsonNode> Positions = [];
        public int Station, Chef, Held, ExpectedTake;
        public string? SpawnPrefab;
        public bool Disabled, Issued;
        public long CandidateFrame = -1, CandidateGameplayFrame = -1, RegistrationCheckpoint;
        public double CandidateTime;
        public int Target;
        public string Field = "";
        public JsonNode? Position, Forward, Material;
    }
    private readonly ConditionalWeakTable<Active, StationaryTransfer> stationaryTransfers = new();

    // Guards consume the per-action option. An opt-in runner default may copy it
    // into a newly created action; explicit per-action opt-outs remain intact.
    private void CaptureStationaryTransfer(Active action, JsonNode state)
    {
        if (action.Spec["stationaryTargetTransfer"]?.GetValue<bool>() != true || stationaryTransfers.TryGetValue(action, out _)) return;
        var proof = new StationaryTransfer(); stationaryTransfers.Add(action, proof);
        if (action.Motion?.Station is not { } station || !StationaryAuditAvailable(state)) { proof.Disabled = true; return; }
        proof.Station = station.EntityId; proof.Chef = IdOf(Chef(state, Player(action.Spec)), "entityId"); proof.Held = action.InitialHeld;
        var related = InteractionTargets(state, station.EntityId);
        if (proof.Held != 0) related.Add(proof.Held);
        foreach (int id in related)
        {
            if (ReadStationaryIdentity(state, id) is not { } identity || Entity(state, id)?["position"] is not { } position)
            { proof.Disabled = true; return; }
            proof.Identities.Add(id, identity);
            if (id != proof.Held) proof.Positions.Add(id, position.DeepClone());
        }
        var target = Entity(state, station.EntityId)!;
        proof.SpawnPrefab = target["spawnPrefab"]?.ToString();
        if (!string.IsNullOrEmpty(proof.SpawnPrefab)) proof.ExpectedTake = 0;
        else if (KitchenModel.Components(target).Contains("CarryableItem")) proof.ExpectedTake = station.EntityId;
        else proof.ExpectedTake = IdOf(target, "attachedEntityId");
    }

    private static bool StationaryAuditAvailable(JsonNode state) => state["entityRegistration"] is JsonObject audit &&
        audit["installed"]?.GetValue<bool>() == true && audit["addHookInstalled"]?.GetValue<bool>() == true &&
        audit["removeHookInstalled"]?.GetValue<bool>() == true && audit["errorCount"] is not null && Number(audit["errorCount"]) == 0 &&
        audit["roundDropped"] is not null && Number(audit["roundDropped"]) == 0;

    private static StationaryIdentity? ReadStationaryIdentity(JsonNode state, int id)
    {
        var entity = Entity(state, id); long sequence = ThrowRegistrationSequence(state, id);
        return entity?["active"]?.GetValue<bool>() == true && entity["observedOrdinal"] is not null && sequence >= 0
            ? new(id, IdOf(entity, "observedOrdinal"), sequence) : null;
    }

    private static bool StationaryVector(JsonNode? node, double horizontal = .00001, double vertical = .01)
    {
        if (node is null || node["x"] is null || node["y"] is null || node["z"] is null) return false;
        double x = Number(node["x"]), y = Number(node["y"]), z = Number(node["z"]);
        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z) && Math.Sqrt(x * x + z * z) <= horizontal && Math.Abs(y) <= vertical;
    }

    private static bool SameStationaryVector(JsonNode? left, JsonNode? right, double tolerance)
    {
        if (left is null || right is null) return false;
        return new[] { "x", "y", "z" }.All(k => left[k] is not null && right[k] is not null &&
            double.IsFinite(Number(left[k])) && double.IsFinite(Number(right[k])) && Math.Abs(Number(left[k]) - Number(right[k])) <= tolerance);
    }

    private static JsonNode StationaryMaterial(JsonNode state, IEnumerable<int> ids)
    {
        JsonNode? Food(JsonNode? node)
        {
            if (node is null) return null;
            var clone = node.DeepClone();
            void StripProgress(JsonNode? value)
            {
                if (value is JsonObject obj) { obj.Remove("progress"); foreach (var pair in obj.ToArray()) StripProgress(pair.Value); }
                else if (value is JsonArray array) foreach (var item in array) StripProgress(item);
            }
            StripProgress(clone); return clone;
        }
        var result = new JsonArray();
        foreach (int id in ids.Order())
        {
            var e = Entity(state, id)!; var material = new JsonObject { ["id"] = id };
            foreach (string key in new[] { "attachedEntityId", "spawnPrefab", "switchIndex", "ingredientIds", "workStage", "workSubStage",
                         "cookingTime", "cookingTypeId", "mixingTime", "mixingTypeId" }) material[key] = e[key]?.DeepClone();
            // Native heat progress advances during the neutral frame. Ingredient multiset,
            // preparation kind/state and native configuration must stay identical.
            material["composition"] = Food(e["composition"]); material["contents"] = Food(e["contents"]); result.Add(material);
        }
        return result;
    }

    private string? StationaryTransferInvalid(Active action, StationaryTransfer proof, JsonNode state, bool take,
        out string field, out int observed, out HashSet<int> allowed)
    {
        field = ""; observed = 0; allowed = [];
        if (action.Motion?.Station is not { } station || station.EntityId != proof.Station || action.Motion.Model is not { } model ||
            action.Motion.Dash is not null || action.Spec["ignoreChefs"]?.GetValue<bool>() == true ||
            action.Spec["invertX"]?.GetValue<bool>() == true || action.Spec["invertY"]?.GetValue<bool>() == false) return "resolved motion changed or is not the measured ordinary approach";
        if (!StationaryAuditAvailable(state) || state["scene"]?.ToString() != "s_Day_3_4" ||
            state["serverRoundActive"]?.GetValue<bool>() != true || state["clientRoundActive"]?.GetValue<bool>() != true ||
            state["timerSuppressed"]?.GetValue<bool>() != false || state["frame"] is null || state["gameplayFrame"] is null ||
            state["clientTime"] is null || !double.IsFinite(Number(state["clientTime"])) || state["unityDeltaTime"] is null || !double.IsFinite(Number(state["unityDeltaTime"])) ||
            Math.Abs(Number(state["unityDeltaTime"]) - 1.0 / 60) > .00001 || state["fixedDeltaTime"] is null ||
            !double.IsFinite(Number(state["fixedDeltaTime"])) || Math.Abs(Number(state["fixedDeltaTime"]) - .02) > .00001 || state["framesSinceNoPhysics"] is null ||
            !double.IsFinite(Number(state["framesSinceNoPhysics"])) ||
            Number(state["framesSinceNoPhysics"]) is < 0 or > 5) return "native observation/clock contract unavailable";
        var chef = Chef(state, Player(action.Spec));
        if (IdOf(chef, "entityId") != proof.Chef || IdOf(chef, "heldEntityId") != proof.Held ||
            chef["controlsEnabled"]?.GetValue<bool>() != true || chef["canAcceptInput"]?.GetValue<bool>() != true ||
            chef["directlyControlled"]?.GetValue<bool>() != true || chef["inputSuppressed"]?.GetValue<bool>() != false ||
            chef["useSuppressed"]?.GetValue<bool>() != false || chef["respawning"]?.GetValue<bool>() != false ||
            chef["aimingThrow"]?.GetValue<bool>() != false || chef["dashTimer"] is null || !double.IsFinite(Number(chef["dashTimer"])) || Number(chef["dashTimer"]) > 0 ||
            chef["impactTimer"] is null || !double.IsFinite(Number(chef["impactTimer"])) || Number(chef["impactTimer"]) >= 0 ||
            new[] { "interactingEntityId", "clientPredictedInteractionId", "serverInteractionId", "trackedThrowableEntityId" }.Any(k => chef[k] is null || Number(chef[k]) != 0))
            return "chef identity, held item or native control/interaction gate changed";
        // velocity is the observed Rigidbody.velocity; lastVelocity is the native cached
        // movement consumed by a later FixedUpdate. Neither may carry horizontal motion.
        if (!StationaryVector(chef["velocity"]) || !StationaryVector(chef["lastVelocity"]) ||
            !StationaryVector(chef["impactVelocity"]) || !StationaryVector(chef["surfaceVelocity"]) || !StationaryVector(chef["windVelocity"]) ||
            !SameStationaryVector(chef["groundNormal"], new JsonObject { ["x"] = 0, ["y"] = 1, ["z"] = 0 }, .001) ||
            !SameStationaryVector(chef["position"], chef["position"], 0) || !SameStationaryVector(chef["forward"], chef["forward"], 0))
            return "actual/cached/surface motion or grounded pose is not stationary";
        double forwardLength = KitchenModel.Position(chef["forward"]).Distance(default);
        if (forwardLength is < .999 or > 1.001 || Math.Abs(Number(chef["forward"]?["y"])) > .001)
            return "native forward is not a finite horizontal unit direction";
        var position = ChefPosition(state, Player(action.Spec));
        if (model.RegionAt(position) is null) return "chef is outside the measured floor";
        if ((state["entities"] as JsonArray)?.OfType<JsonObject>().Any(e => e["active"]?.GetValue<bool>() == true &&
            (e["throwFlying"]?.GetValue<bool>() == true || e["cannonFlying"]?.GetValue<bool>() == true)) == true)
            return "native projectile or cannon flight can change the next interaction pose";
        foreach (var other in (state["chefs"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (IdOf(other, "playerId") == Player(action.Spec)) continue;
            var velocity = KitchenModel.Position(other["velocity"]); var cached = KitchenModel.Position(other["lastVelocity"]);
            double approach = Math.Max(velocity.Distance(default), cached.Distance(default)) * .02;
            if (!double.IsFinite(approach) || KitchenModel.Position(other["position"]).Distance(position) <= 2 * model.ChefRadius + approach + .05)
                return "another chef can enter the stationary contact envelope";
        }
        foreach (var pair in proof.Identities)
        {
            if (ReadStationaryIdentity(state, pair.Key) != pair.Value) return "resolved source/held/station incarnation changed";
            if (proof.Positions.TryGetValue(pair.Key, out var original) && !SameStationaryVector(Entity(state, pair.Key)?["position"], original, .001))
                return "resolved source/station moved";
        }
        var nowRelated = InteractionTargets(state, station.EntityId); if (proof.Held != 0) nowRelated.Add(proof.Held);
        if (!nowRelated.SetEquals(proof.Identities.Keys)) return "resolved native attachment topology changed";
        bool combine = action.Spec["type"]?.ToString() is "combine" or "apply" or "assemble";
        if (!NativeTransferTarget(state, station.EntityId, Player(action.Spec), take, combine, out field, out observed, out allowed)) return "native target is not the exact resolved source/destination";
        if (take && (proof.ExpectedTake == 0 && string.IsNullOrEmpty(proof.SpawnPrefab) || chef["lastPickupTimestamp"] is null ||
                     !double.IsFinite(Number(chef["lastPickupTimestamp"])) || !PickupEligible(state, Player(action.Spec))))
            return "native pickup source or cooldown is not proven";
        return null;
    }

    private bool TryStationaryTransfer(Active action, JsonNode state, JsonObject input, bool take, bool combine)
    {
        if (!stationaryTransfers.TryGetValue(action, out var proof) || proof.Disabled || proof.Issued || action.Stage is not ("navigate" or "face")) return false;
        string? invalid = StationaryTransferInvalid(action, proof, state, take, out string field, out int target, out var allowed);
        if (invalid is not null)
        {
            if (proof.CandidateFrame >= 0)
            {
                proof.Disabled = true;
                trace?.Event("stationaryTransferRejected", new JsonObject { ["player"] = Player(action.Spec), ["reason"] = invalid, ["frame"] = state["frame"]?.DeepClone() });
            }
            return false;
        }
        var chef = Chef(state, Player(action.Spec)); long frame = state["frame"]!.GetValue<long>();
        var material = StationaryMaterial(state, proof.Identities.Keys);
        if (proof.CandidateFrame < 0)
        {
            proof.CandidateFrame = frame; proof.CandidateGameplayFrame = state["gameplayFrame"]!.GetValue<long>(); proof.CandidateTime = Number(state["clientTime"]);
            proof.Position = chef["position"]!.DeepClone(); proof.Forward = chef["forward"]!.DeepClone(); proof.Material = material;
            proof.Target = target; proof.Field = field; proof.RegistrationCheckpoint = state["entityRegistration"]!["lastSequence"]!.GetValue<long>();
            trace?.Event("stationaryTransferNeutralConfirmation", new JsonObject { ["player"] = Player(action.Spec), ["stationId"] = proof.Station,
                ["targetId"] = target, ["field"] = field, ["frame"] = frame, ["gameplayFrame"] = proof.CandidateGameplayFrame,
                ["heldId"] = proof.Held, ["position"] = proof.Position.DeepClone(), ["actualVelocity"] = chef["velocity"]!.DeepClone(),
                ["cachedVelocity"] = chef["lastVelocity"]!.DeepClone(), ["nativeNextPickupTime"] = chef["lastPickupTimestamp"]?.DeepClone() });
            // Update supplies an all-neutral pad. A fresh input edge is not emitted until
            // a distinct, contiguous native frame confirms this released stationary pose.
            return true;
        }
        if (frame == proof.CandidateFrame) return true;
        if (frame != proof.CandidateFrame + 1 || state["gameplayFrame"]!.GetValue<long>() != proof.CandidateGameplayFrame + 1 ||
            Math.Abs(Number(state["clientTime"]) - proof.CandidateTime - 1.0 / 60) > .00001 || target != proof.Target || field != proof.Field ||
            !SameStationaryVector(chef["position"], proof.Position, .0001) || !SameStationaryVector(chef["forward"], proof.Forward, .0001) ||
            !JsonNode.DeepEquals(material, proof.Material))
        {
            proof.Disabled = true;
            trace?.Event("stationaryTransferRejected", new JsonObject { ["player"] = Player(action.Spec), ["reason"] = "neutral confirmation changed frame/pose/target/material", ["frame"] = frame });
            return false;
        }
        proof.Issued = true;
        trace?.Event("stationaryTransferEdge", new JsonObject { ["player"] = Player(action.Spec), ["stationId"] = proof.Station, ["targetId"] = target,
            ["candidateFrame"] = proof.CandidateFrame, ["frame"] = frame, ["heldId"] = proof.Held, ["expectedTakeId"] = proof.ExpectedTake,
            ["nativeIncarnations"] = new JsonArray(proof.Identities.Values.OrderBy(x => x.Id).Select(x => (JsonNode)new JsonObject {
                ["id"] = x.Id, ["ordinal"] = x.Ordinal, ["registration"] = x.Registration }).ToArray()) });
        EmitTransferEdge(action, state, input, action.Motion!.Station!, combine, field, target, allowed);
        return true;
    }

    private void ValidateStationaryTransferResult(Active action, JsonNode state, bool take)
    {
        if (!stationaryTransfers.TryGetValue(action, out var proof) || !proof.Issued) return;
        if (ReadStationaryIdentity(state, proof.Station) != proof.Identities[proof.Station])
            throw new InvalidOperationException("Stationary transfer lost its exact native station incarnation.");
        int held = Held(state, Player(action.Spec));
        if (take && held != 0)
        {
            if (proof.ExpectedTake != 0 && (held != proof.ExpectedTake || ReadStationaryIdentity(state, held) != proof.Identities[proof.ExpectedTake]))
                throw new InvalidOperationException("Stationary pickup took a different native source incarnation.");
            if (proof.ExpectedTake == 0 && (Entity(state, held)?["name"]?.ToString() != proof.SpawnPrefab || ThrowRegistrationSequence(state, held) <= proof.RegistrationCheckpoint))
                throw new InvalidOperationException("Stationary crate pickup lacks its newly registered expected native ingredient.");
        }
        if (!take && held != 0 && (held != proof.Held || ReadStationaryIdentity(state, held) != proof.Identities[proof.Held]))
            throw new InvalidOperationException("Stationary placement changed the held native incarnation.");
        if (!take && Entity(state, proof.Held)?["active"]?.GetValue<bool>() == true && ReadStationaryIdentity(state, proof.Held) != proof.Identities[proof.Held])
            throw new InvalidOperationException("Stationary placement's original source ID was reused during native transfer.");
        // The existing transfer code still proves attachment/content changes, native
        // ingredient expectations and the same-plate under-placement/recovery sequence.
    }
}
