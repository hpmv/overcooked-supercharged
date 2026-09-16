using Hpmv;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public sealed class TraceWriteException(string message, Exception inner = null) : IOException(message, inner);

/// <summary>Exact JSON exchange evidence with bounded paused blocks and an explicit storage ceiling.</summary>
public sealed class TraceStore : IDisposable
{
    public const int BlockRows = 256, BlockBytes = 1024 * 1024;
    public const long DefaultMaximumBytes = 1024L * 1024 * 1024, DefaultReserveBytes = 256L * 1024 * 1024;
    private readonly Stream stream;
    private readonly Func<long> availableBytes;
    private readonly long maximumBytes, reserveBytes;
    private readonly MemoryStream pending = new();
    private readonly Stopwatch pendingAge = Stopwatch.StartNew();
    private int pendingRows;
    private long bytesWritten, rowsWritten, blocksWritten, callbacks;
    private string failure;
    private bool disposed;

    public TraceStore(Stream stream, long maximumBytes = DefaultMaximumBytes, long reserveBytes = 0, Func<long> availableBytes = null)
    {
        if (maximumBytes <= 0 || reserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.stream = stream; this.maximumBytes = maximumBytes; this.reserveBytes = reserveBytes; this.availableBytes = availableBytes;
    }
    public static TraceStore Create(string path, long maximumBytes = DefaultMaximumBytes, long reserveBytes = DefaultReserveBytes)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
        var drive = new DriveInfo(Path.GetPathRoot(path));
        if (drive.AvailableFreeSpace < reserveBytes) throw new TraceWriteException("Trace storage reserve is unavailable before startup.");
        return new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), maximumBytes, reserveBytes, () => drive.AvailableFreeSpace);
    }
    public JsonObject Status() => new()
    {
        ["formatVersion"] = 2, ["bytesWritten"] = bytesWritten, ["maximumBytes"] = maximumBytes, ["minimumFreeBytes"] = reserveBytes,
        ["persistedExchanges"] = rowsWritten, ["observedExchanges"] = callbacks, ["pausedBlocks"] = blocksWritten,
        ["bufferedPausedExchanges"] = pendingRows, ["bufferedPausedBytes"] = pending.Length, ["failed"] = failure is not null, ["error"] = failure,
        ["scope"] = "Advancing and phase-transition rows flush immediately. Exact fully paused callbacks, including all phase fields, are Brotli JSONL blocks (at most 256 callbacks / 1 MiB / 5 seconds). An abrupt process loss can omit only the reported buffered paused tail. Limits stop control; no evidence is silently dropped."
    };
    public void Header(object header) => WriteLine(JsonSerializer.Serialize(header, RuntimeHost.Json));
    public void Control(JsonObject request)
    { Flush(); WriteLine(JsonSerializer.Serialize(new { kind = "control", request }, RuntimeHost.Json)); }
    public void Exchange(OutputData output, InputData input, bool settledPaused)
    {
        EnsureHealthy();
        byte[] row = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind = "exchange", output, input }, RuntimeHost.Json) + "\n");
        bool compress = settledPaused && output.LastFramePaused && output.NextFramePaused && !input.RequestPause && !input.RequestResume &&
            !input.__isset.warp && !input.__isset.resetOrderSeed && !input.__isset.gameSpeed;
        if (!compress || row.Length > BlockBytes)
        { Flush(); WriteBytes(row); rowsWritten++; callbacks++; return; }
        if (pending.Length + row.Length > BlockBytes) Flush();
        pending.Write(row); pendingRows++; callbacks++;
        if (pendingRows >= BlockRows || pending.Length >= BlockBytes || pendingAge.Elapsed >= TimeSpan.FromSeconds(5)) Flush();
    }
    public void Flush()
    {
        EnsureHealthy();
        if (pendingRows == 0) { pendingAge.Restart(); return; }
        byte[] raw = pending.ToArray();
        using var compressed = new MemoryStream();
        using (var encoder = new BrotliStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) encoder.Write(raw);
        WriteLine(JsonSerializer.Serialize(new
        {
            kind = "paused-exchanges", version = 1, encoding = "brotli-jsonl", count = pendingRows, uncompressedBytes = raw.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(raw)), data = Convert.ToBase64String(compressed.ToArray())
        }, RuntimeHost.Json));
        rowsWritten += pendingRows; blocksWritten++; pending.SetLength(0); pendingRows = 0; pendingAge.Restart();
    }
    private void WriteLine(string line) => WriteBytes(Encoding.UTF8.GetBytes(line + "\n"));
    private void EnsureHealthy()
    {
        if (failure is not null) throw new TraceWriteException(failure);
        if (disposed) throw new ObjectDisposedException(nameof(TraceStore));
    }
    private void WriteBytes(byte[] bytes)
    {
        EnsureHealthy();
        try
        {
            if (bytesWritten + bytes.Length > maximumBytes) throw new IOException("Configured trace byte limit reached.");
            if (availableBytes is not null && availableBytes() - bytes.Length < reserveBytes) throw new IOException("Trace minimum free-space reserve reached.");
            stream.Write(bytes); stream.Flush(); bytesWritten += bytes.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failure = "Trace evidence write failed; this session cannot continue or qualify: " + error.Message;
            throw new TraceWriteException(failure, error);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        try { if (failure is null) Flush(); }
        finally { disposed = true; stream.Dispose(); pending.Dispose(); }
    }

    /// <summary>Expands both original v1 rows and v2 compressed paused rows, without interpreting or rounding their data.</summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using Stream decoded = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(decoded, new UTF8Encoding(false, true));
        string line; while ((line = reader.ReadLine()) is not null) yield return line;
    }
    public static IEnumerable<string> Expand(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var row = JsonNode.Parse(line)!.AsObject();
            if (row["kind"]?.ToString() != "paused-exchanges") { yield return line; continue; }
            if (row["version"]?.GetValue<int>() != 1 || row["encoding"]?.ToString() != "brotli-jsonl") throw new InvalidDataException("Unknown paused trace encoding.");
            int count = row["count"]!.GetValue<int>(), length = row["uncompressedBytes"]!.GetValue<int>();
            if (count is < 1 or > BlockRows || length is < 1 or > BlockBytes) throw new InvalidDataException("Paused block bounds exceeded.");
            byte[] packed = Convert.FromBase64String(row["data"]!.ToString());
            if (packed.Length > BlockBytes + 65536) throw new InvalidDataException("Oversized compressed paused block.");
            using var decoder = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);
            byte[] raw = new byte[length]; decoder.ReadExactly(raw);
            if (decoder.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(raw)) != row["sha256"]!.ToString()) throw new InvalidDataException("Paused block byte length/hash mismatch.");
            string text = new UTF8Encoding(false, true).GetString(raw);
            var expanded = text.Split('\n');
            if (expanded.Length != count + 1 || expanded[^1] != "") throw new InvalidDataException("Paused block callback count mismatch.");
            foreach (string exchange in expanded.Take(count))
            {
                var value = JsonNode.Parse(exchange)!;
                if (value["kind"]?.ToString() != "exchange" || value["output"]?["LastFramePaused"]?.GetValue<bool>() != true || value["output"]?["NextFramePaused"]?.GetValue<bool>() != true)
                    throw new InvalidDataException("A paused block contains a non-paused exchange.");
                yield return exchange;
            }
        }
    }
}
