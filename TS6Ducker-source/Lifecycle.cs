using System.Diagnostics;
using Microsoft.Win32;

namespace TS6Ducker;

// Watches process lifetime, not server connection state: leaving a server does not exit Ducker.
public sealed class TeamSpeakLifecycle
{
    private bool seen;
    private int missing;
    public bool ShouldStop(bool enabled, bool running)
    {
        if (!enabled) { seen = false; missing = 0; return false; }
        if (running) { seen = true; missing = 0; return false; }
        return seen && ++missing >= 3;
    }
}

public sealed class LaunchOnTeamSpeak
{
    private bool previous;
    public bool ShouldLaunch(bool running)
    { bool launch = running && !previous; previous = running; return launch; }
}

public static class Lifecycle
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "TS6Ducker";
    public static bool TeamSpeakRunning()
    {
        foreach (string name in new[] { "TeamSpeak", "TeamSpeak6", "ts6client" })
        {
            var processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (running) return true;
        }
        return false;
    }
    public static void Configure(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --monitor", RegistryValueKind.String);
            StartHidden("--monitor");
        }
        else key.DeleteValue(RunName, false);
    }
    private static void StartHidden(string argument)
    {
        using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            ArgumentList = { argument }
        });
    }
    public static void Monitor()
    {
        using var mutex = new Mutex(true, @"Local\TS6Ducker.Monitor", out bool created);
        if (!created) return;
        var launch = new LaunchOnTeamSpeak();
        // While TeamSpeak is closed this is the only Ducker process: no audio engine or tray icon.
        while (!Maintenance.Requested() && Storage.LoadSettings().StartStopWithTeamSpeak)
        {
            try { if (launch.ShouldLaunch(TeamSpeakRunning())) StartHidden("--with-teamspeak"); }
            catch (Exception ex) { Storage.Log("TeamSpeak watcher: " + ex.Message); }
            Thread.Sleep(1000);
        }
    }
}
