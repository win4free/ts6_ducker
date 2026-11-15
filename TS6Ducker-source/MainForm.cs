using System.Diagnostics;

namespace TS6Ducker;

public sealed class MainForm : Form
{
    private readonly Engine engine;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Label status = new() { AutoSize = false, Height = 80, Dock = DockStyle.Fill };
    private readonly Label volumeLabel = new() { AutoSize = true };
    private readonly Label details = new() { AutoSize = false, Height = 75, Dock = DockStyle.Fill };
    private readonly TextBox apps = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly CheckBox enabled = new() { Text = "Enable automatic ducking", AutoSize = true };
    private readonly CheckBox self = new() { Text = "When I talk", AutoSize = true };
    private readonly CheckBox others = new() { Text = "When others talk", AutoSize = true };
    private readonly CheckBox whispers = new() { Text = "Include incoming whispers", AutoSize = true };
    private readonly CheckBox followTeamSpeak = new() { Text = "Start and stop with TeamSpeak", AutoSize = true };
    private readonly TeamSpeakLifecycle lifecycle = new();
    private readonly Icon normalIcon = AppIcons.Load("ducker"), duckingIcon = AppIcons.Load("ducking"), pausedIcon = AppIcons.Load("paused");
    private long nextLifecycleCheck;
    private string iconState = "normal";
    private readonly NumericUpDown remaining = Number(0, 100, 25), hold = Number(0, 5000, 650), fade = Number(0, 3000, 400), port = Number(1, 65535, 5899);
    private readonly TextBox exclusions = new() { Dock = DockStyle.Fill, PlaceholderText = "e.g. Discord, obs64 (comma separated)" };
    private bool quitting;
    private static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 100 };
    public MainForm(bool automatic = false)
    {
        Text = "TS6 Ducker"; Icon = normalIcon;
        ClientSize = new Size(640, 820); MinimumSize = new Size(600, 790);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var settings = Storage.LoadSettings();
        if (automatic) lifecycle.ShouldStop(true, true);
        enabled.Checked = settings.Enabled; self.Checked = settings.OnSelf; others.Checked = settings.OnOthers; whispers.Checked = settings.IncludeWhispers;
        followTeamSpeak.Checked = settings.StartStopWithTeamSpeak;
        remaining.Value = settings.RemainingPercent; hold.Value = settings.HoldMs; fade.Value = settings.FadeMs; port.Value = settings.Port; exclusions.Text = settings.ExcludedProcesses;
        engine = new Engine(settings);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 16, AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        void Full(Control control, int row) { panel.Controls.Add(control, 0, row); panel.SetColumnSpan(control, 2); }
        var heading = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "TS6 Ducker", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty });
        heading.Controls.Add(new Label { Text = "by Philip Weisel", Font = new Font("Segoe UI", 9), ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(3, 0, 0, 8) });
        Full(heading, 0);
        followTeamSpeak.Margin = new Padding(3, 6, 3, 12);
        enabled.Margin = new Padding(3, 6, 3, 8);
        whispers.Margin = new Padding(3, 6, 3, 14);
        self.Margin = new Padding(3, 3, 22, 3);
        Full(followTeamSpeak, 1); Full(status, 2); Full(enabled, 3);
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) }; options.Controls.AddRange([self, others]); Full(options, 4);
        Full(whispers, 5);
        void Field(string text, Control control, int row) {
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 7) }, 0, row);
            control.Margin = new Padding(3, 5, 3, 7); panel.Controls.Add(control, 1, row);
        }
        Field("Other apps' remaining volume", remaining, 6);
        volumeLabel.Margin = new Padding(3, 0, 3, 22);
        Full(volumeLabel, 7);
        Field("Wait after speech ends (ms)", hold, 8);
        Field("Fade back to normal (ms)", fade, 9);
        Field("TS6 Remote Apps port", port, 10);
        Field("Also leave these apps unchanged", exclusions, 11);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
        Button Button(string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); return b; }
        actions.Controls.Add(Button("Test for 3 seconds", () => { engine.Resume(); engine.Test(); }));
        actions.Controls.Add(Button("Restore & pause", () => { enabled.Checked = false; engine.Restore(); }));
        actions.Controls.Add(Button("Reconnect", engine.Reconnect)); Full(actions, 12);
        Full(details, 13); apps.MinimumSize = new Size(0, 75); Full(apps, 14);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        footer.Controls.Add(Button("Hide to tray", Hide));
        footer.Controls.Add(Button("Open data folder", () => Process.Start(new ProcessStartInfo("explorer.exe", Storage.Root) { UseShellExecute = true })));
        footer.Controls.Add(Button("Quit", Quit)); footer.Margin = new Padding(0, 10, 0, 0); Full(footer, 15);
        for (int i = 0; i < 16; i++) panel.RowStyles.Add(new RowStyle(i == 14 ? SizeType.Percent : SizeType.AutoSize, i == 14 ? 100 : 0));
        panel.RowStyles[2] = new RowStyle(SizeType.Absolute, 56);
        panel.RowStyles[13] = new RowStyle(SizeType.Absolute, 78);
        Controls.Add(panel);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Restore & pause", null, (_, _) => { enabled.Checked = false; engine.Restore(); });
        menu.Items.Add("Resume", null, (_, _) => { enabled.Checked = true; engine.Resume(); });
        menu.Items.Add("Quit", null, (_, _) => Quit());
        tray = new NotifyIcon { Icon = normalIcon, Text = "TS6 Ducker", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowSettings();
        foreach (var check in new[] { enabled, self, others, whispers }) check.CheckedChanged += (_, _) => Save();
        foreach (var number in new[] { remaining, hold, fade, port }) number.ValueChanged += (_, _) => Save();
        exclusions.Leave += (_, _) => Save();
        followTeamSpeak.CheckedChanged += (_, _) => Save();
        UpdateVolumeLabel();
        timer.Tick += (_, _) => RefreshStatus(); timer.Start();
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); engine.Dispose(); normalIcon.Dispose(); duckingIcon.Dispose(); pausedIcon.Dispose(); };
        Shown += (_, _) => {
            engine.Start();
            if (settings.StartStopWithTeamSpeak) { try { Lifecycle.Configure(true); } catch (Exception ex) { Storage.Log("Startup registration: " + ex.Message); } }
            if (automatic) Hide();
        };
        SystemEventsSubscribe();
    }
    private void SystemEventsSubscribe()
    {
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        FormClosed += (_, _) => Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
    }
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e) => engine.Restore();
    private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void Quit() { quitting = true; Close(); }
    private void Save()
    {
        try
        {
            var next = new Settings((int)port.Value, (int)remaining.Value, (int)hold.Value, (int)fade.Value,
                self.Checked, others.Checked, enabled.Checked, exclusions.Text.Trim(), whispers.Checked, followTeamSpeak.Checked);
            bool changedPort = engine.Settings.Port != next.Port;
            bool changedLifecycle = engine.Settings.StartStopWithTeamSpeak != next.StartStopWithTeamSpeak;
            // Persist before starting the monitor so it sees the new setting immediately.
            if (changedLifecycle)
            {
                var previous = engine.Settings;
                engine.Update(next);
                try { Lifecycle.Configure(next.StartStopWithTeamSpeak); }
                catch { engine.Update(previous); followTeamSpeak.Checked = previous.StartStopWithTeamSpeak; throw; }
            }
            engine.Update(next); if (enabled.Checked) engine.Resume();
            if (changedPort) engine.Reconnect(); UpdateVolumeLabel();
        }
        catch (Exception ex) { MessageBox.Show("Could not save settings: " + ex.Message, Text); }
    }
    private void UpdateVolumeLabel() => volumeLabel.Text = $"{100 - remaining.Value}% reduction; preserves each app's existing mixer level.";
    private void RefreshStatus()
    {
        if (Maintenance.Requested()) { Quit(); return; }
        if (Environment.TickCount64 >= nextLifecycleCheck)
        {
            nextLifecycleCheck = Environment.TickCount64 + 1000;
            if (lifecycle.ShouldStop(engine.Settings.StartStopWithTeamSpeak, Lifecycle.TeamSpeakRunning())) { Quit(); return; }
        }
        var v = engine.View;
        status.Text = v.Connection + "\n" + v.Activity + (v.Error == null ? "" : "\n" + v.Error);
        status.ForeColor = v.Error != null ? Color.Firebrick : SystemColors.ControlText;
        details.Text = $"Servers: {v.Servers}    Speaking: {v.Speakers}    Sessions changed: {v.ChangedSessions}\nSpeech starts seen — you: {v.SelfStarts}, others: {v.OtherStarts}\nTeamSpeak is always excluded. Closing this window keeps the app in the tray.";
        string text = "Audio apps detected:\r\n" + string.Join("\r\n", v.Apps.Select(x => x + (VolumeController.Excluded(x, engine.Settings) ? " (excluded)" : "")));
        if (apps.Text != text) apps.Text = text;
        tray.Text = "TS6 Ducker — " + (engine.Settings.Enabled ? "enabled" : "paused");
        string nextIcon = !engine.Settings.Enabled ? "paused" : v.ChangedSessions > 0 ? "ducking" : "normal";
        if (nextIcon != iconState) { tray.Icon = nextIcon == "paused" ? pausedIcon : nextIcon == "ducking" ? duckingIcon : normalIcon; iconState = nextIcon; }
    }
}
