using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed class TraceWriter : IDisposable
{
    private readonly StreamWriter writer;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private int index;
    public TraceWriter(string path, string mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Stream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            output = new GZipStream(output, CompressionLevel.Fastest);
        writer = new StreamWriter(output);
        Write(new JsonObject
        {
            ["kind"] = "header", ["format"] = "overcooked-tas-trace", ["version"] = 1,
            ["createdUtc"] = DateTime.UtcNow.ToString("O"), ["mode"] = mode,
            ["controllerVersion"] = typeof(TraceWriter).Assembly.GetName().Version?.ToString()
        });
    }
    public void Call(JsonObject request, JsonObject response) => Write(new JsonObject
    {
        ["kind"] = "call", ["index"] = index++, ["elapsedMilliseconds"] = elapsed.Elapsed.TotalMilliseconds,
        ["request"] = request.DeepClone(), ["response"] = response.DeepClone()
    });
    public void Event(string name, JsonNode? value) => Write(new JsonObject
    { ["kind"] = "event", ["name"] = name, ["elapsedMilliseconds"] = elapsed.Elapsed.TotalMilliseconds, ["value"] = value?.DeepClone() });
    private void Write(JsonObject value) { writer.WriteLine(value.ToJsonString(Json.Options)); writer.Flush(); }
    public void Dispose() => writer.Dispose();
}

public static class Traces
{
    public static IEnumerable<JsonObject> ReadCalls(string path)
    {
        using var input = File.OpenRead(path);
        using var decompressed = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(input, CompressionMode.Decompress) : null;
        using var reader = new StreamReader(decompressed is null ? input : decompressed);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            JsonObject entry;
            try { entry = Json.Object(line); }
            catch (Exception e) { throw new InvalidDataException($"Invalid JSON at {path}:{lineNumber}", e); }
            if (entry["kind"]?.ToString() is "header" or "event") continue;
            yield return entry;
        }
    }

    public static JsonObject ExtractRequest(JsonObject entry) =>
        (entry["request"] as JsonObject ?? entry).DeepClone().AsObject();
}

public sealed record Difference(string Path, string? Expected, string? Actual);

public static class StateComparer
{
    // No implicit ignored gameplay fields: callers explicitly list volatile paths.
    public static Difference? First(JsonNode? expected, JsonNode? actual, ISet<string>? ignored = null, string path = "$")
    {
        if (ignored?.Contains(path) == true) return null;
        if (expected is JsonObject a && actual is JsonObject b)
        {
            foreach (var key in a.Select(x => x.Key).Union(b.Select(x => x.Key)).Order(StringComparer.Ordinal))
            {
                var childPath = path + "." + key;
                if (ignored?.Contains(childPath) == true) continue;
                if (!a.ContainsKey(key) || !b.ContainsKey(key)) return new Difference(childPath, a[key]?.ToJsonString(), b[key]?.ToJsonString());
                var difference = First(a[key], b[key], ignored, childPath);
                if (difference is not null) return difference;
            }
            return null;
        }
        if (expected is JsonArray aa && actual is JsonArray bb)
        {
            if (aa.Count != bb.Count) return new Difference(path + ".length", aa.Count.ToString(), bb.Count.ToString());
            for (var i = 0; i < aa.Count; i++)
            {
                var difference = First(aa[i], bb[i], ignored, path + "." + i);
                if (difference is not null) return difference;
            }
            return null;
        }
        return JsonNode.DeepEquals(expected, actual) ? null : new Difference(path, expected?.ToJsonString(), actual?.ToJsonString());
    }
}
