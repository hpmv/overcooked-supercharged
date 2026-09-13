using Hpmv;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public sealed partial class HeadlessSession
{
    private RawInputPlan raw;
    private int rawStart, rawEmitted, rawObserved, rawAlignmentCallbacks;
    private string rawOutcome = "none", rawError;
    private string rawObservedTerminalGameState;
    private int? rawTerminalFrame, rawTerminalObservedFrames;
    private bool rawWarpToStartOnTerminal;
    private JsonObject rawTerminalSnapshot;
    private Stopwatch rawWall;
    private const int MaximumRawAlignmentCallbacks = 12;
    private bool RawActive => rawOutcome is "awaiting-resume" or "emitting" or "awaiting-pause";
    private void InterruptRaw(string reason)
    {
        if (!RawActive) return;
        rawOutcome = "interrupted"; rawError = reason; rawWall?.Stop();
    }

    private void StartRaw(JsonObject request, bool replay)
    {
        if (core.State != RealGameState.Paused) throw new InvalidOperationException("Raw input requires a settled paused session.");
        if (RawActive) throw new InvalidOperationException("A raw input recording is already active.");
        if (setup.sequences.NodeById.Values.Any(n => !n.Predictions.EndFrame.HasValue || n.Predictions.EndFrame >= core.simulator.Frame))
            throw new InvalidOperationException("Raw input requires no unfinished graph actions; existing action claims are never replaced.");
        foreach (var chef in setup.entityRecords.Chefs.Keys)
        {
            int id = chef.path.ids[0];
            if (!chef.existed[core.simulator.Frame] || !registry.TryGetValue(id, out var observed) ||
                observed.Components?.Contains("PlayerControls") != true || observed.Components?.Contains("Rigidbody") != true)
                throw new InvalidOperationException("Raw input requires each live chef's actual native control/body registration.");
        }
        var plan = RawInputPlan.Create(setup, core.simulator.Frame, request, replay);
        if (plan.ExpectedTerminalGameState is not null && observedGameState != GameState.InLevel)
            throw new InvalidOperationException("Expected terminal raw input must start in InLevel.");
        rawWarpToStartOnTerminal = request["warpToStartOnTerminal"]?.GetValue<bool>() == true;
        if (rawWarpToStartOnTerminal && (replay || plan.ExpectedTerminalGameState is null ||
            request["development"]?.GetValue<bool>() != true))
            throw new InvalidOperationException("Terminal auto-warp is original-only and requires expectedTerminalGameState plus development:true.");
        raw = plan; rawStart = core.simulator.Frame; rawEmitted = rawObserved = rawAlignmentCallbacks = 0;
        rawOutcome = "awaiting-resume"; rawError = null; rawWall = null;
        rawObservedTerminalGameState = null; rawTerminalFrame = rawTerminalObservedFrames = null;
        rawTerminalSnapshot = null;
        movementAction = null; stopAt = null;
        core.RequestResume();
    }

    private void PrepareRawExchange(OutputData output)
    {
        if (!RawActive) return;
        // Queue pause before processing the observation of release frame one.
        // The original connector emits its pause request alongside release
        // frame two, then observes that final native frame before acknowledging.
        if (rawEmitted == raw.Frames.Count - 1 && !output.LastFramePaused && core.State == RealGameState.Running && !core.RequestPending)
            core.RequestPause();
    }

    private void ApplyRawExchange(OutputData output, InputData input, GameState priorGameState, GameState[] gameStates)
    {
        if (!RawActive) return;
        int observed = core.simulator.Frame - rawStart;
        rawObserved = Math.Min(rawEmitted, Math.Max(rawObserved, observed));
        if (core.State == RealGameState.Error)
        { RawFail("Framework state transition failed.", input); return; }
        if (rawEmitted == 0 && core.State == RealGameState.AwaitingPhysicsPhaseShiftAlignment &&
            ++rawAlignmentCallbacks > MaximumRawAlignmentCallbacks)
        {
            if (!core.TryCancelResumeBeforeNativeSignal())
                throw new InvalidOperationException("Raw input could not cancel a resume before its native signal.");
            RawFail("Bounded raw-input physics-phase alignment deadline exceeded before native resume.", input);
            return;
        }
        // Alignment may legitimately take longer than input playback when a
        // diagnostic native module makes paused callbacks expensive.  Start
        // the playback deadline at the first accepted logical input boundary,
        // rather than consuming it while native time is still paused.
        if (rawEmitted > 0 && (rawWall.Elapsed.TotalSeconds > 30 + raw.Frames.Count / 15.0 || observed > raw.Frames.Count + 2))
        { RawFail("Bounded raw-input wall/frame deadline exceeded.", input); return; }
        bool terminalLatch = output.__isset.nativeWarpCapabilities && output.NativeWarpCapabilities != null &&
            output.NativeWarpCapabilities.Version == 1 && (output.NativeWarpCapabilities.Features & 2) != 0;
        if (raw.ExpectedTerminalGameState is not null && priorGameState == GameState.InLevel &&
            gameStates.Length == 1 && gameStates[0].ToString() == raw.ExpectedTerminalGameState &&
            terminalLatch && !output.LastFramePaused && output.NextFramePaused &&
            core.State == RealGameState.Paused && !core.RequestPending && input.NextFrame == core.simulator.Frame)
        {
            rawObservedTerminalGameState = gameStates[0].ToString();
            rawTerminalFrame = core.simulator.Frame;
            rawTerminalObservedFrames = rawObserved;
        }
        if (core.State == RealGameState.Paused)
        {
            bool terminal = raw.ExpectedTerminalGameState is not null &&
                rawObservedTerminalGameState == raw.ExpectedTerminalGameState &&
                rawTerminalFrame == core.simulator.Frame && rawTerminalObservedFrames == rawObserved &&
                terminalLatch && !output.LastFramePaused && output.NextFramePaused && rawObserved > 0 &&
                rawObserved < raw.Frames.Count && rawEmitted == rawObserved;
            if (terminal)
            {
                try
                {
                    raw.AcceptTerminalReceipt(rawObserved, rawEmitted);
                    rawOutcome = "terminal"; rawError = null;
                    rawTerminalSnapshot = Inspect(true);
                    if (rawWarpToStartOnTerminal) StartWarp(rawStart, true);
                }
                catch (Exception error)
                {
                    rawOutcome = "failed"; rawError = error.Message;
                }
            }
            else if (raw.ExpectedTerminalGameState is not null)
            {
                rawOutcome = "interrupted";
                rawError = "Native pause did not match the explicitly expected RunLevelOutro terminal boundary.";
            }
            else
            {
                rawOutcome = rawObserved == raw.Frames.Count ? "complete" : "interrupted";
                rawError = rawOutcome == "complete" ? null : "Native pause interrupted the fixed input stream before its release barrier completed.";
            }
            rawWall?.Stop(); return;
        }
        if (core.State is not (RealGameState.Running or RealGameState.AwaitingResume or RealGameState.AwaitingPause) || input.NextFrame != core.simulator.Frame + 1)
            return;
        int index = core.simulator.Frame - rawStart;
        if (index != rawEmitted || index >= raw.Frames.Count)
        { RawFail("Native input frame order differed from the fixed recording.", input); return; }
        var row = raw.Frames[index];
        if (rawEmitted == 0) rawWall = Stopwatch.StartNew();
        input.Input = row.Inputs.ToDictionary(p => p.Key, p => p.Value.DeepCopy());
        foreach (var chef in row.States.Keys)
        {
            setup.entityRecords.Chefs[chef].ChangeTo(row.States[chef], input.NextFrame);
            setup.inputHistory.FrameInputs[chef].ChangeTo(row.History[chef], input.NextFrame);
        }
        rawEmitted++;
        rawOutcome = rawEmitted < raw.PayloadFrames ? "emitting" : "awaiting-pause";
    }

    private void RawFail(string reason, InputData input)
    {
        rawOutcome = "failed"; rawError = reason; rawWall?.Stop();
        input.Input = setup.entityRecords.Chefs.Keys.ToDictionary(c => c.path.ids[0], c => new OneInputData
        {
            Pad = new PadDirection { X = 0, Y = 0 },
            Pickup = new ButtonInput { Down = false, JustPressed = false, JustReleased = setup.entityRecords.Chefs[c][core.simulator.Frame].primaryButtonDown },
            Interact = new ButtonInput { Down = false, JustPressed = false, JustReleased = setup.entityRecords.Chefs[c][core.simulator.Frame].secondaryButtonDown },
            Dash = new ButtonInput { Down = false, JustPressed = false, JustReleased = setup.inputHistory.FrameInputs[c][core.simulator.Frame].dash.isDown }
        });
        input.RequestResume = false;
        input.RequestPause = core.State != RealGameState.Paused;
    }

    private JsonObject RawStatus() => new()
    {
        ["outcome"] = rawOutcome, ["active"] = RawActive, ["error"] = rawError,
        ["startFrame"] = raw is null ? null : rawStart,
        ["payloadFrames"] = raw?.PayloadFrames, ["totalFramesIncludingRelease"] = raw?.Frames.Count,
        ["emittedFrames"] = rawEmitted, ["observedFrames"] = rawObserved,
        ["alignmentCallbacks"] = rawAlignmentCallbacks,
        ["recordingSha256"] = raw?.Recording["sha256"]?.ToString(),
        ["expectedTerminalGameState"] = raw?.ExpectedTerminalGameState,
        ["observedTerminalGameState"] = rawObservedTerminalGameState,
        ["terminalFrame"] = rawTerminalFrame,
        ["terminalObservedFrames"] = raw?.TerminalObservedFrames,
        ["terminalEmittedFrames"] = raw?.TerminalEmittedFrames,
        ["warpToStartOnTerminal"] = rawWarpToStartOnTerminal,
        ["completionMeaning"] = rawOutcome == "terminal"
            ? "The explicit pristine RunLevelOutro latch paused the exact advancing callback; every emitted input in the receipted prefix was consumed."
            : "Fixed logical inputs plus two neutral release/pause frames observed; no task-specific interaction success is inferred."
    };

    private JsonObject TerminalInspect()
    {
        if (rawTerminalSnapshot is null)
            throw new InvalidOperationException("No retained pristine terminal snapshot is available.");
        return (JsonObject)rawTerminalSnapshot.DeepClone();
    }

    private JsonObject ExportRawRecording(JsonObject request)
    {
        if (raw is null || rawOutcome is not ("complete" or "terminal")) throw new InvalidOperationException("Only a fully observed release boundary or explicit terminal receipt can be exported for replay.");
        string path = Path.GetFullPath(Path.Combine(evidenceRoot, request["path"]?.ToString() ?? "raw-input.json"));
        if (!path.StartsWith(evidenceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Recording path must remain under evidence root.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write))) file.Write(raw.Recording.ToJsonString());
        return new JsonObject { ["ok"] = true, ["path"] = path, ["rawInput"] = RawStatus() };
    }
}
