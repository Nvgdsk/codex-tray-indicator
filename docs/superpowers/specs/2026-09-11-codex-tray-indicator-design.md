# Codex Tray Indicator Design

Date: 2026-09-11

## Purpose

Codex Tray Indicator is a small per-user Windows 11 application that shows the
state of Codex CLI sessions running inside WSL2. It must react to lifecycle
events without polling files, terminal pixels, logs, or the registry. Runtime
state is carried only through a Windows named pipe and kept in process memory.

The primary deliverable is `dist/CodexTraySetup.exe`. The installed application
is a self-contained, single-file `CodexTray.exe` that requires no separately
installed .NET runtime.

## Required Test Environment and Codex Contract

Development and end-to-end validation require:

- A supported Windows x64 environment with WSL2.
- At least one WSL distribution with Codex available on `PATH`.
- A Codex CLI version supporting the lifecycle hooks below, with `hooks` enabled.
- Coverage for both missing and existing user-level `~/.codex/hooks.json` files.

The Codex 0.154.0 reference schema defines the lifecycle events used by this application:
`SessionStart`, `UserPromptSubmit`, `Stop`, `Interrupt`, and `SessionEnd`.
Command hooks receive one JSON object on standard input. Turn-scoped events
include `turn_id`; all relevant events include `session_id`.

Non-managed user hooks require explicit review and trust through `/hooks` in the
Codex CLI. The installer must not write internal trust state or use
`--dangerously-bypass-hook-trust`. It installs the hooks and explains the one
remaining review step clearly.

Primary references:

- https://learn.chatgpt.com/docs/hooks
- https://learn.chatgpt.com/docs/config-file/config-advanced
- https://github.com/openai/codex/blob/rust-v0.154.0/codex-rs/hooks/src/schema.rs

## Chosen Architecture

The product uses one Windows executable in two runtime modes plus maintenance
commands:

1. `CodexTray.exe` starts the WinForms tray application and named-pipe server.
2. `CodexTray.exe --hook` is a short-lived hook client. It reads the Codex JSON
   payload from stdin, maps it to an IPC event, attempts one short pipe
   connection, sends the event, and exits.
3. `CodexTray.exe --hook-test <state>` sends a synthetic state for manual IPC
   verification.
4. `CodexTray.exe --query-state` queries the live server for automated and
   manual diagnostics.
5. `CodexTray.exe --install` and `--uninstall` perform per-user integration
   maintenance for the installer.

The alternative designs were rejected:

- A persistent Linux helper adds another installed runtime component and does
  not satisfy the preferred one-executable design.
- Wrapping or modifying the `codex` command is intrusive and can miss alternate
  launch paths.
- Log, transcript, filesystem, terminal-pixel, OCR, and UI Automation polling
  violate the latency or storage requirements and are unnecessary while hooks
  are available.

## Components

### Command dispatcher

`Program` selects a mode before initializing WinForms. Maintenance and hook
modes never create a tray icon. Ordinary startup uses Windows GUI subsystem
semantics so no console window appears.

### IPC protocol

The server listens asynchronously on `CodexTray.Status.v1` using
`NamedPipeServerStream`. The client connects through `NamedPipeClientStream`
with a 250 ms connection deadline. The protocol is one UTF-8 JSON object per
connection, bounded to 64 KiB, followed by an optional small JSON response.

The request envelope has these fields:

```json
{
  "protocolVersion": 1,
  "kind": "event",
  "event": "UserPromptSubmit",
  "sessionId": "session-id",
  "turnId": "turn-id",
  "source": null,
  "timestampUtc": "2026-09-11T12:00:00.0000000Z"
}
```

`kind` can be `event`, `set-state`, `query-state`, or `shutdown`. Synthetic
`set-state` messages are accepted only for explicit test commands. The server
uses `PipeOptions.CurrentUserOnly` and limits each connection to one request.

The pipe loop creates a fresh server instance for each accepted client and
immediately resumes waiting after dispatch. A client cannot hold the server
indefinitely: reads and writes have short cancellation deadlines. Malformed or
oversized messages put the tray into Error, but a client-side inability to find
the tray is fail-open and does not create an error elsewhere.

### In-memory session state

`SessionStateStore` owns all runtime status. It contains no filesystem,
registry, database, or logging dependency. Each session record tracks whether
the session is active, its current busy turn, and recently completed turn IDs.

Event rules:

