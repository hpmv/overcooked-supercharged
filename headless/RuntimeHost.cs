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
        listener.Prefixes.Add($"http://127.0.0.1:{controlPort}/"); listener.Start();
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
