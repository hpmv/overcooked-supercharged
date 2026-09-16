using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    public static int StationaryTransferDefaultsSelfTest(JsonObject at437, JsonObject at438)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Stationary defaults: " + message); checks++; }
        RouteRunner New(bool enabled = false) => new(_ => throw new InvalidOperationException("Offline default fixture attempted native I/O."), null)
            { StationaryTargetTransfers = enabled };
        JsonObject Spec(string type) => new() { ["type"] = type, ["station"] = "38", ["player"] = 0 };
        Check(!new CarnivalPlannerOptions().StationaryTargetTransfers && !New().StationaryTargetTransfers, "planner and runner defaults remain disabled");
        foreach (string type in new[] { "take", "place", "combine", "apply", "assemble" })
        {
            var spec = Spec(type); var source = spec.ToJsonString(); var enabled = New(true);
            var action = enabled.CreateAction(spec);
            Check(action.Specification["stationaryTargetTransfer"]?.GetValue<bool>() == true, type + " receives enabled runner default");
            Check(spec.ToJsonString() == source && !spec.ContainsKey("stationaryTargetTransfer"), type + " does not mutate the original queued/caller specification");
            enabled.StationaryTargetTransfers = false;
            Check(action.Specification["stationaryTargetTransfer"]?.GetValue<bool>() == true, type + " preserves its creation-time setting");
            Check(!New().CreateAction(spec).Specification.ContainsKey("stationaryTargetTransfer"), type + " remains unchanged when default is disabled");
            var optOut = Spec(type); optOut["stationaryTargetTransfer"] = false;
            Check(New(true).CreateAction(optOut).Specification["stationaryTargetTransfer"]?.GetValue<bool>() == false, type + " preserves explicit false");
            var optIn = Spec(type); optIn["stationaryTargetTransfer"] = true;
            Check(New().CreateAction(optIn).Specification["stationaryTargetTransfer"]?.GetValue<bool>() == true, type + " preserves explicit per-action true with disabled default");
            var explicitNull = Spec(type); explicitNull["stationaryTargetTransfer"] = null;
            Check(New(true).CreateAction(explicitNull).Specification["stationaryTargetTransfer"] is null, type + " copies a default only when the field is absent");
        }
        foreach (string type in new[] { "navigate", "face", "throw", "chop", "wash", "mix", "cook", "portal", "fire-cannon", "switch-condiment" })
            Check(!New(true).CreateAction(Spec(type)).Specification.ContainsKey("stationaryTargetTransfer"), type + " does not receive a transfer-only default");
        var propagated = New(true); var take = propagated.CreateAction(Spec("take"));
        var first = propagated.Tick(take, at437.DeepClone()); var edge = propagated.Tick(take, at438.DeepClone());
        Check(!Flag(first, "pickup") && Number(first["x"]) == 0 && Number(first["y"]) == 0 && Flag(edge, "pickup") && take.Stage == "await-transfer",
            "inherited option uses the same captured neutral confirmation and original ordinary edge");
        var optedOut = New(true); var off = Spec("take"); off["stationaryTargetTransfer"] = false; var ordinary = optedOut.CreateAction(off);
        optedOut.Tick(ordinary, at437.DeepClone()); var second = optedOut.Tick(ordinary, at438.DeepClone());
        Check(!Flag(second, "pickup") && !optedOut.stationaryTransfers.TryGetValue(ordinary.Action, out _), "explicit opt-out preserves captured ordinary navigation/face path");
        return checks;
    }
}
