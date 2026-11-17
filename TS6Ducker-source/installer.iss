#define AppVersion "0.4.2"
#ifndef PackageDir
#define PackageDir "publish"
#endif
#ifndef AppIcon
#define AppIcon "Assets\ducker.ico"
#endif
#ifndef InstallerOutputDir
#define InstallerOutputDir "installer-output"
#endif
[Setup]
AppId={{04D888DF-6D9A-4DF7-978B-17872EA8F976}
AppName=TS6 Ducker
AppVersion={#AppVersion}
AppPublisher=Philip Weisel
AppCopyright=by Philip Weisel
DefaultDirName={localappdata}\Programs\TS6Ducker
DefaultGroupName=TS6 Ducker
PrivilegesRequired=lowest
#ifdef InstallerTest
CreateUninstallRegKey=no
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutputDir}
OutputBaseFilename=TS6Ducker-{#AppVersion}-Setup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\TS6Ducker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PackageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\TS6Ducker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\*.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
#ifndef InstallerTest
Name: "{userprograms}\TS6 Ducker"; Filename: "{app}\TS6Ducker.exe"
Name: "{userdesktop}\TS6 Ducker"; Filename: "{app}\TS6Ducker.exe"; Tasks: desktopicon
#endif

[Run]
Filename: "{app}\TS6Ducker.exe"; Description: "Launch TS6 Ducker"; Flags: nowait postinstall skipifsilent

[UninstallRun]
#ifndef InstallerTest
Filename: "{app}\TS6Ducker.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: StopDucker
#endif

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
#ifndef InstallerTest
  if CheckForMutexes('Local\TS6Ducker.Main,Local\TS6Ducker.Monitor') then
  begin
    ExtractTemporaryFile('TS6Ducker.exe');
    if not Exec(ExpandConstant('{tmp}\TS6Ducker.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := 'Please quit TS6 Ducker before installing.'
    else if ResultCode <> 0 then
      Result := 'Please disable "Start and stop with TeamSpeak" in the old Ducker, then choose Quit and retry setup.';
  end;
#endif
end;
