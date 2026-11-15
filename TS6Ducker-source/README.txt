# TS6 Ducker

**Automatically lower other apps while someone speaks in TeamSpeak 6.**

by Philip Weisel

TS6 Ducker is a Windows tray app that lowers game, music, and other application audio when you or someone in your TeamSpeak channel talks. TeamSpeak stays at its existing volume. When speech ends, the other apps fade back to their original mixer levels.

This is an independent project and is not affiliated with TeamSpeak.

## Download and install

1. Open this repository's **Releases** section.
2. Download **`TS6Ducker-0.4.1-Setup.exe`** from the release's **Assets** list.
3. Run the installer, then launch **TS6 Ducker** from the Start menu.

The installer adds an entry to Windows Installed apps and offers an optional desktop shortcut. It installs for your Windows user without requiring administrator rights. The .NET runtime is included.

GitHub's automatically generated **Source code** downloads are for inspecting or building the project. To install the app, choose the **Setup.exe** asset.

**Requirements:** Windows 10 or 11, 64-bit x64, and TeamSpeak 6 with Remote Apps support. Windows 10 compatibility is expected but has not been independently tested on a separate Windows 10 machine. Linux and macOS are not supported by this build.

The current installer is unsigned.

## Connect to TeamSpeak

1. In TeamSpeak 6, open **Settings → Remote Apps** and enable the interface.
2. Check that the port matches the one in Ducker. The default is **5899**.
3. Approve **TS6 Ducker** in TeamSpeak's permission requests or notifications.
4. Join a voice channel. Ducker should show **Connected to TeamSpeak** and show who is currently speaking.

Each person approves their own connection. Release downloads contain no personal settings or authorization keys.

## Settings

| Setting | What it does |
| --- | --- |
| **Start and stop with TeamSpeak** | Starts Ducker in the tray when TS6 opens, then restores volumes and exits when TS6 closes. A small background watcher starts at Windows sign-in to detect TS6. |
| **Enable automatic ducking** | Turns speech-triggered volume reduction on or off. |
| **When I talk / When others talk** | Chooses which speakers trigger ducking. |
| **Include incoming whispers** | Also triggers ducking for incoming whispers. |
| **Other apps' remaining volume** | Sets the fraction of each app's existing mixer volume to keep during speech. |
| **Wait after speech ends** | Keeps audio lowered briefly between words and sentences. Default: **650 ms**. |
| **Fade back to normal** | Controls how quickly audio returns after the waiting period. Default: **400 ms**. |
| **Also leave these apps unchanged** | Excludes additional apps by process name, separated by commas, for example `Discord, obs64`. |

**Remaining volume is not the reduction amount.** A setting of **15%** means an **85% reduction**. An app normally at 40% mixer volume is temporarily lowered to 6%, then restored to 40%. The default setting is 25% remaining volume.

Press **Tab** or leave a numeric field to apply a typed value.

- **Test for 3 seconds** demonstrates ducking without requiring speech.
- **Restore & pause** restores available sessions and disables automatic ducking.
- Closing the window hides it to the tray. Double-click the tray icon to reopen settings.
- **Quit** exits and restores available sessions. If automatic start/stop is enabled, a manual quit lasts until the next TS6 restart.

An orange arrow badge on the toucan means audio is ducked. A grayscale toucan means automatic ducking is paused. TeamSpeak is always excluded.

## Privacy and volume recovery

The app connects to TeamSpeak on **localhost**. It does not record voice, send telemetry, or check for updates.

Settings, error logs, recovery records, and the TeamSpeak authorization key are stored in `%LOCALAPPDATA%\TS6Ducker`. The key is protected for your Windows account using DPAPI. Use **Open data folder** to access these files.

Original mixer levels are recorded before volume changes. A separate recovery helper attempts to restore them if the main app crashes. Manual mixer adjustments made while ducked are respected for the remainder of that speech period. The app does not change your microphone level, device master volume, or mute states.

## Troubleshooting

- **Waiting for approval:** check TeamSpeak's Remote Apps permission requests and notifications.
- **Cannot connect:** check the port in both apps, then click **Reconnect**.
- **No speech detected:** check that you are in a voice channel and the relevant speaker switches are enabled.
- **An app is not lowered:** exclusive WASAPI and ASIO audio bypass these session controls. Processes Windows prevents identifying are skipped. Only currently active output devices can be controlled.
- **Volume remains low after a failure:** start Ducker again so it can process its recovery records. Recovery cannot cover every system failure or forced termination of both the app and its helper.

Disconnecting from a server does not close Ducker; automatic exit follows the TS6 process itself.

## Updates and uninstalling

Download and run the installer from a newer release to update. Existing settings are retained. When upgrading an older portable build, disable its automatic start/stop option and choose **Quit** first if setup asks you to close it.

To uninstall, use **Windows Settings → Apps → Installed apps** (or **Apps & features** on Windows 10). Uninstall removes the app and its startup registration. Personal settings in `%LOCALAPPDATA%\TS6Ducker` are retained.

## Build from source

The source is in this repository. Building requires Windows and the **.NET 8 SDK**.

```bat
BUILD.cmd
```

The script publishes a self-contained Windows x64 executable into `publish` and runs the included regression checks.

To build the installer, install **Inno Setup**, make its compiler available on your PATH, then run:

```bat
ISCC.exe installer.iss
```

The installer is written to `installer-output`.

## License

**MIT License — Copyright (c) 2026 Philip Weisel.** See [LICENSE](LICENSE).

Third-party components retain their own licenses. The NAudio and .NET notices are included separately in this repository and in the installer.
