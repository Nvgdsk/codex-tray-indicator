[Setup]
AppId={{A73C2E3A-30CB-4D49-BED3-7B738890A1E2}
AppName=Codex Tray Indicator
AppVersion=1.0.0
DefaultDirName={localappdata}\Programs\CodexTray
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=CodexTraySetup
UninstallDisplayIcon={app}\CodexTray.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
WizardStyle=modern

[Files]
Source: "..\dist\CodexTray.exe"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{app}\CodexTray.exe"; Parameters: "--install"; Flags: waituntilterminated
Filename: "{app}\CodexTray.exe"; Description: "Launch Codex Tray Indicator"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\CodexTray.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCodexTrayIntegration"
