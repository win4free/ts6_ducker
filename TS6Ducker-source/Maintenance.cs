namespace TS6Ducker;

public static class Maintenance
{
    private const string EventName = @"Local\TS6Ducker.Maintenance";
    public static bool Requested()
    {
        try { using var signal = EventWaitHandle.OpenExisting(EventName); return signal.WaitOne(0); }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
    private static bool Exists(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var mutex)) return false;
        mutex.Dispose(); return true;
    }
    public static int Shutdown(bool uninstall)
    {
        if (uninstall)
        {
            Storage.Write("settings.json", Storage.LoadSettings() with { StartStopWithTeamSpeak = false });
            Lifecycle.Configure(false);
        }
        using var signal = new EventWaitHandle(true, EventResetMode.ManualReset, EventName);
        signal.Set();
        for (int i = 0; i < 200; i++)
        {
            if (!Exists(@"Local\TS6Ducker.Main") && !Exists(@"Local\TS6Ducker.Monitor"))
            { Thread.Sleep(750); return 0; } // Allow the recovery companion to release the executable.
            Thread.Sleep(100);
        }
        return 1;
    }
}
