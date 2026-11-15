using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace TS6Ducker;

public interface IAudioSession : IDisposable
{
    string Key { get; }
    string DeviceId { get; }
    string InstanceId { get; }
    string ProcessName { get; }
    float Volume { get; set; }
}
public interface IAudioBackend { List<IAudioSession> Enumerate(); }

public sealed class WindowsAudio : IAudioBackend
{
    public List<IAudioSession> Enumerate()
    {
        var result = new List<IAudioSession>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        try
                        {
                            if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) { session.Dispose(); continue; }
                            string process = "System sounds";
                            if (!session.IsSystemSoundsSession)
                            {
                                using var p = Process.GetProcessById((int)session.GetProcessID);
                                process = p.ProcessName;
                                // Exclude every TeamSpeak subprocess, including Chromium audio services.
                                try
                                {
                                    string path = p.MainModule?.FileName ?? "";
                                    if (path.Contains("TeamSpeak", StringComparison.OrdinalIgnoreCase)) process = "TeamSpeak";
                                }
                                catch { if (!process.Contains("teamspeak", StringComparison.OrdinalIgnoreCase)) { session.Dispose(); continue; } }
                            }
                            result.Add(new WindowsSession(device.ID, session, process));
                        }
                        catch { session.Dispose(); }
                    }
                }
                catch (Exception ex) { Storage.Log("Audio device: " + ex.Message); }
            }
        }
        return result;
    }
    private sealed class WindowsSession(string deviceId, AudioSessionControl control, string process) : IAudioSession
    {
        public string Key => deviceId + "|" + InstanceId;
        public string DeviceId => deviceId;
        public string InstanceId { get; } = control.GetSessionInstanceIdentifier;
        public string ProcessName => process;
        public float Volume { get => control.SimpleAudioVolume.Volume; set => control.SimpleAudioVolume.Volume = Math.Clamp(value, 0, 1); }
        public void Dispose() => control.Dispose();
    }
}

public record VolumeEntry(string Key, string DeviceId, string InstanceId, string ProcessName, float Original, float Last, float Previous);

// Journal is committed BEFORE each mixer write. Last and Previous cover a crash inside that write.
public sealed class VolumeController
{
    private readonly IAudioBackend backend;
    private readonly Action<List<VolumeEntry>> save;
    private readonly Dictionary<string, VolumeEntry> entries;
    private readonly HashSet<string> userOverrides = new();
    private readonly Dictionary<string, float> fadeFrom = new();
    private bool wasDucking;
    private long fadeStart;
    public int ChangedSessions => entries.Count;
    public string[] SeenApps { get; private set; } = [];
    public VolumeController(IAudioBackend backend, Action<List<VolumeEntry>> save, IEnumerable<VolumeEntry>? recovery = null)
    { this.backend = backend; this.save = save; entries = (recovery ?? []).ToDictionary(x => x.Key); }
    public static bool Excluded(string name, Settings s) => name.Contains("teamspeak", StringComparison.OrdinalIgnoreCase)
        || name.Equals("TS6Ducker", StringComparison.OrdinalIgnoreCase)
        || s.ExcludedProcesses.Split([',',';','\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => Path.GetFileNameWithoutExtension(x).Equals(name, StringComparison.OrdinalIgnoreCase));
    private void Commit() => save(entries.Values.ToList());
    public void Tick(bool duck, Settings s, long now, bool immediate = false)
    {
        if (duck != wasDucking)
        {
            fadeFrom.Clear();
            if (!duck) { fadeStart = now; foreach (var e in entries.Values) fadeFrom[e.Key] = e.Last; }
            else userOverrides.Clear();
            wasDucking = duck;
        }
        var sessions = backend.Enumerate();
        try
        {
            SeenApps = sessions.Select(x => x.ProcessName).Distinct().OrderBy(x => x).ToArray();
            foreach (var session in sessions)
            {
                try
                {
                    bool tracked = entries.TryGetValue(session.Key, out var e);
                    float current = session.Volume;
                    bool excluded = Excluded(session.ProcessName, s);
                    if (tracked && Math.Abs(current - e!.Last) > .002f && Math.Abs(current - e.Previous) > .002f)
                    {
                        // A user/application changed this mixer slider. Respect that value for this utterance.
                        entries.Remove(session.Key); userOverrides.Add(session.Key); Commit(); continue;
                    }
                    if (duck && !excluded && !userOverrides.Contains(session.Key))
                    {
                        e ??= new VolumeEntry(session.Key, session.DeviceId, session.InstanceId, session.ProcessName, current, current, current);
                        float target = e.Original * s.RemainingPercent / 100f;
                        if (Math.Abs(target - current) < .0001f) continue;
                        Write(session, e, current, target);
                    }
                    else if (tracked)
                    {
                        float progress = immediate || excluded || s.FadeMs == 0 ? 1 : Math.Clamp((now - fadeStart) / (float)s.FadeMs, 0, 1);
                        float start = fadeFrom.GetValueOrDefault(session.Key, e!.Last);
                        float target = start + (e!.Original - start) * progress;
                        Write(session, e, current, target);
                        if (progress >= 1) { entries.Remove(session.Key); fadeFrom.Remove(session.Key); Commit(); }
                    }
                }
                catch (Exception ex) { Storage.Log("Audio session: " + ex.Message); }
            }
        }
        finally { foreach (var session in sessions) session.Dispose(); }
    }
    private void Write(IAudioSession session, VolumeEntry entry, float current, float target)
    {
        entries[entry.Key] = entry with { Last = target, Previous = current };
        Commit(); // Failure to persist aborts the audio write.
        session.Volume = target;
        // Once write succeeds only Last is considered our value in normal operation.
        entries[entry.Key] = entries[entry.Key] with { Previous = target };
    }
}
