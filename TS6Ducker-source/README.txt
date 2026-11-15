TS6 Ducker 0.4.0 — Windows x64
by Philip Weisel

Run TS6Ducker.exe. No installer, administrator rights, or separate .NET runtime is required.

INSTALLING AND SHARING
Alternatively, run TS6Ducker-0.4.0-Setup.exe. Setup installs for your current user,
adds a Start menu shortcut and an entry in Windows Installed apps, and offers an
optional desktop shortcut. No administrator rights or separate .NET install needed.
Share that single Setup.exe file with other Windows x64 users, or share the portable
ZIP. They need TS6, then approve their own TS6 Ducker Remote Apps request. Personal
settings and authorization keys are not included in either distribution.
Updates retain settings. Uninstall stops the app/watcher and removes startup
registration; personal settings in %LOCALAPPDATA%\TS6Ducker are retained.
The current installer is unsigned. The supplied TooCanDo toucan artwork is used
for the application, tray, and installer icons.

SETUP
1. Open TeamSpeak 6 > Settings > Remote Apps. Enable it and note the port (normally 5899).
2. Start TS6 Ducker. Approve its permission request inside TeamSpeak.
3. Keep "When I talk" and "When others talk" enabled as desired.
4. Join a voice channel. The app reports connected servers and live speech counters.

The app initially leaves other apps at 25% of their existing Windows mixer volume
(a 75% reduction). For example, an app at 40% becomes 10% during speech and returns
to 40% afterwards. The setting is "remaining volume", not "reduction".
Typed numeric settings apply after pressing Tab/Enter or leaving the field.

It waits 650 ms after the last speaker stops, then fades back over 400 ms.
Adjust these values in the window. TeamSpeak is always excluded.
Other exclusions accept process names, with or without .exe, separated by commas.

"Test for 3 seconds" temporarily lowers other apps without requiring speech.
"Restore & pause" restores available sessions and disables automatic ducking.
Closing the window hides it to the tray. Double-click the tray icon to reopen it.
Use Quit to exit completely. A small second process is the crash recovery helper.
The toucan is the app/tray icon. An orange arrow badge means audio is ducked;
a grayscale toucan means automatic ducking is paused.

START AND STOP WITH TEAMSPEAK
Enable the checkbox near the top of the settings window. Ducker then starts in
the tray when the TS6 process starts, and restores volumes and exits a few seconds
after the last TS6 process closes. Leaving/disconnecting from a server does not
close Ducker. A brief TS process restart is tolerated.

This option registers a small hidden watcher for your Windows user at sign-in.
While TS6 is closed, only that watcher runs: there is no tray icon or audio engine.
Disable the checkbox to remove the sign-in registration and stop the watcher.
No administrator rights are needed. Manual Quit stays in effect until the next TS
restart; the watcher will not immediately reopen an app you just quit.
If you move the executable, open it manually once to refresh its startup path.
Opening settings manually while TS is already closed leaves the window open so
you can change this option. The app does not change TeamSpeak's startup settings.

VOLUME RESTORATION
Each audio session's original level is saved before changing its volume. Mixer
changes are journaled before they are applied. Disconnect, pause, and normal exit
restore available sessions. A separate helper attempts restoration if the main app
crashes. Recovery records for unavailable devices are kept for the next launch.
Do not deliberately terminate both TS6Ducker processes together during ducking;
the remaining journal can be processed by starting the app again.

If you manually change a mixer slider while ducked, your new value is respected
for the rest of that speech period and becomes the baseline next time. The app
does not change mute states, the device master volume, or your microphone volume.

DATA AND PRIVACY
Settings, encrypted TeamSpeak authorization, a rotating error log, and recovery
journals are stored in %LOCALAPPDATA%\TS6Ducker. The authorization key is protected
by Windows DPAPI for your user account. The app connects only to localhost, does
not record voice, and does not send telemetry or check for updates.
The "Open data folder" button opens these files. Logs omit raw TS messages and keys.

TROUBLESHOOTING AND LIMITS
- Waiting for approval: review TS6 Remote Apps permission requests/notifications.
- No connection: verify the port in both apps; click Reconnect after changing it.
- No speech counters: check channel membership, transmission/mute settings, and
  the relevant self/others switch. TS6's speech status determines when to duck.
- Multiple servers are supported; eligible speech on any connected channel ducks.
- Incoming whispers can be included. Locally muted clients are excluded where TS6
  reports their mute state through its API.
- Ordinary shared-mode Windows audio sessions are supported. Exclusive WASAPI
  and ASIO output bypass these controls. Processes Windows prevents identifying
  are skipped rather than risking accidental attenuation of a TeamSpeak helper.
- Only sessions on currently active output devices can be changed/restored.
- The helper handles main-process exit/crash, not every possible system failure,
  forced termination of both processes, disk failure, or a hung process.
- This is an initial independently developed build, not an official TeamSpeak addon.

VALIDATION
32 automated checks cover speech state, overlaps, channel moves, scoped client IDs,
relative attenuation, restoration/fades, manual mixer changes, new sessions,
exclusions, persistence failure, crash-recovery cases, and a real loopback WebSocket
handshake with fragmented messages and disconnect handling, lifecycle transitions,
settings compatibility, and embedded status icons.
The app was also launched on this computer, detected real Windows audio sessions,
connected to the running TS6 client after approval, and observed both local and
remote speech events. See test-results.txt in this package.

SOURCE
TS6 Ducker is open-source software released under the MIT License.
Copyright (c) 2026 Philip Weisel. See LICENSE for the full terms.
You may use, modify, redistribute, and sell copies subject to the MIT notice
requirements. Third-party components remain under their respective licenses;
their license and attribution notices are included separately.

The separate source archive contains the C# project and test harness. Build it with
the .NET 8 SDK. See BUILD.cmd. NAudio is used under the MIT license; the bundled
.NET runtime and its components have their own notices included in this package.
