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
Filename: "{app}\CodexTray.exe"; Parameters: "{code:GetMaintenanceParameters|--install}"; Flags: waituntilterminated
Filename: "{app}\CodexTray.exe"; Description: "Launch Codex Tray Indicator"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\CodexTray.exe"; Parameters: "{code:GetMaintenanceParameters|--uninstall}"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCodexTrayIntegration"

[Code]
function GetMaintenanceParameters(Value: String): String;
begin
  Result := Value;
  if ExpandConstant('{param:CODEXTRAYQUIET|0}') = '1' then
    Result := Result + ' --quiet';
end;

procedure StopInstalledTray();
var
  ExecutablePath: String;
  ResultCode: Integer;
begin
  ExecutablePath := ExpandConstant('{app}\CodexTray.exe');
  if FileExists(ExecutablePath) then
  begin
    Exec(ExecutablePath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopInstalledTray();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopInstalledTray();
  Result := True;
end;
