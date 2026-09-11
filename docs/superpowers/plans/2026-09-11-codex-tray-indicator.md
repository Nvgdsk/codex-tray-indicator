# Codex Tray Indicator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and verify a self-contained Windows tray application and per-user installer that displays Codex CLI lifecycle state received from WSL2 through a Windows named pipe.

**Architecture:** One WinForms executable runs either as the long-lived tray/named-pipe server or as a short-lived hook and maintenance client. Hook JSON is reduced to session/turn lifecycle metadata, sent over a current-user-only named pipe, and applied to an in-memory, reorder-tolerant state machine; install-time WSL commands merge owned handlers atomically without using files for runtime state.

**Tech Stack:** C# 12, .NET 8 WinForms, `System.IO.Pipes`, `System.Text.Json.Nodes`, xUnit, Windows Registry, WSL2 interoperability, Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-09-11-codex-tray-indicator-design.md`

## Global Constraints

- Target Windows 11 x64 and WSL2; validate against a configured WSL distribution with a Codex CLI version supporting lifecycle hooks.
- Runtime state must stay in RAM and cross only `\\.\pipe\CodexTray.Status.v1`; never poll or write a status file, log, transcript, registry value, TCP port, terminal pixel, or OCR result.
- Publish `CodexTray.exe` as `win-x64`, self-contained, single-file, Windows GUI subsystem, with no separately installed runtime.
- Hook connection timeout is 250 ms, message size is at most 65,536 UTF-8 bytes, and missing tray server must exit successfully.
- Preserve unrelated hooks, make install idempotent, make uninstall remove only handlers containing `--integration-id codex-tray-indicator-v1`, and write `hooks.json` atomically.
- Do not persist prompt text, assistant output, transcript paths, working directories, model names, session state, or runtime logs.
- Do not set hook trust state and do not persist any dangerous bypass flag; user review remains through the official Codex `/hooks` flow.
- Production behavior is implemented test-first and every completion claim requires a fresh full test/build verification.

## File Map

```text
CodexTray.sln                                      solution entry point
.gitignore                                         build/tool output exclusions
src/CodexTray/CodexTray.csproj                     WinForms executable and publish settings
src/CodexTray/Program.cs                           mode dispatch and process exit policy
src/CodexTray/ApplicationHost.cs                   testable composition of application modes
src/CodexTray/AppServices.cs                       injectable application dependencies
src/CodexTray/AppConstants.cs                      protocol, pipe, registry, and timeout constants
src/CodexTray/CommandLine.cs                       argument parsing into AppCommand
src/CodexTray/Domain/CodexHookEvent.cs             sanitized lifecycle event model
src/CodexTray/Domain/TrayState.cs                  aggregate visible state
src/CodexTray/Domain/StateTransition.cs            state change and notification decision
src/CodexTray/Domain/SessionStateStore.cs           reorder-tolerant in-memory session state
src/CodexTray/Hooks/HookPayloadParser.cs           stdin JSON parser and field minimizer
src/CodexTray/Ipc/IpcMessage.cs                    versioned pipe request envelope
src/CodexTray/Ipc/IpcResponse.cs                   bounded response envelope
src/CodexTray/Ipc/IpcJson.cs                       stable wire JSON naming and enum converters
src/CodexTray/Ipc/IpcClient.cs                     timed one-shot pipe client
src/CodexTray/Ipc/IpcServer.cs                     cancellable concurrent pipe accept loop
src/CodexTray/Installation/IProcessRunner.cs        subprocess abstraction
src/CodexTray/Installation/ProcessRunner.cs         bounded redirected process execution
src/CodexTray/Installation/WslDetector.cs           distro parsing, Codex detection, wslpath conversion
src/CodexTray/Installation/HookConfigMerger.cs      lossless owned-handler merge and removal
src/CodexTray/Installation/WslHookConfigStore.cs    WSL read, backup, and atomic replacement
src/CodexTray/Installation/IUserSettings.cs         per-user settings contract
src/CodexTray/Installation/RegistryUserSettings.cs  HKCU preferences, distro, and startup entry
src/CodexTray/Installation/IntegrationInstaller.cs  install/uninstall orchestration
src/CodexTray/UI/TrayIconFactory.cs                 in-memory colored circle icons
src/CodexTray/UI/NotificationPolicy.cs              Busy-to-Ready notification rule
src/CodexTray/UI/TrayApplicationContext.cs          NotifyIcon/menu/server ownership and shutdown
tests/CodexTray.Tests/CodexTray.Tests.csproj        unit and Windows integration tests
tests/CodexTray.Tests/TestDoubles/ManualTimeProvider.cs deterministic event timestamps
tests/CodexTray.Tests/CommandLineTests.cs           mode parsing tests
tests/CodexTray.Tests/HookPayloadParserTests.cs     lifecycle JSON contract tests
tests/CodexTray.Tests/SessionStateStoreTests.cs     transition and race tests
tests/CodexTray.Tests/IpcIntegrationTests.cs        real named-pipe transport tests
tests/CodexTray.Tests/WslDetectorTests.cs           list/version/path parsing tests
tests/CodexTray.Tests/WslDetectorIntegrationTests.cs read-only real WSL detection test
tests/CodexTray.Tests/HookConfigMergerTests.cs      merge, reinstall, and uninstall tests
tests/CodexTray.Tests/IntegrationInstallerTests.cs  orchestration tests with fake process/settings ports
tests/CodexTray.Tests/NotificationPolicyTests.cs    notification eligibility tests
installer/CodexTray.iss                             per-user Inno Setup definition
scripts/bootstrap-build-tools.ps1                   reproducible build-only SDK/Inno bootstrap
scripts/build-release.ps1                           clean test, publish, installer, and hash pipeline
scripts/test-wsl-ipc.ps1                            real Windows/Ubuntu IPC proof
README.md                                           install, trust, operation, testing, uninstall
dist/CodexTray.exe                                  published application artifact
dist/CodexTraySetup.exe                             primary installer artifact
dist/SHA256SUMS.txt                                 artifact hashes
```

---

### Task 1: Toolchain, solution, command model, and protocol constants

