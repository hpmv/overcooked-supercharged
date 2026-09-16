using Google.Protobuf;
using Hpmv;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

/// <summary>Pure, fixed-duration logical pads; no game-state or geometry feedback.</summary>
public sealed class RawInputPlan
{
    public const string SupportedTerminalGameState = "RunLevelOutro";
    public sealed record Frame(Dictionary<int, OneInputData> Inputs, Dictionary<GameEntityRecord, ControllerState> States,
        Dictionary<GameEntityRecord, ActualControllerInput> History);
    public List<Frame> Frames { get; } = new();
    public int PayloadFrames { get; private set; }
    public JsonObject Recording { get; private set; }
    public string ExpectedTerminalGameState { get; private set; }
    public int? TerminalObservedFrames { get; private set; }
    public int? TerminalEmittedFrames { get; private set; }
    private readonly record struct Pad(float X, float Y, bool Pickup, bool Interact, bool Dash);

    public static RawInputPlan Create(GameSetup setup, int startFrame, JsonObject request, bool replay)
    {
        var result = new RawInputPlan();
        var chefs = setup.entityRecords.Chefs.Keys.OrderBy(c => c.path.ids[0]).ToArray();
        if (chefs.Length != 4) throw new InvalidOperationException("Raw input requires exactly four known chef mappings.");
        int[] ids = chefs.Select(c => c.path.ids[0]).ToArray();
        var states = chefs.ToDictionary(c => c, c => setup.entityRecords.Chefs[c][startFrame]);
        var previousDash = chefs.ToDictionary(c => c, c => setup.inputHistory.FrameInputs[c][startFrame].dash.isDown);
        var initial = new JsonObject();
        foreach (var chef in chefs) initial[chef.path.ids[0].ToString()] = Convert.ToBase64String(states[chef].ToProto().ToByteArray());
        string initialHash = Hash(initial);
        var requested = new List<Dictionary<int, Pad>>();
        JsonArray expectedRows = null;
        string requestedTerminal = request["expectedTerminalGameState"]?.ToString();
        if (requestedTerminal is not null && requestedTerminal != SupportedTerminalGameState)
            throw new ArgumentException("The only supported raw-input terminal is RunLevelOutro.");
        if (replay)
        {
            var recording = request["recording"]?.AsObject() ?? throw new ArgumentException("Replay requires recording.");
            var payload = (JsonObject)recording.DeepClone(); string expectedHash = payload["sha256"]?.ToString(); payload.Remove("sha256");
            if (expectedHash != Hash(payload)) throw new InvalidDataException("Recorded input checksum mismatch.");
            if (recording["version"]?.GetValue<int>() != 1 || recording["kind"]?.ToString() != "supercharged-logical-input-recording") throw new InvalidDataException("Unsupported recording format.");
            if (recording["initialControllerSha256"]?.ToString() != initialHash) throw new InvalidOperationException("Replay starting controller state differs from recorded state; restore the matching checkpoint.");
            expectedRows = recording["frames"]?.AsArray() ?? throw new InvalidDataException("Recorded frames missing.");
            result.PayloadFrames = recording["payloadFrames"]?.GetValue<int>() ?? -1;
            if (result.PayloadFrames < 1 || result.PayloadFrames > 36000 || expectedRows.Count != result.PayloadFrames + 2) throw new InvalidDataException("Recording requires payload and exactly two release/barrier frames.");
            result.ExpectedTerminalGameState = recording["expectedTerminalGameState"]?.ToString();
            if (result.ExpectedTerminalGameState is not null && result.ExpectedTerminalGameState != SupportedTerminalGameState)
                throw new InvalidDataException("Recorded raw-input terminal is unsupported.");
            if (requestedTerminal != result.ExpectedTerminalGameState)
                throw new InvalidDataException("Replay must explicitly request the recorded terminal game state.");
            bool hasObserved = recording["terminalObservedFrames"] is not null;
            bool hasEmitted = recording["terminalEmittedFrames"] is not null;
            if (result.ExpectedTerminalGameState is null)
            {
                if (hasObserved || hasEmitted) throw new InvalidDataException("Non-terminal recording contains a terminal receipt.");
            }
            else
            {
                if (!hasObserved || !hasEmitted) throw new InvalidDataException("Terminal recording lacks its observed/emitted receipt.");
                result.TerminalObservedFrames = recording["terminalObservedFrames"]!.GetValue<int>();
                result.TerminalEmittedFrames = recording["terminalEmittedFrames"]!.GetValue<int>();
                if (result.TerminalObservedFrames < 1 || result.TerminalObservedFrames >= expectedRows.Count ||
                    result.TerminalEmittedFrames != result.TerminalObservedFrames ||
                    result.TerminalEmittedFrames > expectedRows.Count)
                    throw new InvalidDataException("Terminal recording has an invalid observed/emitted boundary.");
            }
            foreach (var row in expectedRows)
            {
                var inputs = row!["inputs"]!.Deserialize<Dictionary<int, OneInputData>>(RuntimeHost.Json)!;
                if (!inputs.Keys.Order().SequenceEqual(ids)) throw new InvalidDataException("Recorded chef mappings differ.");
                var pads = new Dictionary<int, Pad>();
                foreach (int id in ids)
                {
                    var value = inputs[id];
                    if (value.Pad is null || value.Pickup is null || value.Interact is null || value.Dash is null) throw new InvalidDataException("Recorded logical pad fields missing.");
                    pads[id] = CheckedPad(value.Pad.X, value.Pad.Y, value.Pickup.Down, value.Interact.Down, value.Dash.Down);
                }
                requested.Add(pads);
            }
            foreach (var tail in requested.TakeLast(2)) if (tail.Values.Any(p => p != default)) throw new InvalidDataException("Recording must finish with two neutral logical pads.");
        }
        else
        {
            result.ExpectedTerminalGameState = requestedTerminal;
            var segments = request["segments"]?.AsArray() ?? throw new ArgumentException("Raw input requires segments.");
            if (segments.Count is < 1 or > 1024) throw new ArgumentException("Use 1..1024 segments.");
            foreach (var segment in segments)
            {
                int frames = segment?["frames"]?.GetValue<int>() ?? 0;
                if (frames is < 1 or > 36000 || requested.Count + frames > 36000) throw new ArgumentException("Use 1..36000 total payload frames.");
                var values = segment!["chefs"]?.AsObject() ?? throw new ArgumentException("Each segment must explicitly specify all four chefs.");
                if (!values.Select(v => int.Parse(v.Key)).Order().SequenceEqual(ids)) throw new ArgumentException("Each segment must name exactly the four observed native chef IDs.");
                var pads = new Dictionary<int, Pad>();
                foreach (int id in ids)
                {
                    var value = values[id.ToString()]!.AsObject();
                    foreach (string field in new[] { "x", "y", "pickup", "interact", "dash" }) if (value[field] is null) throw new ArgumentException("Explicit logical pad field required: " + field);
                    pads[id] = CheckedPad(value["x"]!.GetValue<double>(), value["y"]!.GetValue<double>(), value["pickup"]!.GetValue<bool>(), value["interact"]!.GetValue<bool>(), value["dash"]!.GetValue<bool>());
                }
                for (int i = 0; i < frames; i++) requested.Add(pads);
            }
            result.PayloadFrames = requested.Count;
            requested.Add(ids.ToDictionary(id => id, _ => default(Pad)));
            requested.Add(ids.ToDictionary(id => id, _ => default(Pad)));
        }
        var rows = new JsonArray();
        foreach (var pads in requested)
        {
            var nextStates = new Dictionary<GameEntityRecord, ControllerState>();
            var history = new Dictionary<GameEntityRecord, ActualControllerInput>();
            var inputs = new Dictionary<int, OneInputData>();
            foreach (var chef in chefs)
            {
                int id = chef.path.ids[0]; Pad pad = pads[id]; var prior = states[chef];
                // The existing controller advances axes and its local history
                // exactly once. Raw pads specify independent logical buttons;
                // this adapter fills the one-edge desired-input API limitation.
                var desired = new DesiredControllerInput { axes = new Vector2(pad.X, pad.Y), primaryDown = pad.Pickup && !prior.primaryButtonDown,
                    primaryDownIsForPickup = true, primaryUp = !pad.Pickup && prior.primaryButtonDown,
                    secondaryDown = pad.Interact && !prior.secondaryButtonDown, secondaryUp = !pad.Interact && prior.secondaryButtonDown,
                    dash = pad.Dash };
                var (next, actual) = prior.ApplyInputAndAdvanceFrame(desired);
                next.primaryButtonDown = pad.Pickup; next.secondaryButtonDown = pad.Interact; next.dashButtonDown = pad.Dash;
                actual.primary = Logical(pad.Pickup, prior.primaryButtonDown);
                actual.secondary = Logical(pad.Interact, prior.secondaryButtonDown);
                actual.dash = Logical(pad.Dash, previousDash[chef]); previousDash[chef] = pad.Dash;
                history[chef] = actual; nextStates[chef] = next;
                inputs[id] = new OneInputData { Pad = new PadDirection { X = actual.axes.X, Y = actual.axes.Y }, Pickup = Wire(actual.primary), Interact = Wire(actual.secondary), Dash = Wire(actual.dash) };
            }
            states = nextStates;
            if (expectedRows is not null)
            {
                var expected = expectedRows[result.Frames.Count]!["inputs"]!.Deserialize<Dictionary<int, OneInputData>>(RuntimeHost.Json)!;
                if (ids.Any(id => !inputs[id].Equals(expected[id]))) throw new InvalidDataException("Recorded logical edges do not match their preceding held states.");
            }
            rows.Add(new JsonObject { ["ordinal"] = result.Frames.Count, ["inputs"] = JsonSerializer.SerializeToNode(inputs, RuntimeHost.Json) });
            result.Frames.Add(new(inputs, nextStates, history));
        }
        result.Recording = new JsonObject { ["version"] = 1, ["kind"] = "supercharged-logical-input-recording", ["payloadFrames"] = result.PayloadFrames,
            ["releaseFrames"] = 2, ["initialControllerStates"] = initial, ["initialControllerSha256"] = initialHash,
            ["frames"] = rows, ["qualification"] = "Fixed logical input stream; native interaction success and physics replay require separate observations." };
        if (result.ExpectedTerminalGameState is not null)
        {
            result.Recording["expectedTerminalGameState"] = result.ExpectedTerminalGameState;
            if (result.TerminalObservedFrames.HasValue)
            {
                result.Recording["terminalObservedFrames"] = result.TerminalObservedFrames.Value;
                result.Recording["terminalEmittedFrames"] = result.TerminalEmittedFrames.Value;
            }
            result.Recording["qualification"] = "Fixed logical input plan with an explicit RunLevelOutro terminal; only the separately receipted observed prefix reached native gameplay.";
        }
        result.Recording["sha256"] = Hash(result.Recording);
        return result;
    }

