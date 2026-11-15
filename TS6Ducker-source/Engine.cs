using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TS6Ducker;

public record EngineView(string Connection, int Servers, int Speakers, int SelfStarts, int OtherStarts,
    int ChangedSessions, string[] Apps, string Activity, string? Error);

public sealed class Engine : IDisposable
{
    private Settings settings;
    private readonly ConcurrentQueue<JsonNode?> events = new();
    private readonly TalkState talk = new();
    private readonly RemoteClient remote;
    private readonly Thread worker;
    private volatile bool stopping, paused;
    private long testUntil;
    private volatile bool restoreRequested;
    private readonly string journal;
    private bool watchdogReady;
    public EngineView View { get; private set; } = new("Starting…", 0, 0, 0, 0, 0, [], "Starting", null);
    public Settings Settings => settings;
    public Engine(Settings initial)
    {
        settings = initial;
        journal = $"recovery-{Environment.ProcessId}-{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks}.json";
        remote = new(() => settings, node => events.Enqueue(node), () => events.Enqueue(null));
        worker = new Thread(Work) { IsBackground = true, Name = "Audio control" };
        worker.SetApartmentState(ApartmentState.MTA);
    }
    public void Start() { worker.Start(); _ = remote.Run(); }
    public void Update(Settings value) { settings = value; Storage.Write("settings.json", value); }
    public void Reconnect() { restoreRequested = true; remote.Reconnect(); }
    public void Test() { Interlocked.Exchange(ref testUntil, Environment.TickCount64 + 3000); }
    public void Restore() { paused = true; restoreRequested = true; Interlocked.Exchange(ref testUntil, 0); }
    public void Resume() { paused = false; }
    private void Work()
    {
        var audio = new WindowsAudio();
        VolumeController? volume = null;
        long holdUntil = 0;
        try
        {
            List<VolumeEntry> recovery;
            using (var gate = new Mutex(false, @"Local\TS6Ducker.Recovery"))
            {
                try { gate.WaitOne(); } catch (AbandonedMutexException) { }
                try
                {
                    var orphaned = Recovery.CollectOrphans();
                    recovery = orphaned.Entries;
                    Storage.Write(journal, recovery);
                    foreach (var oldPath in orphaned.Paths) { File.Delete(oldPath); File.Delete(oldPath + ".ready"); }
                }
                finally { gate.ReleaseMutex(); }
            }
            volume = new VolumeController(audio, entries => Storage.Write(journal, entries), recovery);
            // No ducking until the recovery companion has acknowledged startup.
            using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "--watchdog", Environment.ProcessId.ToString(), Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), journal }
            }))
            {
                for (int i = 0; i < 60 && !stopping; i++)
                {
                    if (File.Exists(Storage.FilePath(journal + ".ready"))) { watchdogReady = true; break; }
                    Thread.Sleep(50);
                }
            }
            if (!watchdogReady) throw new IOException("Recovery companion did not start. Ducking has been disabled.");
            while (!stopping)
            {
                bool disconnected = false;
                while (events.TryDequeue(out var node))
                {
                    if (node == null) { talk.Reset(); disconnected = true; }
                    else talk.Apply(node);
                }
                long now = Environment.TickCount64;
                var s = settings;
                int speakers = talk.Speakers(s);
                bool enabled = s.Enabled && !paused;
                bool forced = Interlocked.Read(ref testUntil) > now && !paused;
                bool immediate = disconnected || restoreRequested || !enabled || !remote.Connected;
                if (restoreRequested) { restoreRequested = false; holdUntil = 0; }
                if (immediate) holdUntil = 0;
                if (enabled && remote.Connected && speakers > 0) holdUntil = now + s.HoldMs;
                bool duck = forced || (enabled && remote.Connected && (speakers > 0 || now < holdUntil));
                volume.Tick(duck, s, now, immediate && !forced);
                string activity = forced ? "Testing ducking for 3 seconds" : !enabled ? "Paused — original volumes restored" : duck ? "Ducking other apps" : "Listening — normal volume";
                View = new(remote.Status, talk.ConnectionCount, speakers, talk.SelfStarts, talk.OtherStarts,
                    volume.ChangedSessions, volume.SeenApps, activity, null);
                Thread.Sleep(duck || volume.ChangedSessions > 0 ? 70 : 140);
            }
        }
        catch (Exception ex)
        {
            Storage.Log("Audio engine stopped: " + ex);
            View = View with { Error = ex.Message, Activity = "Stopped — see error below" };
        }
        finally
        {
            if (volume != null)
            {
                try { volume.Tick(false, settings, Environment.TickCount64, true); }
                catch (Exception ex) { Storage.Log("Restore on exit: " + ex.Message); }
            }
        }
    }
    public void Dispose() { stopping = true; remote.Dispose(); worker.Join(5000); }
}

public static class Recovery
{
    public static bool ParentAlive(int pid, long ticks)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == ticks; }
        catch { return false; }
    }
    public static (List<VolumeEntry> Entries, List<string> Paths) CollectOrphans()
    {
        Directory.CreateDirectory(Storage.Root);
        var collected = new Dictionary<string, VolumeEntry>();
        var oldPaths = new List<string>();
        foreach (var path in Directory.GetFiles(Storage.Root, "recovery-*.json"))
        {
            var name = Path.GetFileName(path);
            var parts = Path.GetFileNameWithoutExtension(name).Split('-');
            if (parts.Length != 3 || !int.TryParse(parts[1], out int pid) || !long.TryParse(parts[2], out long ticks) || ParentAlive(pid, ticks)) continue;
            foreach (var entry in Storage.Read(name, new List<VolumeEntry>())) collected.TryAdd(entry.Key, entry);
            oldPaths.Add(path);
        }
        return (collected.Values.ToList(), oldPaths);
    }
    public static void Watch(int pid, long ticks, string journal)
    {
        try
        {
            Directory.CreateDirectory(Storage.Root);
            File.WriteAllText(Storage.FilePath(journal + ".ready"), "ready");
            while (ParentAlive(pid, ticks)) Thread.Sleep(500);
            using var gate = new Mutex(false, @"Local\TS6Ducker.Recovery");
            try { gate.WaitOne(); } catch (AbandonedMutexException) { }
            try
            {
                var entries = Storage.Read(journal, new List<VolumeEntry>());
                if (entries.Count > 0)
                {
                    var controller = new VolumeController(new WindowsAudio(), x => Storage.Write(journal, x), entries);
                    controller.Tick(false, new Settings(), Environment.TickCount64, true);
                    Storage.Log("Recovery companion restored available sessions after app exit.");
                }
                if (Storage.Read(journal, new List<VolumeEntry>()).Count == 0) File.Delete(Storage.FilePath(journal));
                File.Delete(Storage.FilePath(journal + ".ready"));
            }
            finally { gate.ReleaseMutex(); }
        }
        catch (Exception ex) { Storage.Log("Recovery companion: " + ex.Message); }
    }
}