**Files:**
- Create: `.gitignore`
- Create: `scripts/bootstrap-build-tools.ps1`
- Create: `CodexTray.sln`
- Create: `src/CodexTray/CodexTray.csproj`
- Create: `src/CodexTray/AppConstants.cs`
- Create: `src/CodexTray/CommandLine.cs`
- Create: `tests/CodexTray.Tests/CodexTray.Tests.csproj`
- Create: `tests/CodexTray.Tests/TestDoubles/ManualTimeProvider.cs`
- Create: `tests/CodexTray.Tests/CommandLineTests.cs`

**Interfaces:**
- Produces: `enum AppMode { Tray, Hook, HookTest, QueryState, Install, Uninstall }`.
- Produces: `sealed record AppCommand(AppMode Mode, string? Value, string? IntegrationId)`.
- Produces: `CommandLine.Parse(IReadOnlyList<string> args) -> AppCommand`.
- Produces: constants `PipeName`, `IntegrationId`, `ProtocolVersion`, `MaxMessageBytes`, `PipeConnectTimeout`, `PipeIoTimeout`, `RegistrySubKey`, and `StartupValueName`.

- [ ] **Step 1: Bootstrap build-only dependencies**

Create `scripts/bootstrap-build-tools.ps1` with strict error handling. It must download the official `dotnet-install.ps1` only when `.tools/dotnet/dotnet.exe` is absent, install the latest .NET 8 SDK into `.tools/dotnet`, and use `winget install --id JRSoftware.InnoSetup --scope user --silent --accept-package-agreements --accept-source-agreements` only when `ISCC.exe` is not discoverable in the user or Program Files install paths.

```powershell
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $PSScriptRoot '..\.tools'
$dotnetRoot = Join-Path $toolRoot 'dotnet'
$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
$installerScript = Join-Path $toolRoot 'dotnet-install.ps1'
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerScript
    & $installerScript -Channel 8.0 -InstallDir $dotnetRoot -NoPath
}
```

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-build-tools.ps1`

Expected: `.tools/dotnet/dotnet.exe --version` reports an 8.0 SDK and `ISCC.exe` is found.

- [ ] **Step 2: Create the solution and test project before production types**

Use the local SDK to create a solution, class-library application project, and
xUnit project, then add project references. Set both projects to
`net8.0-windows`; set the application project to `UseWindowsForms=true`,
`OutputType=Library`, `ImplicitUsings=enable`, `Nullable=enable`, and
`LangVersion=12.0`. Remove the generated `Class1.cs`. Expose internal production
types only to the test assembly with an `InternalsVisibleTo` item in
`CodexTray.csproj`. Task 10 changes the fully tested composition to `WinExe`.

Run:

```powershell
.\.tools\dotnet\dotnet.exe new sln -n CodexTray
.\.tools\dotnet\dotnet.exe new classlib -n CodexTray -o src/CodexTray -f net8.0
.\.tools\dotnet\dotnet.exe new xunit -n CodexTray.Tests -o tests/CodexTray.Tests -f net8.0
.\.tools\dotnet\dotnet.exe sln add src/CodexTray/CodexTray.csproj tests/CodexTray.Tests/CodexTray.Tests.csproj
.\.tools\dotnet\dotnet.exe add tests/CodexTray.Tests/CodexTray.Tests.csproj reference src/CodexTray/CodexTray.csproj
```

- [ ] **Step 3: Write failing command-line tests**

Tests must assert: no arguments selects Tray; `--hook --integration-id codex-tray-indicator-v1` selects Hook and captures the marker; every hook-test state is captured; query/install/uninstall select their modes; unknown arguments throw `ArgumentException`.

```csharp
[Theory]
[InlineData("--hook", AppMode.Hook)]
[InlineData("--query-state", AppMode.QueryState)]
[InlineData("--install", AppMode.Install)]
[InlineData("--uninstall", AppMode.Uninstall)]
public void Parse_SelectsRequestedMode(string argument, AppMode expected)
{
    Assert.Equal(expected, CommandLine.Parse([argument]).Mode);
}
```

- [ ] **Step 4: Run the focused tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~CommandLineTests`

Expected: compilation fails because `CommandLine`, `AppMode`, and `AppCommand` do not exist.

- [ ] **Step 5: Implement exact parsing and constants**

Implement one mutually exclusive mode. `--hook-test` requires exactly one of `busy`, `ready`, `inactive`, or `error`; `--integration-id` is accepted only with Hook. Use these values:

```csharp
internal static class AppConstants
{
    public const string PipeName = "CodexTray.Status.v1";
    public const string IntegrationId = "codex-tray-indicator-v1";
    public const int ProtocolVersion = 1;
    public const int MaxMessageBytes = 65_536;
    public static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan PipeIoTimeout = TimeSpan.FromSeconds(1);
    public const string RegistrySubKey = @"Software\CodexTray";
    public const string StartupValueName = "CodexTray";
}
```

Implement `ManualTimeProvider` as a test-only sealed `TimeProvider` whose
constructor stores a `DateTimeOffset`, `GetUtcNow()` returns it, and `Advance`
adds a supplied `TimeSpan`. This avoids adding a test-time package dependency.

