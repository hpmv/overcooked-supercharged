using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    public static int StationaryTransferSelfTest(IReadOnlyDictionary<int, JsonObject> captured)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Stationary transfer: " + message); checks++; }
        JsonObject Read(int frame) => captured[frame].DeepClone().AsObject();
        JsonObject State(JsonObject snapshot) => (snapshot["state"] ?? snapshot).AsObject();
        JsonObject C(JsonObject snapshot) => Chef(State(snapshot), 0);
        JsonObject E(JsonObject snapshot, int id) => Entity(State(snapshot), id)!;
        (RouteRunner Runner, RouteActionHandle Handle) New(string type = "take", string station = "38", bool enabled = true, int[]? ingredients = null)
        {
            var runner = new RouteRunner(_ => throw new InvalidOperationException("Offline fixture attempted native I/O."), null);
            var spec = new JsonObject { ["player"] = 0, ["type"] = type, ["station"] = station, ["timeoutFrames"] = 600 };
            if (enabled) spec["stationaryTargetTransfer"] = true;
            if (ingredients is not null) spec["expectedIngredientIds"] = new JsonArray(ingredients.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            return (runner, runner.CreateAction(spec));
        }
        bool Neutral(JsonObject pad) => Number(pad["x"]) == 0 && Number(pad["y"]) == 0 && !Flag(pad, "pickup") && !Flag(pad, "use") && !Flag(pad, "dash");
        bool Pending(RouteRunner runner, RouteActionHandle handle) => runner.stationaryTransfers.TryGetValue(handle.Action, out var proof) && proof.CandidateFrame >= 0 && !proof.Disabled;
        bool Issued(RouteRunner runner, RouteActionHandle handle) => runner.stationaryTransfers.TryGetValue(handle.Action, out var proof) && proof.Issued;

        var take = New(); var original = Read(437); string untouched = original.ToJsonString();
        Check(Neutral(take.Runner.Tick(take.Handle, original)) && Pending(take.Runner, take.Handle), "captured exact stationary plate target begins released confirmation");
        Check(original.ToJsonString() == untouched, "observation is not mutated");
        var edge = take.Runner.Tick(take.Handle, Read(438));
        Check(Flag(edge, "pickup") && Number(edge["x"]) == 0 && Number(edge["y"]) == 0 && !Flag(edge, "use") && !Flag(edge, "dash") &&
              take.Handle.Stage == "await-transfer" && !take.Handle.IsDone && Issued(take.Runner, take.Handle), "next captured stationary frame emits only the existing pickup edge");
        Check(Neutral(take.Runner.Tick(take.Handle, Read(444))) && take.Handle.IsDone && take.Handle.Error is null, "actual same-plate native pickup completes through the original barrier");

        var baseline = New(enabled: false);
        baseline.Runner.Tick(baseline.Handle, Read(437)); var baselineInput = baseline.Runner.Tick(baseline.Handle, Read(438));
        Check(!Flag(baselineInput, "pickup") && !baseline.Runner.stationaryTransfers.TryGetValue(baseline.Handle.Action, out _), "omitted option preserves default navigation/face path");
        foreach (int frame in new[] {435, 436})
        {
            var moving = New(); var movingInput = moving.Runner.Tick(moving.Handle, Read(frame));
            Check(!Flag(movingInput, "pickup") && !Pending(moving.Runner, moving.Handle), "actual moving native target at" + frame + " retains ordinary navigation");
        }
        var repeated = New(); repeated.Runner.Tick(repeated.Handle, Read(437));
        Check(Neutral(repeated.Runner.Tick(repeated.Handle, Read(437))) && !Issued(repeated.Runner, repeated.Handle), "same native frame cannot manufacture confirmation");
        Check(Flag(repeated.Runner.Tick(repeated.Handle, Read(438)), "pickup"), "one actual following frame remains eligible after a duplicate observation");

        var mutations = new Dictionary<string, Action<JsonObject>> {
            ["rigidbody motion"] = s => C(s)["velocity"]!["x"] = .01,
            ["cached motion"] = s => C(s)["lastVelocity"]!["z"] = .01,
            ["nonfinite actual motion"] = s => C(s)["velocity"]!["x"] = double.NaN,
            ["surface movement"] = s => C(s)["surfaceVelocity"]!["x"] = .01,
            ["wind movement"] = s => C(s)["windVelocity"]!["z"] = .01,
            ["impact velocity"] = s => C(s)["impactVelocity"]!["x"] = .01,
            ["active impact"] = s => C(s)["impactTimer"] = .1,
            ["active dash"] = s => C(s)["dashTimer"] = .1,
            ["unknown dash clock"] = s => C(s)["dashTimer"] = double.NaN,
            ["unknown impact clock"] = s => C(s)["impactTimer"] = double.NaN,
            ["controls disabled"] = s => C(s)["controlsEnabled"] = false,
            ["native gate closed"] = s => C(s)["canAcceptInput"] = false,
            ["not directly controlled"] = s => C(s)["directlyControlled"] = false,
            ["movement suppressed"] = s => C(s)["inputSuppressed"] = true,
            ["use suppressed"] = s => C(s)["useSuppressed"] = true,
            ["throw armed"] = s => C(s)["aimingThrow"] = true,
            ["respawning"] = s => C(s)["respawning"] = true,
            ["native interaction"] = s => C(s)["interactingEntityId"] = 38,
            ["tracked projectile"] = s => C(s)["trackedThrowableEntityId"] = 123,
            ["not grounded"] = s => C(s)["groundNormal"]!["y"] = .9,
            ["missing actual velocity"] = s => C(s).Remove("velocity"),
            ["changed logical clock"] = s => State(s)["unityDeltaTime"] = .03,
            ["changed fixed clock"] = s => State(s)["fixedDeltaTime"] = .03,
            ["unknown fixed clock"] = s => State(s)["fixedDeltaTime"] = double.NaN,
            ["unknown logical clock"] = s => State(s)["unityDeltaTime"] = double.NaN,
            ["active projectile"] = s => E(s, 11)["throwFlying"] = true,
            ["active cannon flight"] = s => E(s, 84)["cannonFlying"] = true,
            ["registration error"] = s => State(s)["entityRegistration"]!["errorCount"] = 1,
            ["source ordinal changed"] = s => E(s, 10)["observedOrdinal"] = 999,
            ["station ordinal changed"] = s => E(s, 38)["observedOrdinal"] = 999,
            ["source inactive"] = s => E(s, 10)["active"] = false,
            ["station moved"] = s => E(s, 38)["position"]!["x"] = 19.3,
            ["source removed from counter"] = s => E(s, 38)["attachedEntityId"] = 0,
            ["source food changed"] = s => E(s, 10)["ingredientIds"] = new JsonArray(BUN_FOR_TEST),
            ["target disappeared"] = s => { C(s)["pickupTargetId"] = 0; C(s)["placementTargetId"] = 0; },
            ["target referral changed"] = s => C(s)["pickupTargetId"] = 10,
            ["pickup cooldown"] = s => C(s)["lastPickupTimestamp"] = Number(State(s)["clientTime"]) + .1,
            ["pose changed"] = s => C(s)["position"]!["x"] = Number(C(s)["position"]!["x"]) + .001,
            ["forward changed"] = s => C(s)["forward"]!["x"] = Number(C(s)["forward"]!["x"]) + .01,
            ["unknown zero forward"] = s => C(s)["forward"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
            ["native frame skipped"] = s => State(s)["frame"] = State(s)["frame"]!.GetValue<long>() + 1,
            ["nearby moving chef"] = s => {
                var other = Chef(State(s), 3); other["position"] = C(s)["position"]!.DeepClone();
                other["position"]!["x"] = Number(C(s)["position"]!["x"]) + .85; other["lastVelocity"]!["x"] = -6;
            }
        };
        foreach (var mutation in mutations)
        {
            var denied = New(); denied.Runner.Tick(denied.Handle, Read(437)); var altered = Read(438); mutation.Value(altered);
            var input = denied.Runner.Tick(denied.Handle, altered);
            Check(!Flag(input, "pickup") && !Issued(denied.Runner, denied.Handle), mutation.Key + " does not emit an early edge");
        }
        var reused = New(); reused.Runner.Tick(reused.Handle, Read(437)); var replacement = Read(438);
        var audit = State(replacement)["entityRegistration"]!.AsObject();
        var registered = audit["events"]!.AsArray().OfType<JsonObject>().Last(e => e["kind"]?.ToString() == "register" && IdOf(e["entity"]!, "entityId") == 10).DeepClone().AsObject();
        registered["sequence"] = audit["lastSequence"]!.GetValue<long>() + 1; registered["entity"]!["observedRegistrationSequence"] = registered["sequence"]!.DeepClone();
        audit["events"]!.AsArray().Add(registered);
        Check(!Flag(reused.Runner.Tick(reused.Handle, replacement), "pickup") && !Issued(reused.Runner, reused.Handle), "same native ID with a new actual registration is rejected");

        var wrongPickup = New(); wrongPickup.Runner.Tick(wrongPickup.Handle, Read(437)); wrongPickup.Runner.Tick(wrongPickup.Handle, Read(438));
        var wrongResult = Read(444); C(wrongResult)["heldEntityId"] = 11;
        Check(Neutral(wrongPickup.Runner.Tick(wrongPickup.Handle, wrongResult)) && wrongPickup.Handle.Error?.Contains("different native source") == true, "post-edge wrong pickup fails instead of completing");
        var reusedPickup = New(); reusedPickup.Runner.Tick(reusedPickup.Handle, Read(437)); reusedPickup.Runner.Tick(reusedPickup.Handle, Read(438));
        var reusedResult = Read(444); E(reusedResult, 10)["observedOrdinal"] = 999;
        reusedPickup.Runner.Tick(reusedPickup.Handle, reusedResult);
        Check(reusedPickup.Handle.Error?.Contains("different native source") == true, "post-edge same ID with changed ordinal cannot satisfy original pickup");

        foreach (var example in new[] { ("place", "49", 1156, 1157, 1163, (int[]?)null),
                     ("combine", "7", 785, 786, 792, new[] { 262914, 284626 }),
                     ("combine", "9", 1075, 1076, 1082, new[] { 17094, 262914, 284626, 461162 }) })
        {
            var task = New(example.Item1, example.Item2, ingredients: example.Item6);
            Check(Neutral(task.Runner.Tick(task.Handle, Read(example.Item3))) && Pending(task.Runner, task.Handle), example.Item1 + example.Item2 + " captures exact native stationary target");
            Check(Flag(task.Runner.Tick(task.Handle, Read(example.Item4)), "pickup") && !task.Handle.IsDone, example.Item1 + example.Item2 + " preserves a distinct confirmation and existing edge");
            Check(Neutral(task.Runner.Tick(task.Handle, Read(example.Item5))) && task.Handle.IsDone && task.Handle.Error is null, example.Item1 + example.Item2 + " requires actual native transfer evidence");
        }
        var assembly = New("assemble", "23", ingredients: [262914]); assembly.Runner.Tick(assembly.Handle, Read(464));
        Check(Flag(assembly.Runner.Tick(assembly.Handle, Read(465)), "pickup"), "same native bun/plate assembly can start from confirmed stationary target");
        Check(Neutral(assembly.Runner.Tick(assembly.Handle, Read(471))) && !assembly.Handle.IsDone && assembly.Handle.Stage == "recover-plate-ready", "native plate-under result enters original recovery stage rather than false completion");
        Check(Neutral(assembly.Runner.Tick(assembly.Handle, Read(472))), "existing assembly recovery retains its required released frame");
        Check(Flag(assembly.Runner.Tick(assembly.Handle, Read(473)), "pickup"), "existing native plate recovery emits its normal fresh edge");
        Check(Neutral(assembly.Runner.Tick(assembly.Handle, Read(474))) && assembly.Handle.IsDone && assembly.Handle.Error is null, "same actual native plate recovery satisfies original material expectation");
        var replacedPlate = New("assemble", "23", ingredients: [262914]); replacedPlate.Runner.Tick(replacedPlate.Handle, Read(464));
        replacedPlate.Runner.Tick(replacedPlate.Handle, Read(465)); replacedPlate.Runner.Tick(replacedPlate.Handle, Read(471));
        var falseRecovery = Read(472); E(falseRecovery, 10)["observedOrdinal"] = 999;
        Check(Neutral(replacedPlate.Runner.Tick(replacedPlate.Handle, falseRecovery)) && replacedPlate.Handle.Error?.Contains("source ID was reused") == true,
            "original plate incarnation remains pinned through native under-placement recovery");
        return checks;
    }
    private const int BUN_FOR_TEST = 262914;
}
