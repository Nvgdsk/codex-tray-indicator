#ifndef MyAppVersion
  #error "MyAppVersion must be defined by the release build."
#endif

[Setup]
AppId={{A73C2E3A-30CB-4D49-BED3-7B738890A1E2}
AppName=Codex Tray Indicator
AppVersion={#MyAppVersion}
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
Source: "..\dist\CodexTray.exe"; DestDir: "{app}"; Flags: ignoreversion; AfterInstall: InstallIntegration

[Run]
Filename: "{app}\CodexTray.exe"; Description: "Launch Codex Tray Indicator"; Flags: nowait postinstall skipifsilent

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

procedure InstallIntegration();
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\CodexTray.exe'),
    GetMaintenanceParameters('--install'), '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ResultCode) then
    RaiseException('Unable to start Codex Tray integration setup.')
  else if ResultCode <> 0 then
    RaiseException(Format('Codex Tray integration setup failed with exit code %d.', [ResultCode]));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopInstalledTray();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  StopInstalledTray();
  if not Exec(ExpandConstant('{app}\CodexTray.exe'),
    GetMaintenanceParameters('--uninstall'), '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ResultCode) then
    RaiseException('Unable to start Codex Tray integration removal.')
  else if ResultCode <> 0 then
    RaiseException(Format('Codex Tray integration removal failed with exit code %d.', [ResultCode]));
end;
