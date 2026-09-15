using Hpmv;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thrift.Protocol;
using Thrift.Transport;

namespace Supercharged.Headless;

public static class RuntimeHost
{
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public static async Task Serve(HeadlessSession session, int gamePort, int controlPort, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lifetime.Token;
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{controlPort}/");
        try { listener.Start(); }
        catch (HttpListenerException error) when (error.NativeErrorCode == 6)
        {
            // Some Windows boots leave HTTP.sys running but unable to create a
            // server session (ERROR_INVALID_HANDLE). The authoring controller is
            // loopback-only and needs only a tiny JSON POST surface, so retain the
            // wire contract without making test execution depend on HTTP.sys.
            await ServeTcpHttp(session, gamePort, controlPort, token);
            return;
        }
        var network = ConnectAndProcess(session, gamePort, token);
        Console.Error.WriteLine($"Headless control http://127.0.0.1:{controlPort}/ ; framework game TCP127.0.0.1:{gamePort}");
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(token);
                try
                {
                    if (context.Request.ContentLength64 > 128 * 1024 * 1024) throw new InvalidDataException("Control request exceeds128MiB.");
                    JsonObject request;
                    if (context.Request.HttpMethod == "GET") request = new() { ["command"] = "inspect", ["full"] = context.Request.QueryString["full"] == "true" };
                    else
                    {
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        request = JsonNode.Parse(await reader.ReadToEndAsync(token))?.AsObject() ?? throw new InvalidDataException("Expected JSON object.");
                    }
                    await Reply(context, session.Command(request));
                }
                catch (Exception error) { await Reply(context, new JsonObject { ["ok"] = false, ["error"] = error.Message }, 400); }
            }
        }
        finally { listener.Stop(); lifetime.Cancel(); try { await network; } catch (OperationCanceledException) when (token.IsCancellationRequested) { } }
    }

    private static async Task ServeTcpHttp(HeadlessSession session, int gamePort, int controlPort, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lifetime.Token;
        var listener = new TcpListener(IPAddress.Loopback, controlPort);
        listener.Start();
        var network = ConnectAndProcess(session, gamePort, token);
        Console.Error.WriteLine($"Headless control fallback http://127.0.0.1:{controlPort}/ ; framework game TCP127.0.0.1:{gamePort}");
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleTcpHttpClient(client, session, token);
            }
        }
        finally
        {
            listener.Stop(); lifetime.Cancel();
            try { await network; } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private static async Task HandleTcpHttpClient(TcpClient client, HeadlessSession session, CancellationToken token)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                byte[] header;
                try { header = await ReadHttpHeader(stream, token); }
                catch (EndOfStreamException) { return; }
                string text = Encoding.ASCII.GetString(header);
                string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;
                string[] requestLine = lines[0].Split(' ');
                if (requestLine.Length != 3 || requestLine[1] != "/")
                {
                    await ReplyTcp(stream, new JsonObject { ["ok"] = false, ["error"] = "Expected the loopback control root." }, 400, token);
                    continue;
                }
                int length = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon < 0 || !lines[i].Substring(0, colon).Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Int32.TryParse(lines[i].Substring(colon + 1).Trim(), out length) || length < 0 || length > 128 * 1024 * 1024)
                        length = -1;
                }
                if (length < 0)
                {
                    await ReplyTcp(stream, new JsonObject { ["ok"] = false, ["error"] = "Control request exceeds128MiB or has an invalid length." }, 400, token);
                    return;
                }
                try
                {
                    JsonObject request;
                    if (requestLine[0] == "GET") request = new() { ["command"] = "inspect", ["full"] = false };
                    else if (requestLine[0] == "POST")
                    {
                        byte[] body = await ReadExactly(stream, length, token);
                        request = JsonNode.Parse(body)?.AsObject() ?? throw new InvalidDataException("Expected JSON object.");
                    }
                    else throw new InvalidDataException("Expected GET or POST.");
                    await ReplyTcp(stream, session.Command(request), 200, token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    await ReplyTcp(stream, new JsonObject { ["ok"] = false, ["error"] = error.Message }, 400, token);
                }
            }
        }
    }

    private static async Task<byte[]> ReadHttpHeader(NetworkStream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        while (bytes.Count < 64 * 1024)
        {
            byte[] one = new byte[1];
            int read = await stream.ReadAsync(one, token);
            if (read == 0) throw new EndOfStreamException();
            bytes.Add(one[0]);
            int count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                return bytes.ToArray();
        }
        throw new InvalidDataException("Control request header exceeds64KiB.");
    }

    private static async Task<byte[]> ReadExactly(NetworkStream stream, int count, CancellationToken token)
    {
        byte[] bytes = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(offset, count - offset), token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }

    private static async Task ReplyTcp(NetworkStream stream, JsonObject value, int status, CancellationToken token)
    {
        byte[] body = Encoding.UTF8.GetBytes(value.ToJsonString());
        string reason = status == 200 ? "OK" : "Bad Request";
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    private static async Task Reply(HttpListenerContext context, JsonObject value, int status = 200)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToJsonString());
        context.Response.StatusCode = status; context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
    }

    private static async Task ConnectAndProcess(HeadlessSession session, int port, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var client = new TcpClient { NoDelay = true };
            try { await client.ConnectAsync(IPAddress.Loopback, port, token); }
            catch (SocketException) { await Task.Delay(250, token); continue; }
            try
            {
                using TTransport transport = new Thrift.Transport.Client.TStreamTransport(client.GetStream(), client.GetStream(), new Thrift.TConfiguration());
                var protocol = new TBinaryProtocol(transport); var processor = new Interceptor.AsyncProcessor(session);
                while (!token.IsCancellationRequested && await processor.ProcessAsync(protocol, protocol, token))
                {
                    // Finish writing the explicit neutral/pause response before
                    // disconnecting. Keep HTTP inspection alive for the failure.
                    if (session.TraceFailed) break;
                }
                session.ConnectionEnded();
            }
            catch (Exception error) when (error is not OperationCanceledException) { session.ConnectionEnded(error.ToString()); }
            return; // Never silently reattach with incomplete running-game history.
        }
    }

    public static async Task<JsonObject> Send(int port, JsonObject request, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"http://127.0.0.1:{port}/", content, token);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?.AsObject() ?? throw new InvalidDataException("Headless control returned no JSON object.");
    }
}