    public void AcceptTerminalReceipt(int observedFrames, int emittedFrames)
    {
        if (ExpectedTerminalGameState is null || observedFrames < 1 || observedFrames >= Frames.Count ||
            emittedFrames != observedFrames || emittedFrames > Frames.Count)
            throw new InvalidOperationException("Raw terminal receipt is outside the planned stream.");
        if (TerminalObservedFrames.HasValue)
        {
            if (TerminalObservedFrames.Value != observedFrames || TerminalEmittedFrames.Value != emittedFrames)
                throw new InvalidOperationException("Replayed terminal boundary differs from the recording.");
            return;
        }
        TerminalObservedFrames = observedFrames;
        TerminalEmittedFrames = emittedFrames;
        Recording.Remove("sha256");
        Recording["terminalObservedFrames"] = observedFrames;
        Recording["terminalEmittedFrames"] = emittedFrames;
        Recording["sha256"] = Hash(Recording);
    }
    private static Pad CheckedPad(double x, double y, bool pickup, bool interact, bool dash)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1 || Math.Abs(y) > 1) throw new ArgumentException("Logical axes must be finite and in [-1,1].");
        return new((float)x, (float)y, pickup, interact, dash);
    }
    private static ButtonOutput Logical(bool value, bool before) => new() { isDown = value, justPressed = value && !before, justReleased = !value && before };
    private static ButtonInput Wire(ButtonOutput value) => new() { Down = value.isDown, JustPressed = value.justPressed, JustReleased = value.justReleased };
    public static string Hash(JsonNode value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToJsonString())));
}
