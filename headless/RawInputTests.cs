using Google.Protobuf;
using Hpmv;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class RawInputTests
{
    public static int Run(string evidenceRoot)
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Raw fixture: " + name); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException) { checks++; return; } throw new InvalidOperationException("Raw rejection missing: " + name); }
        JsonObject Pad(double x = 0, double y = 0, bool pickup = false, bool interact = false, bool dash = false) => new() { ["x"] = x, ["y"] = y, ["pickup"] = pickup, ["interact"] = interact, ["dash"] = dash };
        JsonObject Segment(int frames, bool active) => new() { ["frames"] = frames, ["chefs"] = new JsonObject {
            ["103"] = active ? Pad(.25, 0, interact: true) : Pad(), ["104"] = active ? Pad(pickup: true) : Pad(),
            ["105"] = active ? Pad(dash: true) : Pad(), ["106"] = active ? Pad(-1, 1, true, true) : Pad() } };
        var request = new JsonObject { ["command"] = "raw-input", ["segments"] = new JsonArray(Segment(3, true), Segment(2, false), Segment(1, true)) };
        var setup = new Carnival34FourLevel(); byte[] before = setup.ToProto().ToByteArray();
        var plan = RawInputPlan.Create(setup, 0, request, false);
        Check(before.SequenceEqual(setup.ToProto().ToByteArray()), "preflight leaves original setup, graph and history untouched");
        Check(plan.PayloadFrames == 6 && plan.Frames.Count == 8, "two neutral frames counted explicitly");
        Check(plan.Frames[0].Inputs[103].Pad.X == .25 && plan.Frames[0].Inputs[103].Interact.JustPressed, "movement plus interaction");
        Check(plan.Frames[0].Inputs[106].Pickup.JustPressed && plan.Frames[0].Inputs[106].Interact.JustPressed, "simultaneous logical edges supported");
        Check(plan.Frames[1].Inputs[106].Pickup.Down && plan.Frames[1].Inputs[106].Interact.Down && !plan.Frames[1].Inputs[106].Pickup.JustPressed, "held buttons do not repeat edges");
        Check(plan.Frames[0].Inputs[105].Dash.JustPressed && plan.Frames[1].Inputs[105].Dash.Down && !plan.Frames[1].Inputs[105].Dash.JustPressed, "dash rise and hold recorded");
        Check(plan.Frames[3].Inputs[105].Dash.JustReleased && !plan.Frames[4].Inputs[105].Dash.JustReleased, "dash release exactly once");
        var dashChef=setup.entityRecords.Chefs.Keys.Single(c=>c.path.ids[0]==105);
        Check(plan.Frames.Take(3).All(f=>f.States[dashChef].dashButtonDown) && !plan.Frames[3].States[dashChef].dashButtonDown,
              "raw held dash also persists in controller checkpoint state");
        var (_,typedAfterRawHold)=plan.Frames[1].States[dashChef].ApplyInputAndAdvanceFrame(new DesiredControllerInput {dash=true});
        Check(typedAfterRawHold.dash.isDown && !typedAfterRawHold.dash.justPressed,"raw-to-typed held continuation does not invent dash edge");
        var (_,typedAfterRawRelease)=plan.Frames[6].States[dashChef].ApplyInputAndAdvanceFrame(new DesiredControllerInput {dash=true});
        Check(typedAfterRawRelease.dash.isDown && typedAfterRawRelease.dash.justPressed,"raw release permits next real typed dash edge");
        Check(plan.Frames[5].Inputs[104].Pickup.JustPressed, "no artificial pickup cooldown postpones raw input");
        Check(plan.Frames[6].Inputs[106].Pickup.JustReleased && plan.Frames[6].Inputs[106].Interact.JustReleased && !plan.Frames[7].Inputs[106].Interact.JustReleased, "both tail release edges retained");
        var replay = RawInputPlan.Create(setup, 0, new() { ["recording"] = plan.Recording.DeepClone() }, true);
        Check(replay.Frames.Zip(plan.Frames).All(pair => pair.First.Inputs.All(p => p.Value.Equals(pair.Second.Inputs[p.Key]))), "all recorded pads replay exactly without feedback");
        var damaged = (JsonObject)plan.Recording.DeepClone(); damaged["payloadFrames"] = 7;
        Reject(() => RawInputPlan.Create(setup, 0, new() { ["recording"] = damaged }, true), "checksum mutation");
        damaged = (JsonObject)plan.Recording.DeepClone(); damaged["frames"]![1]!["inputs"]!["104"]!["Pickup"]!["JustPressed"] = true;
        damaged.Remove("sha256"); damaged["sha256"] = RawInputPlan.Hash(damaged);
        Reject(() => RawInputPlan.Create(setup, 0, new() { ["recording"] = damaged }, true), "rehashed forged button edge");
        var missing = (JsonObject)request.DeepClone(); missing["segments"]![0]!["chefs"]!.AsObject().Remove("103");
        Reject(() => RawInputPlan.Create(setup, 0, missing, false), "missing chef");
        var axis = (JsonObject)request.DeepClone(); axis["segments"]![0]!["chefs"]!["103"]!["x"] = 1.01;
        Reject(() => RawInputPlan.Create(setup, 0, axis, false), "axis range");
        var boolean = (JsonObject)request.DeepClone(); boolean["segments"]![0]!["chefs"]!["103"]!.AsObject().Remove("pickup");
        Reject(() => RawInputPlan.Create(setup, 0, boolean, false), "explicit logical values required");
        var changed = new Carnival34FourLevel(); var changedChef = changed.entityRecords.Chefs.Keys.First(); var state = changed.entityRecords.Chefs[changedChef][0]; state.primaryButtonDown = true; changed.entityRecords.Chefs[changedChef].ChangeTo(state, 0);
        Reject(() => RawInputPlan.Create(changed, 0, new() { ["recording"] = plan.Recording.DeepClone() }, true), "initial controller mismatch");
        var terminalRequest = (JsonObject)request.DeepClone(); terminalRequest["expectedTerminalGameState"] = "RunLevelOutro";
        var terminalPlan = RawInputPlan.Create(setup, 0, terminalRequest, false);
        Check(terminalPlan.ExpectedTerminalGameState == "RunLevelOutro" && terminalPlan.TerminalObservedFrames is null,
            "explicit terminal starts without inventing an observation receipt");
        terminalPlan.AcceptTerminalReceipt(2, 2);
        var terminalReplayRequest = new JsonObject { ["recording"] = terminalPlan.Recording.DeepClone(), ["expectedTerminalGameState"] = "RunLevelOutro" };
        var terminalReplayPlan = RawInputPlan.Create(setup, 0, terminalReplayRequest, true);
        Check(terminalReplayPlan.TerminalObservedFrames == 2 && terminalReplayPlan.TerminalEmittedFrames == 2,
            "terminal recording retains an exactly consumed observed prefix");
        Reject(() => RawInputPlan.Create(setup, 0, new() { ["recording"] = terminalPlan.Recording.DeepClone() }, true),
            "terminal replay requires explicit authority");
        var wrongTerminal = (JsonObject)request.DeepClone(); wrongTerminal["expectedTerminalGameState"] = "RanLevelOutro";
        Reject(() => RawInputPlan.Create(setup, 0, wrongTerminal, false), "unsupported terminal state");
        var forgedTerminal = (JsonObject)terminalPlan.Recording.DeepClone(); forgedTerminal["terminalEmittedFrames"] = 3;
        forgedTerminal.Remove("sha256"); forgedTerminal["sha256"] = RawInputPlan.Hash(forgedTerminal);
        Reject(() => RawInputPlan.Create(setup, 0, new() { ["recording"] = forgedTerminal, ["expectedTerminalGameState"] = "RunLevelOutro" }, true),
            "terminal receipt rejects a speculative unconsumed input");

        OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Items = new(), Chefs = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
        ServerMessage Load() => new() { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage { m_Scene = "s_Day_3_4", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() };
        ServerMessage Start() => new() { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() };
        using var stream = typeof(RawInputTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeDInitialRegistryFixture.json")!;
        var registry = JsonNode.Parse(stream)!["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        var nativeSetup = new Carnival34FourLevel(); var session = new HeadlessSession(nativeSetup, "synthetic raw input lifecycle with captured registrations", evidenceRoot);
        InputData Send(OutputData frame) => session.getNext(frame).GetAwaiter().GetResult();
        Send(Frame(Load())); var first = Frame(Start()); first.EntityRegistry = registry; Send(first); Send(Frame());
        var pause = Frame(); pause.NextFramePaused = true; Send(pause);
        var expected = RawInputPlan.Create(nativeSetup, 2, request, false);
        session.Command(request);
        Reject(() => session.Command(new() { ["command"] = "record-input" }), "unfinished recording cannot export");
        var still = Frame(); still.LastFramePaused = still.NextFramePaused = true; Send(still);
        var aligned = still.DeepCopy(); aligned.FramesSinceLastNoPhysicsFrame = 4;
        var plainResume = Send(aligned);
        Check(plainResume.RequestResume && plainResume.__isset.gameSpeed && plainResume.GameSpeed == 1005.0 &&
              (plainResume.Input == null || plainResume.Input.Count == 0),
            "native-gated resume carries the saved phase metadata and no first raw input");
        var resumed = Frame(); resumed.LastFramePaused = true;
        var emitted = new List<InputData> { Send(resumed) };
        for (int i = 1; i < expected.Frames.Count; i++) emitted.Add(Send(Frame()));
        Check(emitted.Count == 8 && emitted.SelectMany((input, i) => input.Input.Select(p => p.Value.Equals(expected.Frames[i].Inputs[p.Key]))).All(b => b), "session emits exact planned logical inputs through original request engine");
        Check(emitted[^1].RequestPause && emitted[^1].NextFrame == 10, "final neutral row requests native pause at exact target");
        Send(pause); var inspection = session.Inspect(true);
        Check(inspection["rawInput"]!["outcome"]!.ToString() == "complete" && inspection["frame"]!.GetValue<int>() == 10 && inspection["rawInput"]!["observedFrames"]!.GetValue<int>() == 8, "all eight actual callbacks observed before completion");
        Check(inspection["rawInput"]!["alignmentCallbacks"]!.GetValue<int>() == 1, "raw status retains bounded physics-alignment callback count");
        var c106 = nativeSetup.entityRecords.Chefs.Keys.Single(c => c.path.ids[0] == 106);
        Check(nativeSetup.inputHistory.FrameInputs[c106][3].primary.justPressed && nativeSetup.inputHistory.FrameInputs[c106][3].secondary.justPressed, "original history retains simultaneous raw edges");
        Check(inspection["entities"]!.AsArray().All(e => e!["rotation"] is JsonObject && e["angularVelocity"] is JsonObject), "full snapshots retain rotation and angular velocity");
        string name = "offline-tests/raw-" + Guid.NewGuid().ToString("N") + ".json";
        var receipt = session.Command(new() { ["command"] = "record-input", ["path"] = name });
        Check(File.Exists(receipt["path"]!.ToString()) && JsonNode.Parse(File.ReadAllBytes(receipt["path"]!.ToString()))!["sha256"]!.ToString() == expected.Recording["sha256"]!.ToString(), "only observed completed stream exports unchanged checksum");
        session.Command(request); session.ConnectionEnded("synthetic disconnect");
        Check(session.Inspect()["rawInput"]!["outcome"]!.ToString() == "interrupted", "disconnect cannot leave old raw stream active");
        Reject(() => session.Command(new() { ["command"] = "record-input" }), "interrupted recording cannot export");

        var cancellationSetup = new Carnival34FourLevel();
        var cancellation = new RealGameConnector(cancellationSetup);
        cancellation.getNext(Frame(Load())).GetAwaiter().GetResult();
        cancellation.getNext(Frame(Start())).GetAwaiter().GetResult();
        cancellation.getNext(Frame()).GetAwaiter().GetResult();
        cancellation.State = RealGameState.Paused;
        cancellation.RequestResume();
        cancellation.getNext(still).GetAwaiter().GetResult();
        Check(cancellation.State == RealGameState.AwaitingPhysicsPhaseShiftAlignment && cancellation.RequestPending,
            "synthetic resume enters exact phase-alignment wait before any native signal");
        Check(cancellation.TryCancelResumeBeforeNativeSignal() && cancellation.State == RealGameState.Paused && !cancellation.RequestPending,
            "pre-signal cancellation returns to a settled paused controller");
        Check(!cancellation.getNext(aligned).GetAwaiter().GetResult().RequestResume,
            "cancelled resume cannot later escape when the matching native phase arrives");
        Check(!cancellation.TryCancelResumeBeforeNativeSignal(), "settled state cannot cancel a nonexistent resume");

        ServerMessage Outro() => new() { Type = (int)MessageType.GameState,
            Message = new GameStateMessage { m_State = GameState.RunLevelOutro }.ToBytes() };
        JsonObject StorySegment(int frames) => new() { ["frames"] = frames, ["chefs"] = new JsonObject {
            ["43"] = Pad(), ["44"] = Pad(), ["45"] = Pad(), ["46"] = Pad() } };
        var storyTerminalRequest = new JsonObject { ["command"] = "raw-input",
            ["segments"] = new JsonArray(StorySegment(6)), ["expectedTerminalGameState"] = "RunLevelOutro" };
        var terminalSetup = new Story11FourLevel();
        var terminalSession = new HeadlessSession(terminalSetup, "synthetic explicit raw terminal", evidenceRoot);
        InputData SendTerminal(OutputData frame) => terminalSession.getNext(frame).GetAwaiter().GetResult();
        using var storyStream = typeof(RawInputTests).Assembly.GetManifestResourceStream("Headless.Reference.Story11NativeRegistryFixture.json")!;
        var storyFixture = JsonNode.Parse(storyStream)!;
        var storyRegistry = storyFixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        var storySettledRegistry = storyFixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        SendTerminal(Frame(Load())); var terminalStart = Frame(Start()); terminalStart.EntityRegistry = storyRegistry;
        SendTerminal(terminalStart); var terminalSettled = Frame(); terminalSettled.EntityRegistry = storySettledRegistry;
        SendTerminal(terminalSettled); SendTerminal(pause);
        var terminalAutoRequest = (JsonObject)storyTerminalRequest.DeepClone();
        terminalAutoRequest["warpToStartOnTerminal"] = true; terminalAutoRequest["development"] = true;
        terminalSession.Command(terminalAutoRequest); SendTerminal(still); SendTerminal(aligned);
        var terminalResume = Frame(); terminalResume.LastFramePaused = true; SendTerminal(terminalResume);
        var pristineTerminal = Frame(Outro()); pristineTerminal.NextFramePaused = true;
        pristineTerminal.NativeWarpCapabilities = new NativeWarpCapabilities { Version = 1, Features = 3 };
        SendTerminal(pristineTerminal);
        var terminalStatus = terminalSession.Inspect(true); var terminalRaw = terminalStatus["rawInput"]!;
        Check(terminalRaw["outcome"]!.ToString() == "terminal" && terminalStatus["frame"]!.GetValue<int>() == 3 &&
              terminalRaw["terminalFrame"]!.GetValue<int>() == 3 && terminalRaw["terminalObservedFrames"]!.GetValue<int>() == 1 &&
              terminalRaw["terminalEmittedFrames"]!.GetValue<int>() == 1 && terminalStatus["requestPending"]!.GetValue<bool>(),
            "pristine advancing InLevel-to-RunLevelOutro latch accepts only the consumed prefix and queues an immediate rewind");
        Check(terminalSession.Command(new() { ["command"] = "terminal-inspect" })["frame"]!.GetValue<int>() == 3,
            "terminal inspection is retained independently of the imminent rewind");
        string terminalName = "offline-tests/raw-terminal-" + Guid.NewGuid().ToString("N") + ".json";
        var terminalExport = terminalSession.Command(new() { ["command"] = "record-input", ["path"] = terminalName });
        var terminalRecording = JsonNode.Parse(File.ReadAllBytes(terminalExport["path"]!.ToString()))!.AsObject();
        Check(terminalRecording["terminalObservedFrames"]!.GetValue<int>() == 1 &&
              terminalRecording["sha256"]!.ToString() == terminalRaw["recordingSha256"]!.ToString(),
            "explicit terminal receipt exports with its recomputed checksum");
        var warpDirective = SendTerminal(Frame());
        Check(warpDirective.Warp != null && warpDirective.Warp.Frame == 2,
            "first held callback emits the queued target rewind without a host control round trip");
        var restoredAck = Frame(); restoredAck.FrameNumber = 2; restoredAck.LastFramePaused = restoredAck.NextFramePaused = true;
        restoredAck.NativeWarpCapabilities = new NativeWarpCapabilities { Version = 1, Features = 5 };
        SendTerminal(restoredAck);
        Check(terminalSession.Inspect()["state"]!.ToString() == "Paused" && !terminalSession.Inspect()["requestPending"]!.GetValue<bool>(),
            "native lifecycle acknowledgement settles the terminal auto-warp at its start frame");
        terminalSession.Command(new JsonObject { ["command"] = "raw-replay", ["recording"] = terminalRecording.DeepClone(),
            ["expectedTerminalGameState"] = "RunLevelOutro" });
        Check(terminalSession.Inspect()["rawInput"]!["active"]!.GetValue<bool>(),
            "lifecycle-restored acknowledgement permits immediate terminal replay admission");
        SendTerminal(still); SendTerminal(aligned); SendTerminal(terminalResume);
        var replayTerminal = Frame(Outro()); replayTerminal.NextFramePaused = true;
        replayTerminal.NativeWarpCapabilities = new NativeWarpCapabilities { Version = 1, Features = 3 };
        SendTerminal(replayTerminal);
        var replayTerminalStatus = terminalSession.Command(new() { ["command"] = "terminal-inspect" });
        Check(replayTerminalStatus["rawInput"]!["outcome"]!.ToString() == "terminal" &&
              replayTerminalStatus["rawInput"]!["terminalObservedFrames"]!.GetValue<int>() == 1 &&
              !terminalSession.Inspect()["requestPending"]!.GetValue<bool>(),
            "terminal replay reaches the same consumed-prefix latch without queuing another warp");
        var heldStill = still.DeepCopy(); heldStill.NativeWarpCapabilities = new NativeWarpCapabilities { Version = 1, Features = 3 };
        Check(SendTerminal(heldStill).Warp is null,
            "a held replay terminal emits no implicit second rewind");
        Check(terminalSession.Command(new() { ["command"] = "terminal-inspect" })["frame"]!.GetValue<int>() == 3,
            "replay terminal inspection remains stable across held callbacks");

        var unrequestedSetup = new Carnival34FourLevel();
        var unrequested = new HeadlessSession(unrequestedSetup, "synthetic unrequested terminal", evidenceRoot);
        InputData SendUnrequested(OutputData frame) => unrequested.getNext(frame).GetAwaiter().GetResult();
        SendUnrequested(Frame(Load())); var unrequestedStart = Frame(Start()); unrequestedStart.EntityRegistry = registry;
        SendUnrequested(unrequestedStart); SendUnrequested(Frame()); SendUnrequested(pause);
        unrequested.Command(request); SendUnrequested(still); SendUnrequested(aligned);
        var unrequestedResume = Frame(); unrequestedResume.LastFramePaused = true; SendUnrequested(unrequestedResume);
        var unrequestedTerminal = Frame(Outro()); unrequestedTerminal.NextFramePaused = true;
        unrequestedTerminal.NativeWarpCapabilities = new NativeWarpCapabilities { Version = 1, Features = 3 };
        SendUnrequested(unrequestedTerminal);
        Check(unrequested.Inspect()["rawInput"]!["outcome"]!.ToString() == "interrupted",
            "unrequested round-end pause remains an interruption");
        Reject(() => unrequested.Command(new() { ["command"] = "resume" }),
            "held round-end latch rejects an unsafe ordinary resume");
        Reject(() => unrequested.Command(new() { ["command"] = "warp", ["frame"] = 2, ["development"] = true }),
            "held round-end latch rejects a warp without its exact terminal receipt");
        Reject(() => unrequested.Command(new() { ["command"] = "record-input" }),
            "unrequested terminal cannot export a partial recording");

        var gatedSetup = new Carnival34FourLevel();
        var gated = new HeadlessSession(gatedSetup, "synthetic native-gated raw resume", evidenceRoot);
        InputData SendGated(OutputData frame) => gated.getNext(frame).GetAwaiter().GetResult();
        SendGated(Frame(Load())); var gatedStart = Frame(Start()); gatedStart.EntityRegistry = registry;
        SendGated(gatedStart); SendGated(Frame()); SendGated(pause); gated.Command(request); SendGated(still);
        var gatedReply = SendGated(still);
        Check(gatedReply.RequestResume && gatedReply.__isset.gameSpeed && gatedReply.GameSpeed == 1005.0 &&
              gated.Inspect()["state"]!.ToString() == "AwaitingResume" &&
              gated.Inspect()["rawInput"]!["emittedFrames"]!.GetValue<int>() == 0,
            "RPC phase alias cannot delay or alter the controller-authored native target metadata");
        return checks+ControllerInputTests.Run();
    }
}
