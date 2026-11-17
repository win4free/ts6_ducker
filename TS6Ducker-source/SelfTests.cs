using System.Text.Json.Nodes;

namespace TS6Ducker;

internal static class SelfTests
{
    private sealed class Session(string key, string process, float volume) : IAudioSession
    {
        public string Key => key;
        public string DeviceId => "test-device";
        public string InstanceId => key;
        public string ProcessName => process;
        public float Volume { get; set; } = volume;
        public void Dispose() { }
    }
    private sealed class Backend(params Session[] sessions) : IAudioBackend
    {
        public List<Session> Sessions = sessions.ToList();
        public List<IAudioSession> Enumerate() => Sessions.Cast<IAudioSession>().ToList();
    }
    public static void Run(string output)
    {
        var results = new List<string>();
        void Check(string name, Action test)
        {
            try { test(); results.Add("PASS " + name); }
            catch (Exception ex) { results.Add("FAIL " + name + ": " + ex.Message); }
        }
        void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
        void Near(float actual, float expected) { if (Math.Abs(actual - expected) > .001) throw new Exception($"Expected {expected}, got {actual}"); }
        JsonNode Node(string json) => JsonNode.Parse(json)!;
        TalkState Ready()
        {
            var state = new TalkState();
            state.Apply(Node("""{"type":"auth","status":{"code":0},"payload":{"connections":[{"id":1,"clientId":10,"clientInfos":[{"id":10,"channelId":5,"properties":{}},{"id":11,"channelId":5,"properties":{}},{"id":12,"channelId":8,"properties":{}}]}]}}"""));
            return state;
        }
        void Talk(TalkState state, int id, int status, bool whisper = false) => state.Apply(Node(System.Text.Json.JsonSerializer.Serialize(new { type = "talkStatusChanged", payload = new { connectionId = 1, clientId = id, status, isWhisper = whisper } })));
        var s = new Settings(FadeMs: 400);
        Check("Incoming speech and local speech both trigger", () => { var t = Ready(); Talk(t, 10, 1); Talk(t, 11, 1); Assert(t.Speakers(s) == 2 && t.SelfStarts == 1 && t.OtherStarts == 1); });
        Check("Overlapping speakers do not release early", () => { var t = Ready(); Talk(t, 10, 1); Talk(t, 11, 1); Talk(t, 10, 0); Assert(t.Speakers(s) == 1); });
        Check("Separate self and other switches", () => { var t = Ready(); Talk(t, 10, 1); Assert(t.Speakers(s with { OnSelf = false }) == 0); Talk(t, 10, 0); Talk(t, 11, 1); Assert(t.Speakers(s with { OnOthers = false }) == 0); });
        Check("Speech while microphone disabled does not trigger", () => { var t = Ready(); Talk(t, 10, 2); Assert(t.Speakers(s) == 0); });
        Check("Other channels are excluded, optional whispers included", () => { var t = Ready(); Talk(t, 12, 1); Assert(t.Speakers(s) == 0); Talk(t, 12, 1, true); Assert(t.Speakers(s) == 1 && t.Speakers(s with { IncludeWhispers = false }) == 0); });
        Check("Moving channels clears speaking state", () => { var t = Ready(); Talk(t, 11, 1); t.Apply(Node("""{"type":"clientMoved","payload":{"connectionId":1,"clientId":10,"newChannelId":8}}""")); Assert(t.Speakers(s) == 0); });
        Check("A leaving speaker cannot leave ducking stuck", () => { var t = Ready(); Talk(t, 11, 1); t.Apply(Node("""{"type":"clientMoved","payload":{"connectionId":1,"clientId":11,"newChannelId":0}}""")); Assert(t.Speakers(s) == 0); });
        Check("Disconnect clears speech", () => { var t = Ready(); Talk(t, 11, 1); t.Reset(); Assert(t.Speakers(s) == 0 && !t.Ready); });
        Check("Snapshot repairs stale speech", () => { var t = Ready(); Talk(t, 11, 1); t.Apply(Node("""{"type":"auth","status":{"code":0},"payload":{"connections":[]}}""")); Assert(t.Speakers(s) == 0); });
        Check("Invalid authentication cannot trigger ducking", () => { var t = Ready(); Talk(t, 10, 1); t.Apply(Node("""{"type":"auth","status":{"code":1},"payload":{}}""")); Assert(!t.Ready && t.Speakers(s) == 0); });
        Check("Muted other client is excluded", () => { var t = Ready(); Talk(t, 11, 1); t.Apply(Node("""{"type":"clientPropertiesUpdated","payload":{"connectionId":1,"clientId":11,"properties":{"isMuted":true}}}""")); Assert(t.Speakers(s) == 0); });
        Check("IDs are scoped to connection", () => { var t = Ready(); t.Apply(Node("""{"type":"talkStatusChanged","payload":{"connectionId":99,"clientId":11,"status":1}}""")); Assert(t.Speakers(s) == 0); });
        TalkState JoinedAfterAuth()
        {
            var t = new TalkState();
            t.Apply(Node("""{"type":"auth","status":{"code":0},"payload":{"connections":[]}}"""));
            t.Apply(Node("""{"type":"connectStatusChanged","payload":{"connectionId":1,"status":4,"info":{"clientId":10}}}"""));
            t.Apply(Node("""{"type":"clientMoved","payload":{"connectionId":1,"clientId":10,"newChannelId":5,"oldChannelId":0}}"""));
            t.Apply(Node("""{"type":"clientMoved","payload":{"connectionId":1,"clientId":11,"newChannelId":5,"oldChannelId":0}}"""));
            return t;
        }
        Check("Server joined after empty initial auth detects self and others", () => { var t=JoinedAfterAuth(); Talk(t,10,1); Talk(t,11,1); Assert(t.ConnectionCount==1 && t.Speakers(s)==2); });
        Check("Leaving a server removes it and clears active speech", () => { var t=JoinedAfterAuth(); Talk(t,10,1); t.Apply(Node("""{"type":"connectStatusChanged","payload":{"connectionId":1,"status":0}}""")); Assert(t.ConnectionCount==0 && t.Speakers(s)==0); });
        Check("Rejoining a server does not need another WebSocket auth", () => { var t=JoinedAfterAuth(); t.Apply(Node("""{"type":"connectStatusChanged","payload":{"connectionId":1,"status":0}}""")); t.Apply(Node("""{"type":"connectStatusChanged","payload":{"connectionId":1,"status":4,"info":{"clientId":20}}}""")); t.Apply(Node("""{"type":"clientMoved","payload":{"connectionId":1,"clientId":20,"newChannelId":6}}""")); Talk(t,20,1); Assert(t.ConnectionCount==1 && t.Speakers(s)==1); });
        Check("Preserve independent mixer levels and exclude TeamSpeak", () => {
            var game = new Session("g", "Game", .8f); var music = new Session("m", "Music", .4f); var ts = new Session("t", "TeamSpeak", .9f);
            var v = new VolumeController(new Backend(game, music, ts), _ => {});
            v.Tick(true, s, 0); Near(game.Volume, .2f); Near(music.Volume, .1f); Near(ts.Volume, .9f);
            v.Tick(false, s, 100, true); Near(game.Volume, .8f); Near(music.Volume, .4f); Assert(v.ChangedSessions == 0);
        });
        Check("Ducking never compounds", () => { var x = new Session("g", "Game", .8f); var v = new VolumeController(new Backend(x), _ => {}); for (int i=0;i<20;i++) v.Tick(true,s,i*100); Near(x.Volume,.2f); });
        Check("New audio sessions are ducked during speech", () => { var b = new Backend(); var v = new VolumeController(b,_=>{}); v.Tick(true,s,0); var x = new Session("x","Game",.6f); b.Sessions.Add(x); v.Tick(true,s,100); Near(x.Volume,.15f); });
        Check("Fade restores original volume progressively", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>{}); v.Tick(true,s,0); v.Tick(false,s,100); Near(x.Volume,.2f); v.Tick(false,s,300); Near(x.Volume,.5f); v.Tick(false,s,500); Near(x.Volume,.8f); });
        Check("Speech resumes during fade without losing original", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>{}); v.Tick(true,s,0); v.Tick(false,s,100); v.Tick(false,s,300); v.Tick(true,s,320); Near(x.Volume,.2f); v.Tick(false,s,400,true); Near(x.Volume,.8f); });
        Check("Manual mixer adjustment is respected", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>{}); v.Tick(true,s,0); x.Volume=.35f; v.Tick(true,s,100); v.Tick(false,s,200,true); Near(x.Volume,.35f); v.Tick(true,s,300); Near(x.Volume,.0875f); v.Tick(false,s,400,true); Near(x.Volume,.35f); });
        Check("Custom exclusions restore a ducked session", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>{}); v.Tick(true,s,0); v.Tick(true,s with { ExcludedProcesses="Game.exe" },100); Near(x.Volume,.8f); });
        Check("Zero percent and 100 percent work", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>{}); v.Tick(true,s with { RemainingPercent=0 },0); Near(x.Volume,0); v.Tick(false,s,100,true); Near(x.Volume,.8f); v.Tick(true,s with { RemainingPercent=100 },200); Near(x.Volume,.8f); Assert(v.ChangedSessions==0); });
        Check("Journal failure prevents volume changes", () => { var x = new Session("g","Game",.8f); var v = new VolumeController(new Backend(x),_=>throw new IOException("simulated persistence failure")); v.Tick(true,s,0); Near(x.Volume,.8f); });
        Check("Crash recovery restores only matching session instances", () => { var x = new Session("g","Game",.2f); var other = new Session("new","Game",.6f); var e = new VolumeEntry("g","test-device","g","Game",.8f,.2f,.8f); var v = new VolumeController(new Backend(x,other),_=>{},[e]); v.Tick(false,s,0,true); Near(x.Volume,.8f); Near(other.Volume,.6f); });
        Check("Crash inside mixer write is recoverable", () => { var x = new Session("g","Game",.2f); var e = new VolumeEntry("g","test-device","g","Game",.8f,.35f,.2f); var v = new VolumeController(new Backend(x),_=>{},[e]); v.Tick(false,s,0,true); Near(x.Volume,.8f); });
        Check("Crash recovery preserves later manual changes", () => { var x = new Session("g","Game",.6f); var e = new VolumeEntry("g","test-device","g","Game",.8f,.2f,.2f); var v = new VolumeController(new Backend(x),_=>{},[e]); v.Tick(false,s,0,true); Near(x.Volume,.6f); });
        Check("Unavailable audio devices retain recovery information", () => { var e = new VolumeEntry("g","test-device","g","Game",.8f,.2f,.2f); var v = new VolumeController(new Backend(),_=>{},[e]); v.Tick(false,s,0,true); Assert(v.ChangedSessions==1); });
        Check("Real WebSocket authentication, fragmented events, and disconnect", () => TransportTest().GetAwaiter().GetResult());
        Check("Healthy WebSocket survives 32 seconds without repeated auth responses", () => TransportTest(true).GetAwaiter().GetResult());
        Check("Lifecycle stops only after TS has run and three absent checks", () => { var l = new TeamSpeakLifecycle(); Assert(!l.ShouldStop(true,false)); Assert(!l.ShouldStop(true,true)); Assert(!l.ShouldStop(true,false)); Assert(!l.ShouldStop(true,false)); Assert(l.ShouldStop(true,false)); });
        Check("Brief TS process restart does not close Ducker", () => { var l = new TeamSpeakLifecycle(); l.ShouldStop(true,true); l.ShouldStop(true,false); Assert(!l.ShouldStop(true,true)); Assert(!l.ShouldStop(true,false)); });
        Check("Disabling start/stop clears lifetime tracking", () => { var l = new TeamSpeakLifecycle(); l.ShouldStop(true,true); l.ShouldStop(false,false); for(int i=0;i<5;i++) Assert(!l.ShouldStop(true,false)); });
        Check("Watcher launches once per TS run and re-arms after exit", () => { var l = new LaunchOnTeamSpeak(); Assert(!l.ShouldLaunch(false)); Assert(l.ShouldLaunch(true)); Assert(!l.ShouldLaunch(true)); Assert(!l.ShouldLaunch(false)); Assert(l.ShouldLaunch(true)); });
        Check("Old settings keep automatic startup opt-in", () => { var old = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"RemainingPercent\":15}",Storage.Json)!; Assert(!old.StartStopWithTeamSpeak && old.RemainingPercent==15); });
        Check("All tray status icons are embedded and load", () => { foreach(var name in new[]{"ducker","ducking","paused"}) { using var icon=AppIcons.Load(name); Assert(icon.Width>0); } });
        results.Add($"\n{results.Count(x => x.StartsWith("PASS"))} passed; {results.Count(x => x.StartsWith("FAIL"))} failed.");
        File.WriteAllLines(output,results); Environment.ExitCode = results.Any(x=>x.StartsWith("FAIL")) ? 1 : 0;
    }
    private static async Task TransportTest(bool longLived = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(longLived ? 50 : 12));
        var token = timeout.Token;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var remote = new RemoteClient(() => new Settings(Port: port), n => messages.Enqueue(n["type"]!.ToString()), () => disconnected.TrySetResult());
        var run = remote.Run();
        try
        {
            using var tcp = await listener.AcceptTcpClientAsync(token);
            var stream = tcp.GetStream();
            var bytes = new List<byte>(); var one = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(one, token) == 0) throw new IOException("Handshake closed.");
                bytes.Add(one[0]);
                if (bytes.Count > 8192) throw new IOException("Handshake too large.");
                if (bytes.Count >= 4 && System.Text.Encoding.ASCII.GetString(bytes.TakeLast(4).ToArray()) == "\r\n\r\n") break;
            }
            string header = System.Text.Encoding.ASCII.GetString(bytes.ToArray());
            string key = header.Split("\r\n").First(x => x.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':',2)[1].Trim();
            string accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token);
            using var server = System.Net.WebSockets.WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(5));
            var buffer = new byte[4096];
            var request = await server.ReceiveAsync(new ArraySegment<byte>(buffer),token);
            var auth = JsonNode.Parse(buffer.AsSpan(0,request.Count))!;
            if (auth["type"]?.ToString() != "auth" || auth["payload"]?["identifier"]?.ToString() != "local.ts6ducker") throw new Exception("Invalid auth request");
            byte[] response = System.Text.Encoding.UTF8.GetBytes("""{"type":"auth","status":{"code":0},"payload":{"connections":[]}}""");
            await server.SendAsync(response.AsMemory(0,20),System.Net.WebSockets.WebSocketMessageType.Text,false,token);
            await server.SendAsync(response.AsMemory(20),System.Net.WebSockets.WebSocketMessageType.Text,true,token);
            await server.SendAsync(System.Text.Encoding.UTF8.GetBytes("""{"type":"talkStatusChanged","payload":{"connectionId":1,"clientId":10,"status":1}}"""),System.Net.WebSockets.WebSocketMessageType.Text,true,token);
            for (int i=0;i<100 && messages.Count<2;i++) await Task.Delay(20,token);
            if (!remote.Connected || !messages.SequenceEqual(new[]{"auth","talkStatusChanged"})) throw new Exception("Fragmented events were not delivered.");
            Task<System.Net.WebSockets.WebSocketReceiveResult>? pending = null;
            if (longLived)
            {
                // Match TS6 builds that ignore repeated auth requests. Drain protocol frames,
                // but send no auth replies or speech events during the idle interval.
                var incomingBuffer = new byte[4096];
                pending = server.ReceiveAsync(new ArraySegment<byte>(incomingBuffer), token);
                long until = Environment.TickCount64 + 32_000;
                while (Environment.TickCount64 < until)
                {
                    await Task.Delay(100, token);
                    if (disconnected.Task.IsCompleted || !remote.Connected) throw new Exception("Healthy connection was disconnected by an application timeout.");
                    if (pending.IsCompleted)
                    {
                        var ignored = await pending;
                        if (ignored.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) throw new Exception("Client closed a healthy connection.");
                        pending = server.ReceiveAsync(new ArraySegment<byte>(incomingBuffer), token);
                    }
                }
                await server.SendAsync(System.Text.Encoding.UTF8.GetBytes("""{"type":"talkStatusChanged","payload":{"connectionId":1,"clientId":10,"status":0}}"""),System.Net.WebSockets.WebSocketMessageType.Text,true,token);
                for (int i=0;i<100 && messages.Count<3;i++) await Task.Delay(20,token);
                if (messages.Count != 3) throw new Exception("Speech events stopped after the idle period.");
            }
            await server.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,"test done",token);
            await disconnected.Task.WaitAsync(token);
            if (remote.Connected) throw new Exception("Remote stayed connected after close.");
            if (pending != null) { server.Abort(); try { await pending; } catch (System.Net.WebSockets.WebSocketException) { } catch (OperationCanceledException) { } }
        }
        finally { remote.Dispose(); listener.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(3)); }
    }
}