| Codex event | State effect |
| --- | --- |
| `SessionStart` with `startup`, `resume`, or `clear` | Add the session as Ready unless it is already Busy |
| `SessionStart` with `compact` | Preserve the current state because compaction can happen during a turn |
| `UserPromptSubmit` | Mark that session and `turn_id` Busy |
| `Stop` | Complete the matching turn and mark the session Ready |
| `Interrupt` | Complete the matching turn and mark the session Ready |
| `SessionEnd` | Remove the session |

The aggregate tray state is Busy if any session is busy, Ready if at least one
session is active and none is busy, and Inactive if there are no sessions.
Error is reserved for a known integration failure and remains until a valid
event or successful reconnect/test resets it.

To tolerate async hook reordering, a terminal event received before its matching
`UserPromptSubmit` records the turn as completed. A later prompt event for that
same turn is ignored. A `Stop` or `Interrupt` for an older turn cannot clear a
newer busy turn. Completed-turn memory is bounded per session.

### Hook payload parser

The hook client reads stdin once and extracts only:

- `hook_event_name`
- `session_id`
- `turn_id`, when present
- `source`, when present

Prompt text, assistant output, transcript paths, working directories, and model
names are neither forwarded nor persisted. Unknown events and malformed stdin
cause a quick, silent exit with code zero so the integration cannot disrupt
Codex. No runtime log is written in Release builds.

### Tray UI

`TrayApplicationContext` owns a single `NotifyIcon`, generated icons, context
menu, cancellation source, and IPC server. It marshals state changes onto the
WinForms UI thread.

States and tooltips:

- Green: `Codex: Ready`
- Yellow: `Codex: Thinking...`
- Red: `Codex: Error`
- Gray: `Codex: Not running`

Icons are circles generated in memory with `System.Drawing`; no icon or status
files are created at runtime. The context menu contains the product title,
read-only status, Start with Windows, Notifications, Reconnect / Test, and Exit.

Notifications default to enabled. A real Busy-to-Ready transition shows
`Codex finished`. Initial session readiness never notifies. Repeated Ready events
and synthetic tests do not notify. The preference is stored as ordinary
per-user configuration only when the user changes it; it is never polled.

### Single instance and shutdown

The tray mode holds a per-user named mutex. A second ordinary launch exits
without starting another server. Maintenance commands do not take the tray
mutex. Exit cancels the pipe loop and disposes the server, icon, generated icon
resources, menu, cancellation source, and mutex.

The uninstaller sends a bounded `shutdown` IPC command before removing files.
If the tray is already absent, uninstall continues.

## Installation and WSL Integration

The setup is a per-user Inno Setup installer with no administrator requirement.
It installs under `{localappdata}\Programs\CodexTray`, registers standard
uninstall metadata, and invokes `CodexTray.exe --install`.

Installation performs these steps:

1. Run `wsl.exe --list --quiet` and normalize UTF-16 output.
2. For each non-infrastructure distribution, run `command -v codex` and
   `codex --version` through argument-safe `ProcessStartInfo` calls.
3. Select the sole distribution containing Codex. If more than one is found,
   stop with a clear diagnostic instead of changing an arbitrary distribution.
4. Convert the installed Windows executable path using `wslpath -a -u` inside
   the selected distribution.
5. Read `~/.codex/hooks.json` through WSL. Treat a missing file as an empty
   hooks object; reject invalid existing JSON without overwriting it.
6. Merge one handler into each required lifecycle event while preserving all
   foreign properties, matcher groups, and handlers.
7. Identify owned handlers by the command argument
   `--integration-id codex-tray-indicator-v1`; remove prior owned handlers
   before adding the current path so reinstall and upgrade are idempotent.
8. Write UTF-8 JSON to stdin of a one-shot WSL shell command that creates a
   sibling temporary file with restrictive permissions and atomically renames
   it to `~/.codex/hooks.json`. If an original file exists and the dedicated
   backup does not, preserve it as `hooks.json.codextray.bak`.
9. Record the selected distribution and notification preference under the
   application's HKCU settings key, add the quoted executable path to
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and start the tray.
10. Explain that `/hooks` must be opened once in Codex to review and trust the
    newly installed user hooks.

Each installed hook is a command handler similar to:

```json
{
  "type": "command",
  "command": "\"/resolved/path/CodexTray.exe\" --hook --integration-id codex-tray-indicator-v1",
  "timeout": 1,
  "async": true
}
```

`SessionEnd` remains synchronous by Codex design, but the client still uses the
same 250 ms fail-open connection deadline.

Uninstall repeats detection for the recorded distribution, removes only owned
handlers, prunes only matcher groups or event arrays made empty by that removal,
atomically writes the remaining configuration, removes the startup value, and
stops the tray. It never restores a whole backup over newer user changes.

