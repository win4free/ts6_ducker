using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace TS6Ducker;

public sealed class RemoteClient : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Func<Settings> settings;
    private readonly Action<JsonNode> message;
    private readonly Action disconnected;
    private ClientWebSocket? socket;
    private volatile bool authenticated;
    private long lastAuth;
    private int refresh;
    public string Status { get; private set; } = "Connecting to TeamSpeak…";
    public bool Connected => authenticated;
    public RemoteClient(Func<Settings> settings, Action<JsonNode> message, Action disconnected)
    { this.settings = settings; this.message = message; this.disconnected = disconnected; }
    public Task Run() => Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                int port = settings().Port;
                using var ws = new ClientWebSocket(); socket = ws;
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(4));
                Status = $"Connecting to TS6 on localhost:{port}…";
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), connectTimeout.Token);
                Status = "Waiting for approval in TeamSpeak notifications…";
                lastAuth = Environment.TickCount64;
                await SendAuth(ws);
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var reader = Read(ws, cycle.Token);
                long sentAt = Environment.TickCount64;
                try
                {
                    while (!reader.IsCompleted && ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        await Task.Delay(200, stop.Token);
                        long now = Environment.TickCount64;
                        if (settings().Port != port) break;
                        if (!authenticated && now - sentAt > 90_000) throw new IOException("TeamSpeak approval timed out; retrying.");
                        if (authenticated && now - Interlocked.Read(ref lastAuth) > 25_000) throw new IOException("TeamSpeak stopped responding; restoring volumes.");
                        if (authenticated && (now - sentAt > 15_000 || Interlocked.Exchange(ref refresh, 0) != 0))
                        { await SendAuth(ws); sentAt = now; }
                    }
                }
                finally { cycle.Cancel(); ws.Abort(); try { await reader; } catch (OperationCanceledException) { } }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Status = "TS6 unavailable — enable Settings → Remote Apps, then approve this app.";
                Storage.Log("Remote connection: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally { authenticated = false; disconnected(); socket = null; }
            try { await Task.Delay(2500, stop.Token); } catch (OperationCanceledException) { }
        }
    });
    private async Task SendAuth(ClientWebSocket ws)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new {
            type = "auth", payload = new { identifier = "local.ts6ducker", version = "0.4.0",
                name = "TS6 Ducker", description = "Lowers other Windows apps while you or your channel members talk.",
                content = new { apiKey = Storage.ReadKey() } } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, stop.Token);
    }
    private async Task Read(ClientWebSocket ws, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            using var data = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (received.MessageType == WebSocketMessageType.Close) return;
                if (data.Length + received.Count > 4_000_000) throw new IOException("Remote message exceeded size limit.");
                data.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            if (received.MessageType != WebSocketMessageType.Text) continue;
            var node = JsonNode.Parse(data.ToArray());
            if (node == null) continue;
            string? type = node["type"]?.ToString();
            if (type == "auth")
            {
                if (TalkState.Int(node["status"]?["code"], -1) != 0)
                    throw new IOException("TeamSpeak declined authentication. Review Remote Apps permissions.");
                string? key = node["payload"]?["apiKey"]?.ToString();
                if (!string.IsNullOrEmpty(key)) Storage.WriteKey(key);
                authenticated = true; Interlocked.Exchange(ref lastAuth, Environment.TickCount64);
                Status = "Connected to TeamSpeak";
            }
            message(node);
            if (type is "clientMoved" or "connectStatusChanged") Interlocked.Exchange(ref refresh, 1);
        }
    }
    public void Reconnect() { try { socket?.Abort(); } catch { } }
    public void Dispose() { stop.Cancel(); Reconnect(); }
}
