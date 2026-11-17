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
    public string Status { get; private set; } = "Connecting to TeamSpeak…";
    public bool Connected => authenticated;
    public RemoteClient(Func<Settings> settings, Action<JsonNode> message, Action disconnected)
    { this.settings = settings; this.message = message; this.disconnected = disconnected; }
    public Task Run() => Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            bool refreshAfterJoin = false;
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
                await SendAuth(ws);
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var joinRefresh = new ServerJoinRefresh();
                var reader = Read(ws, joinRefresh, cycle.Token);
                long sentAt = Environment.TickCount64;
                try
                {
                    while (!reader.IsCompleted && ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        await Task.Delay(200, stop.Token);
                        long now = Environment.TickCount64;
                        if (settings().Port != port) break;
                        if (!authenticated && now - sentAt > 90_000) throw new IOException("TeamSpeak approval timed out; retrying.");
                        // Some TS builds ignore repeated auth requests and omit client
                        // details on a later server join. A new socket obtains a fresh
                        // authoritative snapshot, just like the manual Reconnect button.
                        if (authenticated && joinRefresh.TakeDue(now))
                        {
                            refreshAfterJoin = true;
                            Status = "Refreshing TeamSpeak server state…";
                            Storage.Log("Refreshing Remote Apps connection after server join.");
                            break;
                        }
                    }
                }
                finally
                {
                    cycle.Cancel(); ws.Abort();
                    try { await reader; }
                    catch (OperationCanceledException) { }
                    catch (WebSocketException) when (refreshAfterJoin) { }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Status = "TS6 unavailable — enable Settings → Remote Apps, then approve this app.";
                Storage.Log("Remote connection: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally { authenticated = false; disconnected(); socket = null; }
            try { await Task.Delay(refreshAfterJoin ? 150 : 2500, stop.Token); } catch (OperationCanceledException) { }
        }
    });
    private async Task SendAuth(ClientWebSocket ws)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new {
            type = "auth", payload = new { identifier = "local.ts6ducker", version = "0.4.4",
                name = "TS6 Ducker", description = "Lowers other Windows apps while you or your channel members talk.",
                content = new { apiKey = Storage.ReadKey() } } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, stop.Token);
    }
    private async Task Read(ClientWebSocket ws, ServerJoinRefresh joinRefresh, CancellationToken token)
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
                authenticated = true;
                Status = "Connected to TeamSpeak";
                var connections = node["payload"]?["connections"] as JsonArray;
                int clientCount = connections?.Sum(c => (c?["clientInfos"] as JsonArray)?.Count ?? 0) ?? 0;
                Storage.Log($"Remote snapshot: servers={connections?.Count ?? 0}, clients={clientCount}.");
            }
            joinRefresh.Observe(node, Environment.TickCount64);
            message(node);
        }
    }
    public void Reconnect() { try { socket?.Abort(); } catch { } }
    public void Dispose() { stop.Cancel(); Reconnect(); }
}

// Tracks joins relative to each socket's initial snapshot. Replayed connected
// events for servers already in that snapshot must not cause a reconnect loop.
internal sealed class ServerJoinRefresh
{
    private readonly object gate = new();
    private readonly Dictionary<int, int> known = new();
    private readonly HashSet<int> pending = new();
    private long due;
    public void Observe(JsonNode message, long now)
    {
        lock (gate)
        {
            var p = message["payload"];
            if (p == null) return;
            string? type = message["type"]?.ToString();
            if (type == "auth")
            {
                known.Clear(); pending.Clear(); due = 0;
                foreach (var c in p["connections"]?.AsArray() ?? new JsonArray())
                    if (c != null && TalkState.Int(c["clientId"]) > 0)
                        known[TalkState.Int(c["id"], -1)] = TalkState.Int(c["clientId"]);
                return;
            }
            if (type != "connectStatusChanged") return;
            int id = TalkState.Int(p["connectionId"], -1), status = TalkState.Int(p["status"], -1);
            if (id < 0) return;
            if (status is >= 0 and <= 3)
            {
                known.Remove(id); pending.Remove(id);
                if (pending.Count == 0) due = 0;
            }
            else if (status == 4)
            {
                int self = TalkState.Int(p["info"]?["clientId"]);
                if (known.TryGetValue(id, out int previous) && (self == 0 || self == previous)) return;
                known[id] = self; pending.Add(id);
                if (due == 0) due = now + 750;
            }
        }
    }
    public bool TakeDue(long now)
    {
        lock (gate)
        {
            if (due == 0 || now < due || pending.Count == 0) return false;
            due = 0; pending.Clear();
            return true;
        }
    }
}
