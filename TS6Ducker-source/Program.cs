namespace TS6Ducker;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--shutdown" || args[0] == "--uninstall"))
        { Environment.ExitCode = Maintenance.Shutdown(args[0] == "--uninstall"); return; }
        if (args.Length == 1 && args[0] == "--monitor") { Lifecycle.Monitor(); return; }
        if (args.Length == 4 && args[0] == "--watchdog")
        {
            // Core Audio operates on an MTA thread in both processes.
            var t = new Thread(() => Recovery.Watch(int.Parse(args[1]), long.Parse(args[2]), args[3]));
            t.SetApartmentState(ApartmentState.MTA); t.Start(); t.Join(); return;
        }
        if (args.Length >= 1 && args[0] == "--self-test")
        { SelfTests.Run(args.Length > 1 ? args[1] : "self-test-results.txt"); return; }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\TS6Ducker.Main", out bool created);
        if (!created) { if (!args.Contains("--with-teamspeak")) MessageBox.Show("TS6 Ducker is already running. Double-click its tray icon to open settings.", "TS6 Ducker"); return; }
        if (args.Contains("--with-teamspeak") && !Lifecycle.TeamSpeakRunning()) return;
        try { Application.Run(new MainForm(args.Contains("--with-teamspeak"))); }
        catch (Exception ex) { Storage.Log(ex.ToString()); MessageBox.Show(ex.Message, "TS6 Ducker"); }
    }
}
