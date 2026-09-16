[English](README.md) | [Українська](README.uk.md)

# Codex Tray Indicator

A small Windows tray app that shows the lifecycle state of Codex CLI running in WSL.
Optionally, it mirrors the indicator on a Turing 3.5-inch Revision A USB display.
No prompt or response text is displayed or retained by the indicator.

The source is licensed under [MIT](LICENSE). See [release history](CHANGELOG.md).
v1.3.0 is being prepared for the first public unsigned release; download links become
available after the repository and release are published.

<a id="supported-scope"></a>

## Supported scope

- Windows 11 x64, a normal Windows user account, and WSL2 with Windows executable interop enabled.
- Tested WSL baseline: Ubuntu with Codex CLI `0.154.0`. Codex must expose
  `SessionStart`, `UserPromptSubmit`, `Stop`, `Interrupt`, and `SessionEnd` hooks.
  Compatibility with other CLI versions/distributions is not guaranteed.
- One selected WSL distribution, using the Linux user's default `~/.codex/hooks.json`.
  Custom Codex configuration homes are not automatically detected.
- Optional display: Turing Smart Screen 3.5-inch, Revision A, 320×480.
  Revision B/XuanFang and other screen protocols are not supported.
- Native Windows Codex CLI and the Codex desktop app are **not tracked**.

Confirm that the installer can find Codex, replacing `Ubuntu` with your distribution:

```powershell
wsl.exe --list --verbose
wsl.exe -d Ubuntu --exec sh -lc 'command -v codex && codex --version'
```

If the second command fails, fix the WSL installation/PATH before installing this app.

<a id="installation"></a>

## Installation

1. Open [Latest release](../../releases/latest).
2. Download `CodexTraySetup.exe` and `SHA256SUMS.txt` from the same release.
   Download `CodexTray.exe` too if you want the portable option.