- [ ] **Step 6: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~CommandLineTests`

Expected: all command-line tests pass with no warnings.

```powershell
git add .gitignore scripts/bootstrap-build-tools.ps1 CodexTray.sln src/CodexTray tests/CodexTray.Tests
git commit -m "feat: scaffold Codex Tray command modes"
```

---

### Task 2: Hook payload minimization

**Files:**
- Create: `src/CodexTray/Domain/CodexHookEvent.cs`
- Create: `src/CodexTray/Hooks/HookPayloadParser.cs`
- Create: `tests/CodexTray.Tests/HookPayloadParserTests.cs`

**Interfaces:**
- Produces: `enum CodexHookEventName { SessionStart, UserPromptSubmit, Stop, Interrupt, SessionEnd }`.
- Produces: `sealed record CodexHookEvent(CodexHookEventName Name, string SessionId, string? TurnId, string? Source, DateTimeOffset TimestampUtc)`.
- Produces: `HookPayloadParser.TryParse(ReadOnlySpan<byte> utf8, TimeProvider clock, out CodexHookEvent? value) -> bool`.

- [ ] **Step 1: Write failing parser tests**

Use real UTF-8 JSON. Cover all five event names, missing session ID, missing required turn ID, unknown event, malformed JSON, oversized input, and a payload containing `prompt`, `last_assistant_message`, `transcript_path`, `cwd`, and `model`. Assert the returned record contains none of those sensitive fields.

```csharp
[Fact]
public void TryParse_UserPromptSubmit_ExtractsOnlyLifecycleIdentity()
{
    byte[] json = """{"hook_event_name":"UserPromptSubmit","session_id":"s1","turn_id":"t1","prompt":"secret","cwd":"/secret"}"""u8.ToArray();
    var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
    Assert.True(HookPayloadParser.TryParse(json, clock, out CodexHookEvent? value));
    Assert.Equal(new CodexHookEvent(CodexHookEventName.UserPromptSubmit, "s1", "t1", null, clock.GetUtcNow()), value);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~HookPayloadParserTests`

Expected: compilation fails because the domain record and parser are absent.

- [ ] **Step 3: Implement bounded parsing**

Reject input longer than `AppConstants.MaxMessageBytes`. Use `JsonDocument.Parse`, require a non-empty string `session_id`, require `turn_id` for UserPromptSubmit/Stop/Interrupt, accept `source` only for SessionStart, map exact event names, catch `JsonException`, and never retain the document or source byte array.

```csharp
return name switch
{
    "SessionStart" => Build(CodexHookEventName.SessionStart, turnRequired: false),
    "UserPromptSubmit" => Build(CodexHookEventName.UserPromptSubmit, turnRequired: true),
    "Stop" => Build(CodexHookEventName.Stop, turnRequired: true),
    "Interrupt" => Build(CodexHookEventName.Interrupt, turnRequired: true),
    "SessionEnd" => Build(CodexHookEventName.SessionEnd, turnRequired: false),
    _ => false
};
```

- [ ] **Step 4: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~HookPayloadParserTests`

Expected: all parser tests pass.

```powershell
git add src/CodexTray/Domain src/CodexTray/Hooks tests/CodexTray.Tests/HookPayloadParserTests.cs
git commit -m "feat: parse sanitized Codex lifecycle events"
```

---

### Task 3: Reorder-tolerant in-memory session state

**Files:**
- Create: `src/CodexTray/Domain/TrayState.cs`
- Create: `src/CodexTray/Domain/StateTransition.cs`
- Create: `src/CodexTray/Domain/SessionStateStore.cs`
- Create: `tests/CodexTray.Tests/SessionStateStoreTests.cs`

**Interfaces:**
- Produces: `enum TrayState { Inactive, Ready, Busy, Error }`.
- Produces: `sealed record StateTransition(TrayState Previous, TrayState Current, bool IsSynthetic)` with `Changed => Previous != Current`.
- Produces: thread-safe `SessionStateStore.Current`, `Apply(CodexHookEvent)`, `SetSynthetic(TrayState)`, and `SetError()`.

- [ ] **Step 1: Write failing transition tests**

Cover the specified lifecycle table, SessionStart compact while Busy, initial prompt without SessionStart, multiple sessions, repeated events, terminal-before-prompt ordering, old Stop after a newer prompt, SessionEnd removal, Error recovery after a valid event, and bounded completed-turn retention of 32 IDs per session.

```csharp
[Fact]
public void StopForOlderTurn_DoesNotClearNewBusyTurn()
{
    var store = new SessionStateStore();
    store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t1"));
    store.Apply(Event(CodexHookEventName.UserPromptSubmit, "s", "t2"));
    StateTransition result = store.Apply(Event(CodexHookEventName.Stop, "s", "t1"));
    Assert.Equal(TrayState.Busy, result.Current);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~SessionStateStoreTests`

Expected: compilation fails because the state types are absent.

- [ ] **Step 3: Implement the state machine**

Guard a dictionary with one private lock. Each session stores `CurrentTurnId` and a queue plus hash set of at most 32 completed IDs. Apply this exact order:

```text
SessionEnd: remove session.
SessionStart compact: preserve aggregate state.
SessionStart other: create session; do not clear an existing current turn.
Stop/Interrupt: add turn to completed set; clear current turn only when IDs match.
UserPromptSubmit: create session; ignore completed turn; otherwise set current turn.
Valid event: clear the explicit Error flag.
Aggregate: Error > any current turn Busy > any session Ready > Inactive.
```

- [ ] **Step 4: Verify GREEN, run parser regressions, and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter "FullyQualifiedName~SessionStateStoreTests|FullyQualifiedName~HookPayloadParserTests"`

Expected: all selected tests pass.

```powershell
git add src/CodexTray/Domain tests/CodexTray.Tests/SessionStateStoreTests.cs
git commit -m "feat: track Codex session state in memory"
```

---

### Task 4: Versioned named-pipe transport

**Files:**
- Create: `src/CodexTray/Ipc/IpcMessage.cs`
- Create: `src/CodexTray/Ipc/IpcResponse.cs`
- Create: `src/CodexTray/Ipc/IpcJson.cs`
- Create: `src/CodexTray/Ipc/IpcClient.cs`
- Create: `src/CodexTray/Ipc/IpcServer.cs`
- Create: `tests/CodexTray.Tests/IpcIntegrationTests.cs`

**Interfaces:**
- Produces: `enum IpcMessageKind { Event, SetState, QueryState, Shutdown }`.
- Produces: flat `sealed record IpcMessage(int ProtocolVersion, IpcMessageKind Kind, CodexHookEventName? Event, string? SessionId, string? TurnId, string? Source, DateTimeOffset? TimestampUtc, TrayState? State)` with factories `FromEvent`, `ForState`, `Query`, and `Shutdown` plus `ToCodexHookEvent()` validation.
- Produces: `sealed record IpcResponse(bool Ok, TrayState? State, string? Error)`.
- Produces: `IpcJson.Options` with camel-case property names, camel-case `IpcMessageKind`, exact-name `CodexHookEventName`, and exact-name `TrayState` converters.
- Produces: `IpcClient.SendAsync(IpcMessage message, bool expectResponse, CancellationToken cancellationToken) -> Task<IpcResponse?>`.
- Produces: `IpcServer.RunAsync(Func<IpcMessage, CancellationToken, Task<IpcResponse>> handler, CancellationToken cancellationToken) -> Task`.

- [ ] **Step 1: Write failing real-pipe tests**

Use a unique pipe name constructor overload in tests. Cover the exact flat wire
shape from the specification, event round-trip, query response, malformed JSON
setting handler error, unsupported protocol version, missing event fields,
oversized input rejection, absent server completion under 750 ms, cancellation,
20 concurrent clients, and server restart on the same pipe name.

```csharp
[Fact]
public async Task SendAsync_WhenServerAbsent_ReturnsWithinFailOpenBudget()
{
    var client = new IpcClient($"missing-{Guid.NewGuid():N}", TimeSpan.FromMilliseconds(250));
    var stopwatch = Stopwatch.StartNew();
    IpcResponse? response = await client.SendAsync(Query(), true, CancellationToken.None);
    Assert.Null(response);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750));
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~IpcIntegrationTests`

Expected: compilation fails because IPC types are absent.

- [ ] **Step 3: Implement the client**

Create `NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)`. Link caller cancellation with the connection and I/O timeouts. Serialize one compact JSON line with `IpcJson.Options`. Return null on timeout, unavailable server, broken pipe, or cancellation initiated by the internal deadline; propagate caller cancellation.

Configure the wire format explicitly:

```csharp
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
options.Converters.Add(new JsonStringEnumConverter<IpcMessageKind>(JsonNamingPolicy.CamelCase));
options.Converters.Add(new JsonStringEnumConverter<CodexHookEventName>());
options.Converters.Add(new JsonStringEnumConverter<TrayState>());
```

- [ ] **Step 4: Implement the concurrent server**

For each loop iteration create a new `NamedPipeServerStream` with asynchronous and current-user-only options, await connection, move the connected instance into a tracked handler task, and immediately create the next accepting instance. Read through a bounded byte accumulator until newline or EOF. Reject more than 65,536 bytes before deserialization. Await all tracked handlers on shutdown and dispose every stream.

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    var pipe = CreateServer();
    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
    Task worker = HandleConnectionAsync(pipe, handler, cancellationToken);
    TrackUntilComplete(worker);
}
```

