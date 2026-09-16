using Hpmv;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;
using Thrift.Protocol;
using Thrift.Transport.Client;

namespace Supercharged.Headless;

/// <summary>Starts only a test-owned host and a fake game endpoint on OS-assigned loopback ports.</summary>
public static class TransportFixture
{
    public static async Task<JsonObject> Run(string hostDll, string evidenceRoot)
    {
        string directory = Path.Combine(evidenceRoot, "transport-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string trace = Path.Combine(directory, "exchange.jsonl");
        using var game = new TcpListener(IPAddress.Loopback, 0); game.Start(); int gamePort = ((IPEndPoint)game.LocalEndpoint).Port;
        int controlPort; using (var reservation = new TcpListener(IPAddress.Loopback, 0)) { reservation.Start(); controlPort = ((IPEndPoint)reservation.LocalEndpoint).Port; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { Path.GetFullPath(hostDll), "serve", "--game-port", gamePort.ToString(), "--control-port", controlPort.ToString(), "--evidence-root", directory, "--trace", trace }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Test host failed to start.");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        JsonObject result = null;
        try
        {
            using var connection = await game.AcceptTcpClientAsync(deadline.Token);
            using var transport = new TStreamTransport(connection.GetStream(), connection.GetStream(), new Thrift.TConfiguration());
            using var client = new Interceptor.Client(new TBinaryProtocol(transport));
            long headerBytes = LiveBytes(trace); var header = JsonNode.Parse(LiveLines(trace).First())!.AsObject();
            if (header["kind"]?.ToString() != "session" || headerBytes == 0) throw new InvalidDataException("Live trace header missing before first exchange.");
            OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Items = new(), Chefs = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
            await client.getNext(Frame(new ServerMessage { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage { m_Scene = "s_Day_3_4", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() }), deadline.Token);
            var first = Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() });
            using var fixture = typeof(TransportFixture).Assembly.GetManifestResourceStream("Headless.Reference.NativeDInitialRegistryFixture.json")!;
            first.EntityRegistry = JsonNode.Parse(fixture)!["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
            await client.getNext(first, deadline.Token); await client.getNext(Frame(), deadline.Token);
            var pause = Frame(); pause.NextFramePaused = true; await client.getNext(pause, deadline.Token);
            var inspection = await RuntimeHost.Send(controlPort, new() { ["command"] = "inspect", ["full"] = true }, deadline.Token);
            if (inspection["state"]?.ToString() != "Paused" || inspection["frame"]?.GetValue<int>() != 2) throw new InvalidDataException("HTTP inspection disagrees with actual fake-Thrift lifecycle.");
            for (int i = 0; i < 300; i++)
            {
                var idle = Frame(); idle.LastFramePaused = idle.NextFramePaused = true; idle.FrameNumber = 2;
                idle.FramesSinceLastNoPhysicsFrame = i % 6;
                await client.getNext(idle, deadline.Token);
            }
            var accepted = await RuntimeHost.Send(controlPort, new() { ["command"] = "step", ["frames"] = 2 }, deadline.Token);
            if (accepted["acceptedCommand"]?.ToString() != "step") throw new InvalidDataException("HTTP step command rejected.");
            var still = Frame(); still.LastFramePaused = still.NextFramePaused = true; await client.getNext(still, deadline.Token);
            var aligned = still.DeepCopy(); aligned.FramesSinceLastNoPhysicsFrame = 4;
            await client.getNext(aligned, deadline.Token);
            var resumed = Frame(); resumed.LastFramePaused = true; await client.getNext(resumed, deadline.Token);
            await client.getNext(Frame(), deadline.Token); await client.getNext(pause, deadline.Token);
            var physicalRows = LiveLines(trace);
            var rows = TraceStore.Expand(physicalRows).Select(l => JsonNode.Parse(l)!.AsObject()).ToArray();
            int exchanges = rows.Count(r => r["kind"]?.ToString() == "exchange"), controls = rows.Count(r => r["kind"]?.ToString() == "control");
            if (exchanges != 309 || controls != 1 || rows.Length != 311 || !physicalRows.Any(l => JsonNode.Parse(l)!["kind"]?.ToString() == "paused-exchanges")) throw new InvalidDataException("Live trace is incomplete: " + rows.Length + " records.");
            long liveBytes = LiveBytes(trace);
            result = new() { ["ok"] = true, ["gameCalls"] = 0, ["testOwnedHostPid"] = child.Id, ["gamePort"] = gamePort, ["controlPort"] = controlPort,
                ["hostDll"] = Path.GetFullPath(hostDll), ["hostSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(hostDll))),
                ["tracePath"] = trace, ["headerBytesWhileHostAlive"] = headerBytes, ["traceBytesWhileHostAlive"] = liveBytes,
                ["exchanges"] = exchanges, ["controls"] = controls, ["records"] = rows.Length, ["physicalRecords"] = physicalRows.Length,
                ["qualification"] = "Synthetic HTTP/Thrift process fixture; no native game endpoint connected." };
        }
        finally
        {
            // This is the exact Process object created above, never a searched
            // process or game PID. Its test artifacts remain for inspection.
            if (!child.HasExited) child.Kill(entireProcessTree: false);
            await child.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), await stderr);
        }
        result!["traceBytesAfterHostExit"] = new FileInfo(trace).Length;
        result["traceSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(trace)));
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), result.ToJsonString()); return result;
    }
    private static long LiveBytes(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return file.Length; }
    private static string[] LiveLines(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var reader = new StreamReader(file); return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries); }
}
