using System.Text.Json.Nodes;

namespace TS6Ducker;

// All mutations are serialized by the RemoteClient. IDs are scoped to a connection.
public sealed class TalkState
{
    private sealed class Connection
    {
        public int Self;
        public Dictionary<int, Client> Clients = new();
    }
    private sealed class Client
    {
        public string Channel = "";
        public bool Talking, Whisper, Muted;
    }
    private readonly Dictionary<int, Connection> connections = new();
    public bool Ready { get; private set; }
    public int ConnectionCount => connections.Count;
    public int SelfStarts { get; private set; }
    public int OtherStarts { get; private set; }
    public void Reset() { connections.Clear(); Ready = false; }
    public static int Int(JsonNode? n, int fallback = 0) => int.TryParse(n?.ToString(), out int v) ? v : fallback;
    public static bool Bool(JsonNode? n) => bool.TryParse(n?.ToString(), out bool v) && v;
    public void Apply(JsonNode message)
    {
        string type = message["type"]?.ToString() ?? "";
        var p = message["payload"];
        if (p == null) return;
        if (type == "auth")
        {
            if (Int(message["status"]?["code"], -1) != 0) { Reset(); return; }
            // A full snapshot also heals stale talking state after lost/missed events.
            connections.Clear();
            foreach (var item in p["connections"]?.AsArray() ?? new JsonArray())
            {
                if (item == null) continue;
                int cid = Int(item["id"]);
                var conn = new Connection { Self = Int(item["clientId"]) };
                foreach (var user in item["clientInfos"]?.AsArray() ?? new JsonArray())
                {
                    if (user == null) continue;
                    var props = user["properties"];
                    conn.Clients[Int(user["id"])] = new Client { Channel = user["channelId"]?.ToString() ?? "",
                        Talking = Bool(props?["flagTalking"]), Muted = Bool(props?["isMuted"]) };
                }
                connections[cid] = conn;
            }
            Ready = true;
            return;
        }
        int connectionId = Int(p["connectionId"], -1);
        if (!connections.TryGetValue(connectionId, out var c)) return;
        if (type == "connectStatusChanged")
        {
            // Any connection transition invalidates cached speaking flags.
            foreach (var u in c.Clients.Values) u.Talking = false;
            if (Int(p["status"]) == 0) connections.Remove(connectionId);
            return;
        }
        int clientId = Int(p["clientId"]);
        if (type == "clientMoved")
        {
            string channel = p["newChannelId"]?.ToString() ?? "";
            if (channel == "0" || channel.Length == 0) c.Clients.Remove(clientId);
            else
            {
                if (!c.Clients.TryGetValue(clientId, out var user)) c.Clients[clientId] = user = new Client();
                user.Channel = channel; user.Talking = false;
            }
            if (clientId == c.Self) foreach (var u in c.Clients.Values) u.Talking = false;
        }
        else if (c.Clients.TryGetValue(clientId, out var user))
        {
            if (type == "talkStatusChanged")
            {
                bool talking = Int(p["status"]) == 1; // 2 = speech while transmission is disabled.
                if (talking && !user.Talking) { if (clientId == c.Self) SelfStarts++; else OtherStarts++; }
                user.Talking = talking; user.Whisper = Bool(p["isWhisper"]);
            }
            else if (type == "clientPropertiesUpdated")
            {
                var props = p["properties"];
                if (props?["flagTalking"] != null) user.Talking = Bool(props["flagTalking"]);
                if (props?["isMuted"] != null) user.Muted = Bool(props["isMuted"]);
                if (clientId == c.Self && (Bool(props?["inputMuted"]) || Bool(props?["inputDeactivated"]))) user.Talking = false;
            }
        }
    }
    public int Speakers(Settings settings)
    {
        if (!Ready) return 0;
        int count = 0;
        foreach (var conn in connections.Values)
        {
            if (!conn.Clients.TryGetValue(conn.Self, out var self)) continue;
            foreach (var (id, u) in conn.Clients)
            {
                if (!u.Talking) continue;
                if (id == conn.Self) { if (settings.OnSelf) count++; }
                else if (settings.OnOthers && !u.Muted &&
                    ((u.Whisper && settings.IncludeWhispers) || (!u.Whisper && self.Channel.Length > 0 && u.Channel == self.Channel))) count++;
            }
        }
        return count;
    }
}