## Error Handling

- Missing tray server: hook exits zero after the connection deadline.
- Invalid hook stdin or unsupported hook event: hook exits zero without IPC.
- Invalid existing hooks JSON during install/uninstall: abort configuration
  mutation, keep the file untouched, and display an actionable error.
- WSL unavailable or no Codex distribution found: leave existing integration
  unchanged and report the cause.
- Pipe creation failure or internal tray exception: show Error when the tray can
  continue; otherwise show a user-visible fatal dialog and exit.
- Invalid or oversized server message: reject the connection and set Error.
- Duplicate tray launch: exit quietly.

All subprocess calls have cancellation and time limits. Distribution names and
paths are passed as arguments instead of interpolated into a Windows shell.
The fixed Linux atomic-write command contains no user-controlled shell text.

## Project Layout

```text
CodexTray/
├── src/
│   └── CodexTray/
│       ├── CodexTray.csproj
│       ├── Program.cs
│       ├── AppConstants.cs
│       ├── Ipc/
│       ├── State/
│       ├── Hooks/
│       ├── Installation/
│       └── UI/
├── tests/
│   └── CodexTray.Tests/
├── installer/
│   └── CodexTray.iss
├── scripts/
├── docs/superpowers/
├── dist/
├── CodexTray.sln
└── README.md
```

Production classes are kept independent of WinForms where possible so parsing,
state transitions, hook merging, process-output parsing, and IPC can be tested
without a visible desktop.

## Build Strategy

The original build design requires a .NET 8 LTS SDK for build only.
The release targets `net8.0-windows` and `win-x64`, enables WinForms, uses the
Windows GUI subsystem, and publishes self-contained single-file output without
trimming. Inno Setup embeds that executable into `CodexTraySetup.exe`.

Self-contained single-file startup may extract bundled framework payload into
the normal .NET bundle cache. This is executable payload caching, not Codex
status transport or mutable runtime state; no per-event disk write occurs.

## Test Strategy

Implementation follows test-first development for production behavior.

Automated unit tests cover:

- mapping each supported hook payload;
- invalid, oversized, and unknown payloads;
- all state transitions, including compaction and out-of-order events;
- aggregate multi-session state;
- notification eligibility;
- hooks JSON merge, deduplication, path changes, and uninstall preservation;
- WSL list parsing, including UTF-16/NUL output and paths with spaces;
- command quoting and owned-handler identification.

Automated integration tests cover:

- named-pipe event delivery and query response;
- 100 Busy/Ready cycles without state files;
- absent server returning within the hook deadline;
- server shutdown and restart;
- single-instance behavior;
- install twice without duplicate handlers;
- uninstall preserving unrelated hooks.

Host/WSL validation covers:

1. Publish `CodexTray.exe` and start it on Windows.
2. Invoke the Windows executable from Ubuntu through its `wslpath` path with
   `--hook-test busy`, query Yellow, then send Ready and query Green.
3. Stop the tray and verify an Ubuntu hook invocation exits quickly.
4. Install into a temporary isolated `CODEX_HOME` fixture and verify merge,
   reinstall, and uninstall behavior before touching the real user config.
5. Build and run the installer, verify the installed executable, startup entry,
   tray process, and configured hooks, then exercise uninstall and reinstall.
6. After the user-level hook trust step, run real interactive Codex checks for
   session start, prompt submission, normal completion, a second prompt,
   Ctrl+C interruption, and normal exit.

Interactive steps that require the user's Codex account or visible tray are
reported separately from automated assertions. No test is claimed successful
unless its actual output or observable state was checked.

## Acceptance Criteria

The implementation is complete only when:

- `dist/CodexTraySetup.exe` and `dist/CodexTray.exe` exist and their hashes are
  recorded;
- unit and integration test suites pass from a clean build;
- real Ubuntu-to-Windows named-pipe delivery is demonstrated;
- the hook client is fail-open when the tray is absent;
- no status file, log polling, registry polling, OCR, terminal-pixel polling,
  TCP listener, or recurring shell process exists;
- reinstall is idempotent and uninstall preserves unrelated hooks;
- the release is self-contained and starts without a console window;
- Windows startup, tray state, notification settings, and clean shutdown work;
- actual Codex hooks are installed for version 0.154.0, and the required
  official `/hooks` trust step is clearly surfaced and verified only after the
  user confirms it;
- any end-to-end step that cannot be automated is explicitly identified rather
  than simulated or inferred.
