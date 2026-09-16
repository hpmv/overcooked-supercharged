using Hpmv;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class TraceTests
{
    private sealed class FailingStream : MemoryStream
    {
        public bool Fail;
        public int Attempts;
        public override void Write(byte[] buffer, int offset, int count)
        { Attempts++; if (Fail) throw new IOException("Injected disk full"); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Attempts++; if (Fail) throw new IOException("Injected disk full"); base.Write(buffer); }
    }
    public static JsonObject Run(string evidenceRoot)
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Trace fixture: " + name); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException) { checks++; return; } throw new InvalidOperationException("Expected trace rejection: " + name); }
        string[] Lines(MemoryStream stream) => Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Chefs = new(), Items = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
        var input = new InputData { NextFrame = 300, Input = new(), PreventInvalidState = false };
        var paused = Frame(); paused.FrameNumber = 300; paused.LastFramePaused = paused.NextFramePaused = true;
        paused.Items[84] = new ItemData { Pos = new() { X = 17.123456789, Y = .6, Z = -10.999 }, Rotation = new() { W = .99999999 }, AngularVelocity = new() { Y = .000001 } };
        paused.ServerMessages.Add(new() { Type = 1000, Message = Encoding.UTF8.GetBytes(new string('x', 8000)) });
        using var memory = new MemoryStream(); using var trace = new TraceStore(memory);
        trace.Header(new { kind = "session", version = 2 });
        var expected = new List<string>();
        bool memoryBounded = true;
        for (int i = 0; i < 777; i++)
        {
            paused.FramesSinceLastNoPhysicsFrame = i % 6; paused.PhysicsFramesElapsed = i % 2;
            paused.Items[84].Rotation.X = i % 11 * .0000001; // Retain even small raw paused physics changes.
            expected.Add(JsonSerializer.Serialize(new { kind = "exchange", output = paused, input }, RuntimeHost.Json));
            trace.Exchange(paused, input, true);
            memoryBounded &= trace.Status()["bufferedPausedBytes"]!.GetValue<long>() <= TraceStore.BlockBytes;
        }
        trace.Control(new() { ["command"] = "step", ["frames"] = 2 });
        Check(memoryBounded, "bounded paused memory across all777callbacks");
        var expanded = TraceStore.Expand(Lines(memory)).ToArray();
        Check(expanded.Skip(1).Take(777).SequenceEqual(expected), "exact paused callbacks/phase/float/input bytes expand in order");
        Check(JsonNode.Parse(expanded[^1])!["kind"]!.ToString() == "control", "control flushes prior paused evidence");
        long uncompressed = expected.Sum(s => Encoding.UTF8.GetByteCount(s) + 1);
        Check(memory.Length < uncompressed / 40, "bounded paused compression removes repeated payload volume");
        Check(trace.Status()["persistedExchanges"]!.GetValue<long>() == 777 && trace.Status()["bufferedPausedExchanges"]!.GetValue<int>() == 0, "exact persisted callback counts");
        foreach (int boundary in Enumerable.Range(0, 5))
        {
            var output = paused.DeepCopy(); var outgoing = input.DeepCopy(); bool settled = true;
            if (boundary == 0) output.LastFramePaused = false;
            if (boundary == 1) output.NextFramePaused = false;
            if (boundary == 2) outgoing.RequestResume = true;
            if (boundary == 3) outgoing.RequestPause = true;
            if (boundary == 4) settled = false;
            trace.Exchange(output, outgoing, settled);
            Check(JsonNode.Parse(Lines(memory)[^1])!["kind"]!.ToString() == "exchange", "advancing/request/transition immediate row " + boundary);
        }
        string block = Lines(memory).First(s => JsonNode.Parse(s)!["kind"]!.ToString() == "paused-exchanges");
        foreach (string field in new[] { "sha256", "count", "uncompressedBytes", "version" })
        {
            var mutated = JsonNode.Parse(block)!.AsObject();
            if (field == "sha256") mutated[field] = new string('0', 64);
            else mutated[field] = field == "count" ? 257 : field == "uncompressedBytes" ? TraceStore.BlockBytes + 1 : 2;
            Reject(() => TraceStore.Expand(new[] { mutated.ToJsonString() }).ToArray(), "corrupt compressed receipt " + field);
        }
        using var limitedStream = new MemoryStream(); using var limited = new TraceStore(limitedStream, maximumBytes: 2);
        Reject(() => limited.Header(new { kind = "session" }), "byte ceiling rejects before partial write");
        Check(limitedStream.Length == 0 && limited.Status()["failed"]!.GetValue<bool>(), "byte ceiling latches evidence failure");
        using var reservedStream = new MemoryStream(); using var reserved = new TraceStore(reservedStream, reserveBytes: 10, availableBytes: () => 12);
        Reject(() => reserved.Header(new { kind = "session" }), "low storage reserve fails before writing");
        using var disk = new FailingStream(); using var broken = new TraceStore(disk);
        broken.Header(new { kind = "session" }); disk.Fail = true;
        Reject(() => broken.Header(new { kind = "test" }), "real IO exception becomes evidence failure");
        int attempts = disk.Attempts;
        Reject(() => broken.Header(new { kind = "test" }), "failed writer never retries");
        Check(disk.Attempts == attempts, "no repeated disk-error spam");
        var setup = new Carnival34FourLevel(); var session = new HeadlessSession(setup, "synthetic trace failure", evidenceRoot);
        session.getNext(Frame(new ServerMessage { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage { m_Scene = "s_Day_3_4", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() })).GetAwaiter().GetResult();
        session.getNext(Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() })).GetAwaiter().GetResult();
        session.AttachTrace(broken);
        var response = session.getNext(Frame()).GetAwaiter().GetResult();
        Check(session.TraceFailed && response.RequestPause && !response.RequestResume && response.Input.Count == 4, "disk failure returns explicit four-chef pause fence");
        Check(response.Input.Values.All(p => p.Pad.X == 0 && p.Pad.Y == 0 && !p.Pickup.Down && !p.Interact.Down && !p.Dash.Down) && !response.__isset.warp && !response.__isset.resetOrderSeed, "failure fence is neutral and has no unrelated control");
        int stoppedFrame = session.Inspect()["frame"]!.GetValue<int>();
        for (int i = 0; i < 3; i++) session.getNext(Frame()).GetAwaiter().GetResult();
        Check(session.Inspect()["frame"]!.GetValue<int>() == stoppedFrame && session.Inspect()["errors"]!.AsArray().Count == 1 && disk.Attempts == attempts, "latched session stops reconstruction and repeated diagnostics");
        Check(session.Inspect()["ok"]!.GetValue<bool>() == false && session.Inspect()["traceFailure"] is not null, "inspect explicitly fails evidence qualification");
        Reject(() => session.Command(new() { ["command"] = "resume" }), "storage failure blocks future control");
        Reject(() => session.Command(new() { ["command"] = "checkpoint", ["path"] = "must-not-write.pb" }), "storage failure blocks successful-looking checkpoint");
        session.ConnectionEnded();
        Check(session.Inspect()["connected"]!.GetValue<bool>() == false, "transport closes after failure response");
        return new() { ["ok"] = true, ["checks"] = checks, ["pausedCallbacks"] = 777, ["uncompressedBytes"] = uncompressed,
            ["compressedTraceBytes"] = memory.Length, ["gameCalls"] = 0, ["qualification"] = "Offline exact callback roundtrip, bounded storage and injected disk failure; no native control." };
    }
}