3. [Verify the checksums](#checksums) and read the [unsigned-release warning](#unsigned-release).
4. Run `CodexTraySetup.exe`. Setup installs per user into
   `%LOCALAPPDATA%/Programs/CodexTray`, integrates with one WSL distribution,
   and enables startup. Administrator rights are not required.
5. Keep the launch option selected at the end of interactive setup.
   Find the indicator in the Windows notification area (including hidden icons).
6. Open Codex in the selected WSL distribution and complete [hook Trust](#hook-trust).

Both release executables are self-contained: end users need **no separate .NET runtime**.
Setup does not install WSL or Codex, authenticate your Codex account, or configure other distributions.

For portable use, keep `CodexTray.exe` in a permanent location. From Windows PowerShell:

```powershell
$codexTray = 'C:/Apps/CodexTray/CodexTray.exe'
& $codexTray --install
Start-Process -FilePath $codexTray -WindowStyle Hidden
```

Replace the path with your actual file location, then complete hook Trust.
Moving the portable executable breaks hook/startup paths; [remove the old integration](#uninstall)
before moving it and install again from the new path.

<a id="hook-trust"></a>

## Review hooks and select Trust

In Codex CLI, enter `/hooks`. Review the handlers whose command ends with
`--hook --integration-id codex-tray-indicator-v1`; they must point to the
Windows executable you installed. Select **Trust** for the indicator's handlers.

Codex skips new or changed non-managed hooks until you review their exact definition.
After reinstalling or changing the executable path, review any newly pending hooks again.
See [official OpenAI hook documentation](https://learn.chatgpt.com/docs/hooks).

The installer does not bypass hook trust. Start a new Codex session after the review so
its `SessionStart` event reaches the running tray. Synthetic tests do not grant Trust.

<a id="tray-states"></a>

## Tray states

| Color | Tooltip | Meaning |
|---|---|---|
| Green `#22C55E` | `Codex: Ready` | At least one active session, no active turn |
| Yellow `#EAB308` | `Codex: Busy` | At least one session is processing a turn |
| Red `#EF4444` | `Codex: Error` | IPC/protocol or application error; not an interpretation of a Codex answer |
| Gray `#6B7280` | `Codex: Inactive` | No known active sessions since the tray started |

`SessionStart` → Ready; `UserPromptSubmit` → Busy; matching `Stop` or
`Interrupt` → Ready; `SessionEnd` removes that session. Any remaining busy
session keeps the aggregate state Busy. A compaction `SessionStart` does not reset Busy.

State exists only in RAM. Restarting the tray starts at Inactive; it cannot reconstruct
existing sessions until new hooks arrive. Closing a terminal does not itself prove a
`SessionEnd` occurred: the gray state follows Codex's actual lifecycle event.

Right-click the tray icon for settings. **Reconnect / Test** sets a synthetic Ready
state and reconnects USB; it does not reconnect missing hooks or validate a real Codex turn.

<a id="notifications"></a>

## Notifications

**Notifications** toggles Windows balloon notifications. **Codex finished** appears
only on a genuine aggregate Busy → Ready transition caused by `Stop` or `Interrupt`.
Synthetic tests never notify; completing one turn while another remains Busy does not notify.
Windows notification settings or Do Not Disturb can suppress visible notifications.

<a id="startup"></a>

## Start with Windows

**Start with Windows** toggles a per-user startup entry under
`HKCU/Software/Microsoft/Windows/CurrentVersion/Run/CodexTray`.
Setup enables it only after hook configuration succeeds.
**Exit** stops the tray/pipe and releases USB without removing integration or startup.

<a id="usb-screen"></a>

## USB screen configuration

The display is optional. Without a supported screen, the tray continues to work.

1. Connect a Revision A display and close vendor software or other apps using its COM port.
2. Right-click the tray → **USB screen (Turing 3.5")**.
3. **Automatic** selects a single matching device: USB serial `USB35INCHIPSV2`
   or hardware ID `VID_1A86&PID_5722`. If multiple devices match, choose a COM port
   manually. `COM3` in examples is not a universal default; use your actual port.
4. Choose **Orientation**: Portrait 320×480, Landscape 480×320, or an upside-down variant.
5. **Reconnect screen** retries immediately. **Off** turns off output and releases the port.

**Pixel shift animation** is enabled by default: the status block moves by 6 pixels
every 10 seconds and bounces within safe margins. It is not a hardware guarantee
against image retention or burn-in.

**Animated character**, also enabled by default, shows a pixel robot: Ready waves/blinks,
Busy types, Inactive sleeps, Error raises its arms. The mascot updates every 500 ms
using partial frames; disabling it restores the colored circle.

Serial rendering/writes run in a separate worker with only the latest pending state.
USB connection checks/retries do not poll Codex or WSL. USB errors are shown separately
in the menu and never change the Codex state. Preferences persist under `HKCU/Software/CodexTray`.
The display receives rendered status graphics, not prompts, responses, or session IDs.

<a id="wsl-selection"></a>

## Multiple WSL distributions

Detection lists WSL distributions, ignores `docker-desktop`, and probes `codex`
through `sh -lc`. When several distributions contain Codex and no valid selection
is saved, installation stops rather than choosing arbitrarily.

Before first installation, save the exact distribution name in Windows PowerShell:

```powershell
$settingsPath = 'HKCU:/Software/CodexTray'
New-Item -Path $settingsPath -Force | Out-Null
New-ItemProperty -Path $settingsPath -Name WslDistribution -Value 'Ubuntu' -PropertyType String -Force | Out-Null
```

Then run setup again. Replace `Ubuntu` with a name from `wsl.exe --list --quiet`.
To change an existing integration, first run `--shutdown` and `--uninstall` from
the old executable while the old distribution is still recorded. Only then change
`WslDistribution`, install again, launch the tray, and review Trust.
Do not simply overwrite the selection: that can leave hooks behind in the old distribution.

<a id="checksums"></a>

## Verify SHA-256 checksums

In the folder containing the downloads, run Windows PowerShell:

```powershell
Get-Content ./SHA256SUMS.txt
Get-FileHash -Algorithm SHA256 -LiteralPath ./CodexTraySetup.exe
# Also verify this file if downloaded:
Get-FileHash -Algorithm SHA256 -LiteralPath ./CodexTray.exe
```

Compare each entire 64-character hash to the line for that exact filename in
`SHA256SUMS.txt`. A mismatch means **do not run the file**; download the matching
release assets again and report continued mismatches.
Checksums detect corruption/mismatched downloads; they do not independently prove
publisher identity if the release account or manifest is compromised.

<a id="unsigned-release"></a>

## Unsigned release / Unknown Publisher

v1.3.0 has no Authenticode signature. Windows may show **Unknown Publisher** or
`Unknown publisher`, and SmartScreen may warn about an unrecognized application.
A matching checksum does not make those warnings disappear.

Download only from this project's release page, verify the files, and review the source.
Do not disable antivirus, SmartScreen, organizational policy, or Codex hook-trust controls.
If your security policy blocks unsigned applications, do not run this release;
ask your administrator, review/build the source where permitted, or wait for a signed release.

<a id="source-build"></a>

## Build from source

Use Windows x64 with Git, .NET SDK **10.0.401**, and Inno Setup **7.1.0**.
Windows PowerShell 5.1 can run bootstrap/release/repository-contract scripts;
use PowerShell 7 (`pwsh`) for the WSL IPC proof.
Inno Setup has separate [licensing terms](https://jrsoftware.org/isinfo.php);
the project's MIT license does not license the build tools.

Clone this repository using GitHub's **Code** URL, open Windows PowerShell in its root,
and review the scripts before running:

```powershell
./scripts/bootstrap-build-tools.ps1
dotnet --version
dotnet restore ./CodexTray.sln --locked-mode
dotnet format ./CodexTray.sln --verify-no-changes --no-restore
dotnet build ./CodexTray.sln --configuration Release --no-restore
powershell.exe -NoProfile -File ./scripts/test-repository-contract.ps1
powershell.exe -NoProfile -File ./scripts/build-release.ps1 -AllowUnsigned
```

Bootstrap installs the exact tool versions through Winget if missing; it needs network
access and may request system installation approval. Run scripts only where permitted
by your execution policy; do not weaken global security settings to make them run.

`global.json` pins SDK selection, and NuGet lock files are committed.
`src/CodexTray/CodexTray.csproj` is the single product-version source; the release
script passes it to Inno. The unsigned switch is an explicit acknowledgement,
not a signing operation. Without it, unsigned output is rejected.

The release script replaces only repository-local `artifacts/publish` and `dist`,
gracefully stopping a running previous `dist/CodexTray.exe` first. Full tests require
the Ubuntu/Codex integration baseline below. `-PureTestsOnly` explicitly excludes
real WSL integration and USB hardware tests for hosted builds; it does not replace
full local testing or manual release acceptance. Success produces exactly
`CodexTray.exe`, `CodexTraySetup.exe`, and `SHA256SUMS.txt` in `dist`;
none belong in Git. Tool/package pinning makes the build inputs repeatable,
not a guarantee of identical installer bytes across machines.

<a id="tests"></a>

## Tests and manual acceptance

Run these commands from the repository root after locked restore/build.

Automated tests without WSL integration or hardware:

```powershell
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category!=Integration&Category!=UsbHardware'
```

Full tests include real WSL integration tests and require a distribution named
`Ubuntu` with Codex visible to `sh -lc`; USB hardware tests skip unless explicitly enabled:

```powershell
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~WslHookConfigStoreIntegrationTests'
```

The hook-store test uses an isolated `/tmp/codex-tray-tests-<guid>` directory,
not your real hook configuration; it verifies atomic install/reinstall/uninstall,
foreign-handler preservation and backup behavior, then removes its test directory.

For the real WSL → Windows pipe proof, set USB to Off and exit any existing tray first.
After building the release, run in PowerShell 7:

```powershell
pwsh -NoProfile -File ./scripts/test-wsl-ipc.ps1 -Distribution Ubuntu -Cycles 100
```

This starts/stops its own tray, checks Busy/Ready IPC, restart-to-Inactive,
offline hook fail-open, and no per-event runtime files. It does not prove real Codex hooks.

Opt-in USB hardware tests write to the selected physical screen. Close vendor apps,
set tray USB output to Off, and use your actual COM port:

```powershell
$previousUsbPort = $env:CODEXTRAY_TEST_USB_PORT
$previousUsbOrientation = $env:CODEXTRAY_TEST_USB_ORIENTATION
try {
    $env:CODEXTRAY_TEST_USB_PORT = 'COM3'
    $env:CODEXTRAY_TEST_USB_ORIENTATION = 'Portrait'
    dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category=UsbHardware'
}
finally {
    $env:CODEXTRAY_TEST_USB_PORT = $previousUsbPort
    $env:CODEXTRAY_TEST_USB_ORIENTATION = $previousUsbOrientation
}
```

Previews go to `artifacts/usb-screen-previews`. Serial writes do not prove the
pixels displayed correctly: inspect the screen yourself, then restore Automatic/your port.

Before accepting a release, manually test setup, hook Trust, session start → Ready,
prompt → Busy, normal completion → Ready with one notification, Ctrl+C → Ready,
actual session end → Inactive, Windows startup, reinstall, uninstall preservation,
USB orientation/reconnect and downloaded checksums.

<a id="reinstall"></a>

## Reinstall or upgrade

Run the new verified installer into the same location. Setup stops the previous tray
before replacement. Hook merge removes/replaces only handlers carrying
`codex-tray-indicator-v1`, preserving foreign hooks; it does not accumulate duplicate
owned handlers. Review pending hooks again with `/hooks`, select Trust, and open a new session.

If the WSL hook file is malformed or unavailable, setup reports failure.
Do not delete the entire file to fix it: preserve your hooks and repair the reported problem.

<a id="uninstall"></a>

## Uninstall

For an installed copy: Windows **Settings → Apps → Installed apps →
Codex Tray Indicator → Uninstall**.

For a portable copy, while its original path and WSL selection are still valid:

```powershell
& $codexTray --shutdown
& $codexTray --uninstall
```

Use the path variable from installation. Only delete/move the portable file after
successful integration removal. Uninstall removes owned handlers, per-user startup,
and app preferences. Foreign hooks and the original
`~/.codex/hooks.json.codextray.bak` (if created) remain.
Keep the selected WSL distribution accessible so cleanup can succeed.
An unavailable distribution or malformed configuration causes a reported failure,
not permission to erase foreign hooks.

<a id="troubleshooting"></a>

## Troubleshooting

Run transport diagnostics from Windows PowerShell with the installed executable:

```powershell
$codexTray = "$env:LOCALAPPDATA/Programs/CodexTray/CodexTray.exe"
& $codexTray --query-state
& $codexTray --hook-test busy
& $codexTray --hook-test ready
```

Query prints Inactive/Ready/Busy/Error. Synthetic commands require a running tray and
test IPC only; they neither execute a Codex prompt nor create completion notifications.

| Symptom | Check |
|---|---|
| No icon | Check hidden tray icons; launch the installed EXE; only one instance per Windows user is allowed |
| No Codex found | Run the WSL probe in Supported scope; ensure login-shell PATH and Windows interop work |
| Multiple distributions error | Save WslDistribution before installation as described above |
| Always Inactive/Ready | Verify tray running, selected distribution, hook path and /hooks Trust; start a new Codex session |
| Busy persists after closing terminal | Await actual SessionEnd; state is not inferred from terminal/process disappearance |
| Error | Run query/synthetic tests; repair config/permissions; a valid subsequent event can clear the protocol error |
| No notification | Check Notifications, Windows notification settings, and whether another session is still Busy |
| USB unavailable/blank | Confirm Revision A, actual port, orientation, vendor app closed; select Reconnect screen |
| Build SDK/compiler not found | Run bootstrap, verify exact versions, then locked restore; never remove lock files just to force success |

For non-security bugs, use the repository's Issues page with Windows/app/WSL/CLI versions,
reproduction steps and sanitized diagnostics. Do not attach prompts, transcripts or secrets.
See [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md).

<a id="privacy"></a>

## Architecture and privacy

Hooks execute the same Windows EXE with `--hook`. Input is bounded to 65,536 bytes;
only lifecycle event name, session ID, turn ID, SessionStart source and an app-generated
timestamp are forwarded to `CodexTray.Status.v1` with `PipeOptions.CurrentUserOnly`.
Prompts, responses, transcript paths, working directories and model metadata are discarded.

Hooks fail open: invalid input, an offline tray or IPC timeout returns exit code 0 so
the indicator does not block Codex. The pipe connect budget is 250 ms and I/O is bounded.
The indicator makes no network requests and writes no per-event state files, logs or registry values.
The .NET single-file runtime may extract native libraries at startup; this is not a status log.

Installation/configuration persist `~/.codex/hooks.json`, a one-time
`hooks.json.codextray.bak` when an existing file is present, preferences under
`HKCU/Software/CodexTray`, and the per-user startup entry. USB discovery reads Windows
device information; it does not transfer Codex state through the registry.
Do not assume the pipe protects against malicious software running as the same Windows user.

<a id="security"></a>

## Security, contribution and license

Report vulnerabilities privately according to [SECURITY.md](SECURITY.md), not in public Issues.
The indicator is not a sandbox, a Codex authorization mechanism or a code-signing service.
Review hooks before Trust, preserve foreign configuration, and never submit credentials
or certificate material as diagnostics.

Contributions follow [CONTRIBUTING.md](CONTRIBUTING.md) and
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Copyright © 2026 Vasyl Danyliuk.
The source is provided under the [MIT license](LICENSE), without warranty.
