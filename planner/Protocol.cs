using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed class TasClient : IAsyncDisposable
{
    private readonly TcpClient socket = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private NetworkStream? stream;
    public const int ProtocolVersion = 1;
    public const int MaxMessageBytes = 32 * 1024 * 1024;

    public async Task ConnectAsync(string host, int port, CancellationToken token)
    {
        if (!System.Net.IPAddress.TryParse(host, out var address) || !System.Net.IPAddress.IsLoopback(address))
            throw new ArgumentException("Host must be a numeric loopback address.");
        await socket.ConnectAsync(address, port, token);
        socket.NoDelay = true;
        stream = socket.GetStream();
    }

    public async Task<JsonObject> CallAsync(JsonObject request, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (stream is null) throw new InvalidOperationException("Not connected.");
            request["version"] = ProtocolVersion;
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, Json.Options);
            await WriteFrameAsync(stream, payload, token);
            var response = JsonNode.Parse(await ReadFrameAsync(stream, token))?.AsObject()
                ?? throw new InvalidDataException("Response is empty.");
            if (response["version"]?.GetValue<int>() != ProtocolVersion)
                throw new InvalidDataException("Unsupported response protocol version.");
            return response;
        }
        finally { gate.Release(); }
    }

    public static async Task WriteFrameAsync(Stream target, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if (payload.Length is <= 0 or > MaxMessageBytes) throw new InvalidDataException("Invalid message size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await target.WriteAsync(header, token);
        await target.WriteAsync(payload, token);
        await target.FlushAsync(token);
    }

    public static async Task<byte[]> ReadFrameAsync(Stream source, CancellationToken token)
    {
        var header = new byte[4];
        await source.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes) throw new InvalidDataException($"Invalid frame length {length}.");
        var buffer = new byte[length];
        await source.ReadExactlyAsync(buffer, token);
        return buffer;
    }

    public ValueTask DisposeAsync()
    {
        stream?.Dispose();
        socket.Dispose();
        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public static JsonObject Object(string text) => JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("Expected JSON object.");
    public static JsonObject Request(string command) => new() { ["version"] = 1, ["command"] = command };
    public static void RequireOk(JsonObject response)
    {
        if (response["ok"]?.GetValue<bool>() != true)
            throw new InvalidOperationException(response["error"]?.ToString() ?? "The game rejected the request.");
    }
    public static JsonNode? At(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is JsonObject obj) node = obj[part];
            else if (node is JsonArray arr && int.TryParse(part, out var i) && i >= 0 && i < arr.Count) node = arr[i];
            else return null;
        }
        return node;
    }
}

public static class Inputs
{
    public static JsonObject Neutral(int player) => new()
    {
        ["player"] = player, ["x"] = 0, ["y"] = 0,
        ["pickup"] = false, ["use"] = false, ["dash"] = false
    };
    public static JsonArray AllNeutral() => new(Enumerable.Range(0, 4).Select(p => (JsonNode)Neutral(p)).ToArray());
    public static JsonObject Step(int steps, JsonArray? inputs = null)
    {
        if (steps is < 1 or > 36000) throw new ArgumentOutOfRangeException(nameof(steps));
        return new JsonObject { ["version"] = 1, ["command"] = "step", ["steps"] = steps, ["inputs"] = inputs ?? AllNeutral() };
    }
}