- [ ] **Step 5: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~IpcIntegrationTests`

Expected: all pipe tests pass and the absent-server timing assertion passes.

```powershell
git add src/CodexTray/Ipc tests/CodexTray.Tests/IpcIntegrationTests.cs
git commit -m "feat: add bounded named pipe transport"
```

---

### Task 5: Hook, test, query, and shutdown command execution

**Files:**
- Create: `src/CodexTray/HookCommandRunner.cs`
- Create: `tests/CodexTray.Tests/HookCommandRunnerTests.cs`

**Interfaces:**
- Produces: `HookCommandRunner.RunAsync(AppCommand command, Stream stdin, TextWriter stdout, CancellationToken cancellationToken) -> Task<int>`.
- Consumes: `HookPayloadParser`, `IpcClient`, `IpcMessage`, and `AppConstants.IntegrationId`.

- [ ] **Step 1: Write failing mode tests**

Use a loopback `IIpcClient` test port introduced beside `IpcClient`. Assert Hook rejects a wrong integration ID without sending, valid stdin sends Event, malformed/unknown/oversized stdin returns zero without sending, HookTest maps four values, QueryState prints exactly one state name, and an unavailable client returns zero for Hook and nonzero for QueryState.

```csharp
[Fact]
public async Task Hook_WithMalformedInput_FailsOpenWithoutSending()
{
    var fake = new RecordingIpcClient();
    var runner = new HookCommandRunner(fake, TimeProvider.System);
    int exitCode = await runner.RunAsync(HookCommand(), Utf8("{"), TextWriter.Null, CancellationToken.None);
    Assert.Equal(0, exitCode);
    Assert.Empty(fake.Messages);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~HookCommandRunnerTests`

Expected: compilation fails because `HookCommandRunner` and `IIpcClient` are absent.

- [ ] **Step 3: Implement command execution**

Read stdin with a bounded memory buffer, create flat Event messages with
`IpcMessage.FromEvent` only after parsing, mark HookTest messages as SetState,
request a response for QueryState and Shutdown, and never print in Hook mode.
`HookCommandRunner` catches parser, stdin, and IPC exceptions in Hook mode and
returns zero. HookTest and QueryState return code two when the server is
unavailable so diagnostics remain observable.

- [ ] **Step 4: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter "FullyQualifiedName~HookCommandRunnerTests|FullyQualifiedName~CommandLineTests"`

Expected: all selected tests pass.

```powershell
git add src/CodexTray/HookCommandRunner.cs src/CodexTray/Ipc tests/CodexTray.Tests/HookCommandRunnerTests.cs
git commit -m "feat: execute fail-open hook client modes"
```

---

### Task 6: WSL detection and bounded process execution

**Files:**
- Create: `src/CodexTray/Installation/IProcessRunner.cs`
- Create: `src/CodexTray/Installation/ProcessRunner.cs`
- Create: `src/CodexTray/Installation/WslDetector.cs`
- Create: `tests/CodexTray.Tests/WslDetectorTests.cs`
- Create: `tests/CodexTray.Tests/WslDetectorIntegrationTests.cs`

**Interfaces:**
- Produces: `sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, string? StandardInput, TimeSpan Timeout)`.
- Produces: `sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)`.
- Produces: `IProcessRunner.RunAsync(ProcessRequest, CancellationToken) -> Task<ProcessResult>`.
- Produces: `sealed record WslCodexInstallation(string Distribution, string CodexPath, string Version)`.
- Produces: `IWslDetector`, implemented by `WslDetector`, for installer orchestration tests.
- Produces: `WslDetector.FindCodexInstallationsAsync(CancellationToken)`, `ConvertWindowsPathAsync(string distro, string path, CancellationToken)`, and internal `ParseDistributionList(string)`.

- [ ] **Step 1: Write failing WSL parsing and request-construction tests**

Cover plain output, output containing NUL characters from UTF-16 redirection, blank lines, exclusion of `docker-desktop`, one/multiple/no Codex results, Unicode distribution names, and a Windows path containing spaces and Cyrillic characters. Assert distro and path are separate `ArgumentList` values.

```csharp
[Fact]
public void ParseDistributionList_RemovesNulsAndInfrastructureDistros()
{
    string raw = "U\0b\0u\0n\0t\0u\0\r\0\n\0d\0o\0c\0k\0e\0r\0-\0d\0e\0s\0k\0t\0o\0p\0";
    Assert.Equal(["Ubuntu"], WslDetector.ParseDistributionList(raw));
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~WslDetectorTests`

Expected: compilation fails because WSL components are absent.

- [ ] **Step 3: Implement ProcessRunner and WslDetector**

Use `ProcessStartInfo.ArgumentList`, redirect all standard streams, disable shell execute and window creation, write optional stdin as UTF-8, close stdin, kill the process tree on timeout, and await stdout/stderr without deadlock. Use fixed Linux commands:

```text
wsl.exe --list --quiet
wsl.exe -d <distro> -- sh -lc "command -v codex 2>/dev/null && codex --version"
wsl.exe -d <distro> -- wslpath -a -u -- <windows-path>
```

- [ ] **Step 4: Verify GREEN and run a read-only real detector test**

Write `WslDetectorIntegrationTests` using a real `ProcessRunner`. It calls
`FindCodexInstallationsAsync`, requires at least one installation, and checks
that each has a nonblank distribution, absolute Linux executable path, and
Codex CLI version string; it performs no writes.

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~WslDetectorTests
.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~WslDetectorIntegrationTests
```

Expected real result: at least one Codex installation in the configured WSL distributions, without assuming a specific username, executable path, or installed version.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexTray/Installation tests/CodexTray.Tests/WslDetectorTests.cs tests/CodexTray.Tests/WslDetectorIntegrationTests.cs
git commit -m "feat: detect Codex installations in WSL"
```

---

### Task 7: Lossless hook configuration merge and removal

**Files:**
- Create: `src/CodexTray/Installation/HookConfigMerger.cs`
- Create: `tests/CodexTray.Tests/HookConfigMergerTests.cs`

**Interfaces:**
- Produces: `HookConfigMerger.Install(string existingJson, string wslExePath) -> string`.
- Produces: `HookConfigMerger.Uninstall(string existingJson) -> string`.
- Produces: `HookConfigMerger.CountOwnedHandlers(string json) -> int`.

- [ ] **Step 1: Write failing JSON merge tests**

Fixtures must include a missing/empty document, unrelated top-level metadata, unrelated handlers in all five target events, matcher groups with extra recognized fields, existing owned handlers using an old path, duplicate owned handlers, empty groups, and invalid JSON. Assert exact foreign `JsonNode.DeepEquals` values survive install and uninstall.

```csharp
[Fact]
public void InstallTwice_ProducesExactlyFiveOwnedHandlers()
{
    string once = HookConfigMerger.Install("{}", "/mnt/c/Program Files/CodexTray/CodexTray.exe");
    string twice = HookConfigMerger.Install(once, "/mnt/c/Program Files/CodexTray/CodexTray.exe");
    Assert.Equal(5, HookConfigMerger.CountOwnedHandlers(twice));
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~HookConfigMergerTests`

Expected: compilation fails because the merger is absent.

- [ ] **Step 3: Implement owned-handler recognition and merge**

Parse with `JsonNode`. Reject a non-object root or non-object `hooks`. For every event in `SessionStart`, `UserPromptSubmit`, `Stop`, `Interrupt`, `SessionEnd`, traverse groups and remove only command handlers whose `command` string contains the exact token `--integration-id codex-tray-indicator-v1`. Remove a group only when its `hooks` array becomes empty. Append one matcher group per target event containing:

```json
{
  "hooks": [
    {
      "type": "command",
      "command": "\"/mnt/c/Program Files/CodexTray/CodexTray.exe\" --hook --integration-id codex-tray-indicator-v1",
      "timeout": 1,
      "async": true
    }
  ]
}
```

Escape the WSL path for a POSIX double-quoted command by escaping backslash,
double quote, dollar sign, and backtick. Serialize indented UTF-8 JSON with a
trailing newline.

- [ ] **Step 4: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~HookConfigMergerTests`

Expected: all merger tests pass.

```powershell
git add src/CodexTray/Installation/HookConfigMerger.cs tests/CodexTray.Tests/HookConfigMergerTests.cs
git commit -m "feat: merge owned Codex hooks safely"
```

---

### Task 8: Atomic WSL config store and install orchestration

**Files:**
- Create: `src/CodexTray/Installation/WslHookConfigStore.cs`
- Create: `src/CodexTray/Installation/IUserSettings.cs`
- Create: `src/CodexTray/Installation/RegistryUserSettings.cs`
- Create: `src/CodexTray/Installation/IntegrationInstaller.cs`
- Create: `tests/CodexTray.Tests/IntegrationInstallerTests.cs`

**Interfaces:**
- Produces: `IWslHookConfigStore`, implemented by `WslHookConfigStore`, with `ReadAsync(string distro, CancellationToken) -> Task<string>` and `WriteAtomicAsync(string distro, string json, bool createBackup, CancellationToken) -> Task`.
- Produces: settings properties `NotificationsEnabled`, `WslDistribution`; methods `SetStartup(string exePath)`, `RemoveStartup()`, and `Dispose()`.
- Produces: `sealed record IntegrationResult(bool Success, string Message, string? Distribution, string? CodexVersion)`.
- Produces: `IntegrationInstaller.InstallAsync(string exePath, CancellationToken)` and `UninstallAsync(CancellationToken)`.

- [ ] **Step 1: Write failing orchestration tests**

Use fake detector/config/settings/process ports. Cover no distro, multiple distros, successful path conversion and merge, invalid existing JSON with no write, atomic write failure with no registry change, reinstall, recorded-distro uninstall, fallback detection when record is missing, and foreign-hook preservation.

```csharp
[Fact]
public async Task Install_WhenConfigWriteFails_DoesNotEnableStartup()
{
    var fixture = InstallerFixture.OneDistro().WithWriteFailure();
    IntegrationResult result = await fixture.Installer.InstallAsync(@"C:\Program Files\Codex Tray\CodexTray.exe", CancellationToken.None);
    Assert.False(result.Success);
    Assert.False(fixture.Settings.StartupEnabled);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~IntegrationInstallerTests`

Expected: compilation fails because config store, settings, and installer types are absent.

- [ ] **Step 3: Implement WSL config I/O**

Read with the fixed command below and treat exit code 3 as missing:

```sh
if [ -f "$HOME/.codex/hooks.json" ]; then cat "$HOME/.codex/hooks.json"; else exit 3; fi
```

Write JSON on stdin to this fixed script, checking every exit code. Pass the
backup switch as argument `$1`; production omits `$2` so the script uses
`$HOME/.codex`, while integration tests pass a validated absolute directory as
`$2`:

```sh
set -eu
umask 077
create_backup="$1"
dir="${2:-$HOME/.codex}"
target="$dir/hooks.json"
mkdir -p "$dir"
tmp=$(mktemp "$dir/hooks.json.codextray.XXXXXX")
trap 'rm -f "$tmp"' EXIT HUP INT TERM
cat > "$tmp"
if [ "$create_backup" = "1" ] && [ -f "$target" ] && [ ! -f "$dir/hooks.json.codextray.bak" ]; then cp -p "$target" "$dir/hooks.json.codextray.bak"; fi
chmod 600 "$tmp"
mv -f "$tmp" "$target"
trap - EXIT HUP INT TERM
```

For uninstall, pass `createBackup=false`, which supplies `0` as `$1` and retains
the same temporary-file and rename behavior.

- [ ] **Step 4: Implement registry settings and orchestration**

Store `NotificationsEnabled` and `WslDistribution` under `HKCU\Software\CodexTray`. Store a quoted executable command under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexTray`. Installation changes settings only after config replacement succeeds. Uninstall changes hooks before removing the startup value and saved distro.

- [ ] **Step 5: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter "FullyQualifiedName~IntegrationInstallerTests|FullyQualifiedName~HookConfigMergerTests|FullyQualifiedName~WslDetectorTests"`

Expected: all selected tests pass.

```powershell
git add src/CodexTray/Installation tests/CodexTray.Tests/IntegrationInstallerTests.cs
git commit -m "feat: install Codex hooks atomically"
```

---

### Task 9: Tray visuals, notification policy, and single-instance UI

**Files:**
- Create: `src/CodexTray/UI/TrayIconFactory.cs`
- Create: `src/CodexTray/UI/NotificationPolicy.cs`
- Create: `src/CodexTray/UI/TrayApplicationContext.cs`
- Create: `tests/CodexTray.Tests/NotificationPolicyTests.cs`
- Create: `tests/CodexTray.Tests/TrayIconFactoryTests.cs`

**Interfaces:**
- Produces: `TrayIconFactory.Create(TrayState state) -> Icon`.
- Produces: `NotificationPolicy.ShouldNotify(StateTransition transition, CodexHookEventName? cause) -> bool`.
- Produces: `TrayApplicationContext(SessionStateStore, IpcServer, IUserSettings, Mutex)`.

- [ ] **Step 1: Write failing policy and icon tests**

Assert the policy returns true only for non-synthetic Busy-to-Ready transitions
caused by Stop or Interrupt; `TrayApplicationContext` separately requires the
persisted Notifications setting to be enabled. Assert 16x16 and 32x32 icon
handles are created for all four states, contain at least one non-transparent
colored pixel, and can be disposed repeatedly without throwing.

```csharp
[Theory]
[InlineData(CodexHookEventName.Stop, true)]
[InlineData(CodexHookEventName.Interrupt, true)]
[InlineData(CodexHookEventName.SessionStart, false)]
public void ShouldNotify_RequiresCompletedBusyTurn(CodexHookEventName cause, bool expected)
{
    var transition = new StateTransition(TrayState.Busy, TrayState.Ready, false);
    Assert.Equal(expected, NotificationPolicy.ShouldNotify(transition, cause));
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter "FullyQualifiedName~NotificationPolicyTests|FullyQualifiedName~TrayIconFactoryTests"`

Expected: compilation fails because UI helpers are absent.

- [ ] **Step 3: Implement icons and notification policy**

Generate circles on transparent bitmaps using Green `#22C55E`, Yellow `#EAB308`, Red `#EF4444`, and Gray `#6B7280`, with a dark outline. Clone the `Icon` produced from the bitmap handle and release the native HICON with `DestroyIcon` after cloning.

- [ ] **Step 4: Implement TrayApplicationContext**

Create one `NotifyIcon`, cached owned icons, and menu items for title, status, Start with Windows, Notifications, Reconnect / Test, and Exit. Marshal IPC callbacks with `SynchronizationContext.Post`. Handler mapping is:

```text
Event -> validate ToCodexHookEvent, then SessionStateStore.Apply
SetState -> SessionStateStore.SetSynthetic
QueryState -> response with Current
Shutdown -> response success, then post ExitThread
invalid protocol/kind/body -> SessionStateStore.SetError and response failure
```

Update text and icon only on state change. Show `NotifyIcon.ShowBalloonTip(3000, "Codex finished", "Codex is ready for the next prompt.", ToolTipIcon.Info)` only when the policy and setting both allow it. Dispose icons, menu, NotifyIcon, server cancellation source, settings, and mutex in `ExitThreadCore`.

- [ ] **Step 5: Add and test the per-user mutex**

Use `new Mutex(initiallyOwned: true, @"Local\CodexTray.Status.v1", out bool createdNew)`. If `createdNew` is false, dispose and return zero without creating WinForms objects. Add a process-level integration test that starts a hidden tray test host twice and asserts the second exits within one second.

- [ ] **Step 6: Verify GREEN and commit**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter "FullyQualifiedName~NotificationPolicyTests|FullyQualifiedName~TrayIconFactoryTests"`

Expected: all UI helper tests pass.

```powershell
git add src/CodexTray/UI tests/CodexTray.Tests/NotificationPolicyTests.cs tests/CodexTray.Tests/TrayIconFactoryTests.cs
git commit -m "feat: add tray UI and notifications"
```

---

### Task 10: Wire all application modes and maintenance UX

**Files:**
- Create: `src/CodexTray/Program.cs`
- Create: `src/CodexTray/ApplicationHost.cs`
- Create: `src/CodexTray/AppServices.cs`
- Modify: `src/CodexTray/CodexTray.csproj`
- Create: `tests/CodexTray.Tests/ProgramModeTests.cs`

**Interfaces:**
- Consumes: all command, hook, IPC, state, installer, settings, and UI interfaces from Tasks 1-9.
- Produces: executable behavior for Tray, Hook, HookTest, QueryState, Install, and Uninstall.

- [ ] **Step 1: Write failing mode-composition tests**

Extract `ApplicationHost.RunAsync(AppCommand, AppServices, CancellationToken) -> Task<int>` so tests can inject ports. Assert Hook exceptions return zero, query failures return two, install/uninstall return zero only for successful `IntegrationResult`, and tray duplicate returns zero without creating a view.

- [ ] **Step 2: Run the tests and confirm RED**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~ProgramModeTests`

Expected: compilation fails because `ApplicationHost` and `AppServices` are absent.

- [ ] **Step 3: Implement composition and user messages**

Mark `Main` with `[STAThread]`, parse arguments before WinForms initialization, and route maintenance results through a MessageBox whose success text includes detected distro/version plus the exact instruction `Start Codex, enter /hooks, review Codex Tray Indicator, and choose Trust.` Uninstall success reports that foreign hooks were preserved.

Change `OutputType` from `Library` to `WinExe` only after the
`ApplicationHost` tests are red. `Program.Main` constructs production services;
all mode-specific exception and exit-code behavior stays in `ApplicationHost`
and is covered by `ProgramModeTests`.

Set publish properties:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<PublishTrimmed>false</PublishTrimmed>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<DebugType>none</DebugType>
<DebugSymbols>false</DebugSymbols>
```

- [ ] **Step 4: Verify GREEN and full unit suite**

Run: `.\.tools\dotnet\dotnet.exe test CodexTray.sln --configuration Release --no-restore`

Expected: all tests pass with zero warnings and zero failures.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexTray/Program.cs src/CodexTray/ApplicationHost.cs src/CodexTray/AppServices.cs src/CodexTray/CodexTray.csproj tests/CodexTray.Tests/ProgramModeTests.cs
git commit -m "feat: compose Codex Tray application modes"
```

---

### Task 11: Publish and prove real WSL-to-Windows IPC

**Files:**
- Create: `scripts/test-wsl-ipc.ps1`
- Modify: `.gitignore`
- Create during build: `artifacts/publish/CodexTray.exe`

**Interfaces:**
- Consumes: published executable modes from Task 10.
- Produces: repeatable host/WSL IPC evidence with process exit codes and queried states.

- [ ] **Step 1: Publish a self-contained executable**

Run:

```powershell
.\.tools\dotnet\dotnet.exe publish src/CodexTray/CodexTray.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/publish
```

Expected: `artifacts/publish/CodexTray.exe` exists and no companion DLL is required.

- [ ] **Step 2: Write the IPC proof script**

The script must start the tray process, wait for `--query-state` to return `Inactive`, obtain the WSL path with `wsl.exe -d Ubuntu -- wslpath -a -u -- <absolute-exe-path>`, and invoke these commands from Ubuntu:

```text
<wsl-exe-path> --hook-test busy
<wsl-exe-path> --query-state
<wsl-exe-path> --hook-test ready
<wsl-exe-path> --query-state
```

Assert the queried values are `Busy` then `Ready`. Run 100 Busy/Ready pairs, assert every exit code is zero, assert final Ready, send shutdown, restart the tray, and query Inactive. After shutdown, time one Busy send and require exit zero in under 750 ms.

- [ ] **Step 3: Verify no runtime state files**

Snapshot filenames and last-write timestamps under the published executable directory and `%TEMP%\CodexTray*` before the 100-cycle run, then compare after it. Allow the standard .NET single-file extraction directory to appear once at process startup, but require no file creation or timestamp change per hook event and require no file named `status*`, `*.log`, `*.json`, or `*.db` attributable to CodexTray.

- [ ] **Step 4: Run the proof and inspect live processes**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-wsl-ipc.ps1`

Expected: Busy/Ready WSL transitions pass, 100 cycles pass, restart passes, fail-open timing passes, and the script exits zero.

- [ ] **Step 5: Commit**

```powershell
git add scripts/test-wsl-ipc.ps1 .gitignore
git commit -m "test: prove WSL named pipe integration"
```

---

### Task 12: Isolated install/reinstall/uninstall validation

**Files:**
- Create: `tests/CodexTray.Tests/WslHookConfigStoreIntegrationTests.cs`
- Create: `scripts/test-hook-install.ps1`

**Interfaces:**
- Consumes: real `WslHookConfigStore`, `HookConfigMerger`, published executable, and Ubuntu WSL.
- Produces: evidence that atomic mutation, deduplication, backup, and foreign-hook preservation work before changing the real Codex home.

- [ ] **Step 1: Write an integration test using an isolated Linux directory**

Create a unique directory under `/tmp/codex-tray-tests-<guid>`, seed `hooks.json` with one foreign SessionStart and one foreign Stop handler, invoke the config store with an overridable config directory, and assert install produces five owned plus two foreign handlers, second install still produces five owned, backup equals the original, and uninstall leaves exactly the two foreign handlers.

- [ ] **Step 2: Run the test and confirm RED if the store is not injectable**

Run: `.\.tools\dotnet\dotnet.exe test tests/CodexTray.Tests/CodexTray.Tests.csproj --filter FullyQualifiedName~WslHookConfigStoreIntegrationTests`

Expected: the test fails because the store does not yet accept an explicit test config directory.

- [ ] **Step 3: Add the narrow testable config-directory parameter**

Add `internal WslHookConfigStore(IProcessRunner runner, string? configDirectory)`.
Production passes null, causing the fixed script to use `$HOME/.codex`. Tests
pass only the generated `/tmp/codex-tray-tests-<guid>` path after validation
that it starts with `/tmp/codex-tray-tests-` and contains only ASCII letters,
digits, slash, hyphen, and hexadecimal GUID characters. The directory is a
separate `ArgumentList` value, never interpolated into shell source.

- [ ] **Step 4: Verify GREEN and cleanup**

Run the focused integration test and `scripts/test-hook-install.ps1`; the script removes only its validated `/tmp/codex-tray-tests-<guid>` directory in the same WSL shell after assertions.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexTray/Installation/WslHookConfigStore.cs tests/CodexTray.Tests/WslHookConfigStoreIntegrationTests.cs scripts/test-hook-install.ps1
git commit -m "test: validate atomic WSL hook installation"
```

---

### Task 13: Build the per-user Inno Setup installer

**Files:**
- Create: `installer/CodexTray.iss`
- Create: `scripts/build-release.ps1`
- Create during build: `dist/CodexTray.exe`
- Create during build: `dist/CodexTraySetup.exe`
- Create during build: `dist/SHA256SUMS.txt`

**Interfaces:**
- Produces: installer exit flow that invokes `CodexTray.exe --install` after file copy and `CodexTray.exe --uninstall` before removal.

- [ ] **Step 1: Author the installer definition**

Use exact Inno properties:

```ini
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

[Files]
Source: "..\dist\CodexTray.exe"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{app}\CodexTray.exe"; Parameters: "--install"; Flags: waituntilterminated

[UninstallRun]
Filename: "{app}\CodexTray.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCodexTrayIntegration"
```

- [ ] **Step 2: Author the deterministic release script**

The script removes only validated repository-local `artifacts/publish` and `dist` directories, restores, tests Release, publishes to a staging directory, copies the single executable to `dist`, locates `ISCC.exe`, compiles the installer, Authenticode-inspects both executables, and writes uppercase SHA-256 lines to `dist/SHA256SUMS.txt`. It stops on any nonzero exit code.

- [ ] **Step 3: Run the release build**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-release.ps1`

Expected: tests pass and all three dist files exist.

- [ ] **Step 4: Verify executable shape**

Assert `dist/CodexTray.exe` is the only application payload, has PE x64 architecture, has GUI subsystem, starts without a console window, and runs on a clean process environment without using the machine-wide dotnet runtime.

- [ ] **Step 5: Commit installer sources**

```powershell
git add installer/CodexTray.iss scripts/build-release.ps1
git commit -m "build: add self-contained Windows installer"
```

---

### Task 14: Real per-user install and Codex hook verification

**Files:**
- Modify only through installer: `%LOCALAPPDATA%\Programs\CodexTray`, HKCU application/startup keys, and Ubuntu `~/.codex/hooks.json`
- Preserve: Ubuntu `~/.codex/hooks.json.codextray.bak`

**Interfaces:**
- Consumes: `dist/CodexTraySetup.exe` and installed Codex 0.154.0.
- Produces: verified installed tray process, startup entry, five owned hooks, and removable integration.

- [ ] **Step 1: Snapshot current user integration state**

Record whether the install directory, startup registry value, application settings key, tray process, `~/.codex/hooks.json`, and dedicated backup exist. Do not print unrelated registry data or Codex configuration content.

- [ ] **Step 2: Run installer and verify read-back**

Run `dist/CodexTraySetup.exe` interactively. Verify the installed file hash matches `dist/CodexTray.exe`, the tray process is running, query state returns Inactive, startup value points to the installed executable, selected distro is Ubuntu, and `CountOwnedHandlers` returns five.

- [ ] **Step 3: Run installer a second time**

Verify the installed file remains valid, exactly five owned handlers exist, foreign handlers are unchanged, one tray server owns the pipe, and no duplicate startup value exists.

- [ ] **Step 4: Validate real Codex lifecycle without persisting a bypass**

Because Codex requires human review for non-managed hooks, open normal Codex and perform the documented `/hooks` review. After trust is confirmed, run these observed checks:

```text
new or resumed CLI session -> Green without notification
submit "say hello" -> Yellow before completion
normal Stop -> Green and one notification
submit a second prompt -> Yellow
Ctrl+C during the active turn -> Green
normal CLI exit -> Gray
```

If interactive trust is not completed in this session, report these checks as pending user confirmation. Do not simulate them and do not claim them from direct `--hook-test` results.

- [ ] **Step 5: Verify uninstall preservation, then reinstall final state**

Run the installed uninstaller. Verify tray exits, startup/settings values are removed, owned handler count is zero, and foreign hooks remain. Re-run `dist/CodexTraySetup.exe` so the final machine state is installed, then read back the same invariants from Step 2.

- [ ] **Step 6: Commit any test-driven corrections**

For every defect found, first add a focused failing automated regression test, implement the smallest correction, run the focused test and full suite, then commit with a message naming the corrected behavior.

---

### Task 15: Documentation and final verification

**Files:**
- Create: `README.md`
- Verify: `dist/CodexTray.exe`
- Verify: `dist/CodexTraySetup.exe`
- Verify: `dist/SHA256SUMS.txt`

**Interfaces:**
- Produces: concise user instructions for installation, one-time hook trust, uninstallation, architecture, IPC testing, notification toggle, troubleshooting, and rebuild.

- [ ] **Step 1: Write README against observed behavior**

Document double-click install, `/hooks` review, exact colors/tooltips, tray menu, automatic startup, uninstall path, the named-pipe-only runtime architecture, `--hook-test`, `--query-state`, notification toggle, WSL detection behavior, fail-open timeout, and build commands. State that SessionEnd timing follows Codex lifecycle semantics and distinguish automated IPC proof from interactive lifecycle proof.

- [ ] **Step 2: Run static forbidden-pattern checks**

Run repository searches for `status.txt`, `status.json`, `FileSystemWatcher`, recurring `Timer`, `SQLite`, `TcpListener`, OCR, screenshot parsing, runtime file logging, and looped `wsl.exe` invocation. Inspect every match and require that none implements runtime state transport or polling.

- [ ] **Step 3: Run the complete fresh verification pipeline**

Run in this order:

```powershell
.\.tools\dotnet\dotnet.exe clean CodexTray.sln --configuration Release
.\.tools\dotnet\dotnet.exe test CodexTray.sln --configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-wsl-ipc.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-hook-install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

Expected: every command exits zero, full tests report zero failures, both WSL scripts pass, and release artifacts are rebuilt after the tests.

- [ ] **Step 4: Verify final artifacts and machine state**

Recompute SHA-256 for both executables and compare with `dist/SHA256SUMS.txt`. Query the installed tray, inspect one running tray process, confirm five owned handlers, confirm the startup value, and confirm no state/log/database files were created by the 100-cycle proof.

- [ ] **Step 5: Commit documentation and report evidence**

```powershell
git add README.md dist/CodexTray.exe dist/CodexTraySetup.exe dist/SHA256SUMS.txt
git commit -m "release: deliver Codex Tray Indicator 1.0.0"
```

The final report must link both artifacts and README, state test counts and exact commands, provide hashes, name the detected Codex version/distribution, report installer/uninstaller read-back results, and explicitly list any visible interactive lifecycle step that could not be completed.
