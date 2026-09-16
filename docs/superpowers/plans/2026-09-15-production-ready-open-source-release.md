# Codex Tray Indicator Production-Ready Open-Source Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare Codex Tray Indicator v1.3.0 as a production-ready MIT-licensed open-source Windows application, with reproducible .NET 10 builds, bilingual setup documentation, CI gates, and an explicitly approved draft GitHub Release containing an installer, portable executable, and SHA-256 checksums.

**Architecture:** Preserve the current WinForms/WSL hook/named-pipe/optional USB architecture and harden the repository around it. The project file remains the single version source; PowerShell owns local verification and release assembly; Inno Setup consumes the project version; GitHub Actions repeats the same gates and creates only a draft release.

**Tech Stack:** C# 14, .NET 10 SDK 10.0.401, WinForms, xUnit.net v3, PowerShell 7/Windows PowerShell 5.1-compatible scripts, Inno Setup 7.1.0 x64, Git, GitHub Actions, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-09-15-production-ready-open-source-release-design.md`

## Global Constraints

- [ ] Stop before every task below, report what will change, and obtain explicit approval from Vasyl before making those changes.
- [ ] Treat the existing application, installer, USB source, tests, and local `dist` files as user-owned work; never discard or overwrite them without the task-specific approval.
- [ ] Never read, copy, import, upload, or log any `.p12`, `.pfx`, private key, or certificate password.
- [ ] Keep v1.3.0 unsigned. Never add a signing secret or a security-control bypass.
- [ ] Do not create a GitHub repository, add a remote, push, tag, publish, or alter GitHub settings until the separate remote-publication approvals in Task 10.
- [ ] Use `apply_patch` for hand-authored file changes. Use formatters and package restore only for their intended mechanical outputs.
- [ ] Add a focused failing test or repository-contract assertion before each behavioral/build-contract change, then make the smallest implementation that passes it.
- [ ] Preserve runtime privacy and security invariants from the approved spec.
- [ ] Keep hardware access opt-in through `CODEXTRAY_TEST_USB_PORT`; CI must not enumerate, reserve, or write to a physical COM port.
- [ ] After every commit, show `git status --short` and the commit summary before requesting approval for the next task.

---

### Task 1: Preserve the Current v1.3.0 USB Baseline

**Files:**

- Modify: `README.md`
- Modify: `installer/CodexTray.iss`
- Modify: `src/CodexTray/CodexTray.csproj`
- Modify: `src/CodexTray/Installation/IUserSettings.cs`
- Modify: `src/CodexTray/Installation/RegistryUserSettings.cs`
- Modify: `src/CodexTray/UI/TrayApplicationContext.cs`
- Modify: `src/CodexTray/UI/TrayIconFactory.cs`
- Create: `src/CodexTray/UsbScreen/TuringScreenConnection.cs`
- Create: `src/CodexTray/UsbScreen/UsbMascotRenderer.cs`
- Create: `src/CodexTray/UsbScreen/UsbScreenController.cs`
- Create: `src/CodexTray/UsbScreen/UsbScreenOptions.cs`
- Create: `src/CodexTray/UsbScreen/UsbScreenPortDiscovery.cs`
- Create: `src/CodexTray/UsbScreen/UsbScreenRenderer.cs`
- Modify: `tests/CodexTray.Tests/IntegrationInstallerTests.cs`
- Create: `tests/CodexTray.Tests/UsbMascotTests.cs`
- Create: `tests/CodexTray.Tests/UsbScreenAnimationTests.cs`
- Create: `tests/CodexTray.Tests/UsbScreenControllerTests.cs`
- Create: `tests/CodexTray.Tests/UsbScreenHardwareTests.cs`
- Create: `tests/CodexTray.Tests/UsbScreenTests.cs`
- Create: `scripts/test-usb-screen.ps1`
- Do not stage: `dist/CodexTray.exe`, `dist/CodexTraySetup.exe`, `dist/SHA256SUMS.txt`

**Interfaces:**

- Consumes: the present uncommitted v1.3.0 working-tree changes and existing .NET 8 Release test environment.
- Produces: one isolated source/test commit for the already-implemented USB feature, with generated release assets still uncommitted.

- [x] **Step 1: Re-review the exact baseline diff and tracked-file boundary**

  Run: `git diff --check; git diff --stat; git status --short; git ls-files dist`

  Expected: no whitespace errors; source/test/installer/README and three `dist` files appear modified; the three `dist` paths are currently tracked.

- [x] **Step 2: Re-run the existing Release baseline**

  Run: `& .\.tools\dotnet\dotnet.exe test .\CodexTray.sln --configuration Release --no-restore`

  Expected: exit 0; 177 passed and 3 explicitly skipped USB hardware tests.

- [x] **Step 3: Stage only the reviewed USB baseline**

  Run: `git add README.md installer/CodexTray.iss src/CodexTray/CodexTray.csproj src/CodexTray/Installation/IUserSettings.cs src/CodexTray/Installation/RegistryUserSettings.cs src/CodexTray/UI/TrayApplicationContext.cs src/CodexTray/UI/TrayIconFactory.cs src/CodexTray/UsbScreen scripts/test-usb-screen.ps1 tests/CodexTray.Tests/IntegrationInstallerTests.cs tests/CodexTray.Tests/UsbMascotTests.cs tests/CodexTray.Tests/UsbScreenAnimationTests.cs tests/CodexTray.Tests/UsbScreenControllerTests.cs tests/CodexTray.Tests/UsbScreenHardwareTests.cs tests/CodexTray.Tests/UsbScreenTests.cs`

  Run: `git diff --cached --check; git diff --cached --stat; git status --short`

  Expected: only the listed source, installer, documentation, script, and test files are staged; all three `dist` files remain unstaged.

- [x] **Step 4: Commit the preserved baseline**

  Run: `git commit -m "feat: add optional USB status display"`

  Expected: commit succeeds; `git status --short` shows only the three modified `dist` files.

---

### Task 2: Add Repository Hygiene and the MIT License

**Files:**

- Create: `.gitattributes`
- Create: `.editorconfig`
- Modify: `.gitignore`
- Create: `LICENSE`
- Create: `scripts/test-repository-contract.ps1`
- Remove from Git index, retain locally: `dist/CodexTray.exe`
- Remove from Git index, retain locally: `dist/CodexTraySetup.exe`
- Remove from Git index, retain locally: `dist/SHA256SUMS.txt`

**Interfaces:**

- Consumes: tracked-file inventory and MIT ownership decision `Vasyl Danyliuk`.
- Produces: a source-only Git boundary and an executable repository-contract check.

- [x] **Step 1: Add the failing repository-contract check**

  Create `scripts/test-repository-contract.ps1` with terminating assertions that:

  - `LICENSE`, `.gitattributes`, and `.editorconfig` exist;
  - `git ls-files` contains no path under `dist/`, `artifacts/`, `.tools/`, `bin/`, `obj/`, or `TestResults/`;
  - no tracked filename ends in `.exe`, `.p12`, `.pfx`, `.snk`, `.key`, or `.pem`;
  - `.gitignore` covers generated output, IDE state, local tools, certificates, keys, and test output;
  - `LICENSE` contains the standard MIT grant and `Copyright (c) 2026 Vasyl Danyliuk`.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit, identifying the missing legal/hygiene files and currently tracked `dist` content.

- [x] **Step 2: Implement the repository boundary**

  Update `.gitignore` to cover `.tools/`, `artifacts/`, `dist/`, `**/bin/`, `**/obj/`, `TestResults/`, `*.trx`, `*.coverage*`, IDE files, and `*.p12`, `*.pfx`, `*.snk`, `*.key`, `*.pem`.

  Add `.gitattributes` with text normalization, LF for source/YAML/Markdown/JSON/XML, CRLF for `.ps1` and `.iss`, and binary classification for image/icon/executable formats.

  Add `.editorconfig` with UTF-8, final newlines, four-space C#/PowerShell indentation, two-space YAML indentation, and standard .NET naming/style defaults.

  Add the unmodified MIT license terms with the approved copyright line.

  Run: `git rm --cached -- dist/CodexTray.exe dist/CodexTraySetup.exe dist/SHA256SUMS.txt`

  Expected: the paths are staged as deleted from Git, but `Test-Path .\dist\CodexTray.exe`, `Test-Path .\dist\CodexTraySetup.exe`, and `Test-Path .\dist\SHA256SUMS.txt` all return `True` locally.

- [x] **Step 3: Make the repository contract pass**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: exit 0 and a concise success message.

- [x] **Step 4: Commit repository hygiene**

  Run: `git add .gitattributes .editorconfig .gitignore LICENSE scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "chore: establish open-source repository hygiene"`

  Expected: commit succeeds; local ignored `dist` assets remain on disk and no longer appear in `git status --short`.

---

### Task 3: Migrate to .NET 10 and Lock the Dependency Graph

**Files:**

- Create: `global.json`
- Create: `Directory.Build.props`
- Modify: `src/CodexTray/CodexTray.csproj`
- Modify: `tests/CodexTray.Tests/CodexTray.Tests.csproj`
- Create: `src/CodexTray/packages.lock.json`
- Create: `tests/CodexTray.Tests/packages.lock.json`
- Modify: `scripts/bootstrap-build-tools.ps1`
- Test: all files under `tests/CodexTray.Tests/`

**Interfaces:**

- Consumes: .NET 10 SDK 10.0.401, NuGet package versions approved in the spec, and the existing test behavior.
- Produces: `net10.0-windows` application/tests, xUnit.net v3 discovery, deterministic build defaults, and locked NuGet restores.

- [x] **Step 1: Extend the repository-contract test with failing toolchain assertions**

  Assert exact SDK `10.0.401`, `rollForward: latestPatch`, `allowPrerelease: false`, both target frameworks `net10.0-windows`, the approved package versions, lock-file presence, and absence of `Invoke-WebRequest`/`dotnet-install.ps1` from the bootstrap script.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit listing the .NET 8 target, old packages, missing lock files, and unpinned bootstrap download.

- [x] **Step 2: Pin the SDK and compiler/build defaults**

  Add `global.json` with SDK version `10.0.401`, `rollForward` set to `latestPatch`, and previews disabled.

  Add `Directory.Build.props` with `LangVersion` `14.0`, deterministic builds, nullable analysis, code-style enforcement, `AnalysisLevel` `latest-recommended`, warnings as errors, NuGet lock generation, and `ContinuousIntegrationBuild=true` only when `CI=true`.

- [x] **Step 3: Update framework and package references**

  Set both projects to `net10.0-windows`. Set `System.IO.Ports` to `10.0.12`. Replace xUnit v2 with `xunit.v3` `4.0.0`, `xunit.runner.visualstudio` `4.0.0`, `xunit.analyzers` `2.1.0`, `Microsoft.NET.Test.Sdk` `18.10.0`, and `coverlet.collector` `10.0.1`. Mark the analyzer, runner, and collector references `PrivateAssets=all` and set their `IncludeAssets` to `runtime; build; native; contentfiles; analyzers; buildtransitive`.

- [x] **Step 4: Replace the unpinned bootstrap path**

  Make `scripts/bootstrap-build-tools.ps1` first discover and verify `dotnet.exe` version `10.0.401` and Inno Setup `7.1.0`. If missing, it may invoke only exact `winget` package IDs and versions: `Microsoft.DotNet.SDK.10` version `10.0.401` and `JRSoftware.InnoSetup.7` version `7.1.0`, with silent/noninteractive agreement flags. It must re-discover and re-verify both tools after installation and must never download or execute a remote script directly.

- [x] **Step 5: Install missing build tools only after a separate command approval**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap-build-tools.ps1`

  Expected: exact .NET and Inno versions are reported. If installation is required, stop first and obtain command/network/system-change approval.

- [x] **Step 6: Generate and verify NuGet lock files**

  Run: `dotnet restore .\CodexTray.sln --use-lock-file --force-evaluate`

  Run: `dotnet restore .\CodexTray.sln --locked-mode`

  Expected: both commands exit 0; exactly one `packages.lock.json` exists beside each project and the locked restore makes no diff.

- [x] **Step 7: Run the migrated tests and fix only migration regressions**

  Run: `dotnet test .\CodexTray.sln --configuration Release --no-restore`

  Expected initially: any xUnit v3/compiler incompatibility fails visibly. Apply only compatibility changes required to retain existing semantics, then rerun until 177 tests pass and the same 3 hardware tests skip.

- [x] **Step 8: Verify format, analyzers, and contract**

  Run: `dotnet format .\CodexTray.sln --verify-no-changes --no-restore`

  Run: `dotnet build .\CodexTray.sln --configuration Release --no-restore`

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: all commands exit 0 with no warnings or file changes.

- [x] **Step 9: Commit the toolchain migration**

  Run: `git add global.json Directory.Build.props src/CodexTray/CodexTray.csproj src/CodexTray/packages.lock.json tests/CodexTray.Tests/CodexTray.Tests.csproj tests/CodexTray.Tests/packages.lock.json tests/CodexTray.Tests scripts/bootstrap-build-tools.ps1 scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "build: migrate to locked .NET 10 toolchain"`

  Expected: commit succeeds and contains no unrelated runtime behavior change.

---

### Task 4: Enforce Single-Source Versioning and the Unsigned Release Contract

**Files:**

- Modify: `installer/CodexTray.iss`
- Modify: `scripts/build-release.ps1`
- Create: `tests/CodexTray.Tests/ReleaseContractTests.cs`

**Interfaces:**

- Consumes: `<Version>1.3.0</Version>` from `src/CodexTray/CodexTray.csproj`, exact .NET/Inno tools, and explicit `-AllowUnsigned` acknowledgement.
- Produces: exactly `dist/CodexTray.exe`, `dist/CodexTraySetup.exe`, and `dist/SHA256SUMS.txt`, all validated before release use.

- [x] **Step 1: Write failing release-contract tests**

  Add tests that read the repository files and require:

  - no literal independent `AppVersion=1.3.0` in the installer;
  - an Inno preprocessor `MyAppVersion` define and `AppVersion={#MyAppVersion}`;
  - a mandatory explicit `-AllowUnsigned` switch in the build script;
  - project-version parsing with a strict `major.minor.patch` shape;
  - `/DMyAppVersion=<project version>` passed to `ISCC.exe`;
  - exact release filenames and rejection of extra files;
  - Authenticode status rejection unless unsigned output is explicitly allowed;
  - checksum re-reading and independent verification before success.

  Run: `dotnet test .\tests\CodexTray.Tests\CodexTray.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ReleaseContractTests`

  Expected: nonzero exit because the installer and build script still violate the new contract.

- [x] **Step 2: Parameterize the installer version**

  Add a required `MyAppVersion` preprocessor define in `installer/CodexTray.iss`, bind `AppVersion` to it, and keep all install/uninstall behavior unchanged.

- [x] **Step 3: Harden the release script**

  Add `[switch]$AllowUnsigned`; resolve `dotnet` from the pinned SDK environment; parse and validate the project version; pass it to Inno Setup 7.1.0; preserve the current safe-directory reset checks; run locked restore, Release build/test, and publish; validate x64 PE32+ Windows GUI shape; require exactly the three declared assets; verify both Authenticode states; require explicit allowance when they are unsigned; write then independently re-read SHA-256 checksums; reject duplicate, missing, or unexpected checksum entries.

- [x] **Step 4: Make the release-contract tests pass**

  Run: `dotnet test .\tests\CodexTray.Tests\CodexTray.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ReleaseContractTests`

  Expected: exit 0.

- [x] **Step 5: Verify that unsigned output cannot be accidental**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1`

  Expected: nonzero exit with a clear message requiring `-AllowUnsigned`; no release is reported as successful.

- [x] **Step 6: Build and verify the explicitly unsigned release**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -AllowUnsigned`

  Expected: exit 0; exact three-file output; both executables report unsigned/NotSigned; checksum verification succeeds.

- [x] **Step 7: Commit release hardening**

  Run: `git add installer/CodexTray.iss scripts/build-release.ps1 tests/CodexTray.Tests/ReleaseContractTests.cs; git diff --cached --check; git commit -m "build: enforce reproducible unsigned release contract"`

  Expected: commit succeeds; generated `dist` and `artifacts` remain ignored.

---

### Task 5: Publish Complete English and Ukrainian Documentation

**Files:**

- Modify: `README.md`
- Create: `README.uk.md`
- Create: `CHANGELOG.md`
- Create: `CONTRIBUTING.md`
- Create: `SECURITY.md`
- Create: `CODE_OF_CONDUCT.md`
- Modify: `scripts/test-repository-contract.ps1`

**Interfaces:**

- Consumes: approved installation, trust, security, WSL-only, USB, unsigned-release, testing, and support behavior.
- Produces: an English canonical README, complete Ukrainian translation, and public project governance documents.

- [x] **Step 1: Add failing documentation-contract assertions**

  Require all six public documents, reciprocal language links at the beginning of both READMEs, and headings/content for supported scope, installation, `/hooks` Trust, tray states, notifications, startup, USB configuration, multi-distro WSL selection, SHA-256 verification, Unknown Publisher, source build, tests, reinstall, uninstall, troubleshooting, privacy, and security.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit listing missing documents and sections.

- [x] **Step 2: Write the canonical English README**

  Link installation to `../../releases/latest`, which resolves within the eventual GitHub repository without embedding an account name. Explain that the self-contained `.exe` needs no separate .NET runtime, the first release is unsigned, Windows may show Unknown Publisher, and users should compare both downloads to `SHA256SUMS.txt` with `Get-FileHash` rather than disable any security control.

- [x] **Step 3: Write the complete Ukrainian README**

  Translate every user-facing setup, usage, build, test, troubleshooting, privacy, and security section; retain commands, filenames, and technical identifiers exactly.

- [x] **Step 4: Add release and community documents**

  Add Keep-a-Changelog-style entries for v1.0.0 through v1.3.0; contribution setup/style/test/PR rules; supported security versions and private GitHub Security Advisory reporting; and Contributor Covenant 2.1 with a project-owner enforcement contact expressed as a GitHub profile/security-advisory route, not a private email.

- [x] **Step 5: Make the documentation contract pass**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: exit 0.

- [x] **Step 6: Commit public documentation**

  Run: `git add README.md README.uk.md CHANGELOG.md CONTRIBUTING.md SECURITY.md CODE_OF_CONDUCT.md scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "docs: add bilingual open-source project guides"`

  Expected: commit succeeds; no certificate/signing instructions or private contact data are present.

**Execution note for Task 9:** `scripts/test-hook-install.ps1` and
`scripts/test-usb-screen.ps1` still reference the old `.tools/dotnet` executable.
The READMEs use direct pinned .NET 10 test commands instead. Revisit these legacy
helper paths during final verification before release publication.

---

### Task 6: Add GitHub Community and Dependency Metadata

**Files:**

- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `.github/ISSUE_TEMPLATE/feature_request.yml`
- Create: `.github/ISSUE_TEMPLATE/config.yml`
- Create: `.github/pull_request_template.md`
- Create: `.github/dependabot.yml`
- Modify: `scripts/test-repository-contract.ps1`

**Interfaces:**

- Consumes: public support policy and NuGet/GitHub Actions dependency ecosystems.
- Produces: structured issue intake, a security-report redirect, PR checklist, and weekly grouped dependency updates.

- [x] **Step 1: Add failing metadata-contract assertions**

  Require the five files; valid issue-form names/descriptions/body IDs; a bug form requesting Windows, WSL distro, Codex CLI, app version, reproduction, expected/actual behavior, and sanitized logs; disabled blank issues with security-reporting guidance in `SECURITY.md`; and weekly Dependabot entries for `nuget` and `github-actions` rooted at `/`.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit listing absent GitHub metadata.

- [x] **Step 2: Add issue forms and contribution template**

  Ensure issue forms warn users not to attach prompts, responses, transcripts, certificate material, or secrets. Keep `contact_links` empty until the exact GitHub owner is confirmed in Task 10. The PR template requires tests, documentation impact, privacy/security impact, and hardware-test declaration.

- [x] **Step 3: Add Dependabot configuration**

  Configure weekly updates with a limit of five open PRs per ecosystem, conventional `deps` commit prefixes, and grouped minor/patch updates. Do not configure automatic merge.

- [x] **Step 4: Verify and commit metadata**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Run: `git add .github/ISSUE_TEMPLATE .github/pull_request_template.md .github/dependabot.yml scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "chore: add GitHub community metadata"`

  Expected: both commands exit 0 and the commit succeeds.

---

### Task 7: Add the Least-Privilege Windows CI Workflow

**Files:**

- Create: `.github/workflows/ci.yml`
- Modify: `scripts/test-repository-contract.ps1`

**Interfaces:**

- Consumes: clean source checkout, pinned GitHub Action SHAs, global SDK pin, NuGet lock files.
- Produces: PR/push quality gates with read-only permissions and failure-only diagnostics.

- [x] **Step 1: Resolve immutable action SHAs from official release tags**

  Resolve the full commit SHA for `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0, and `actions/upload-artifact` v7.0.1 using `git ls-remote` against each official GitHub repository. Select the peeled tag target when present, otherwise the direct tag target; validate each value against `^[0-9a-f]{40,64}$`; record the release tag in an inline workflow comment beside the SHA.

  Expected: three full immutable SHAs, with no branch or floating major-version reference.

- [x] **Step 2: Add failing workflow-contract assertions**

  Require pull-request and push triggers; `windows-latest`; top-level `permissions: contents: read`; concurrency cancellation; only full-length SHA `uses:` references; pinned global JSON setup with NuGet cache; locked restore; format verification; Release build; non-hardware tests; transitive vulnerability audit; and diagnostic artifact upload guarded by `failure()`.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit because `ci.yml` is absent.

- [x] **Step 3: Implement CI in gate order**

  Create one Windows job that checks out without persisted credentials, sets up the SDK from `global.json`, restores with `--locked-mode`, verifies format with `--no-restore`, builds Release with `--no-restore`, tests Release with `--no-build --no-restore --logger trx`, runs `dotnet list .\CodexTray.sln package --vulnerable --include-transitive`, and uploads only `TestResults/**/*.trx` on failure for seven days. Set `CODEXTRAY_TEST_USB_PORT` to an empty value and never invoke the USB hardware script.

- [x] **Step 4: Verify workflow policy and commit**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Run: `git add .github/workflows/ci.yml scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "ci: add locked Windows quality gates"`

  Expected: contract exits 0; commit succeeds; no write permission or secret reference exists.

**Task 7 execution notes:**

- Microsoft.Testing.Platform rejects the planned VSTest-only `--logger trx`.
  Use xUnit's built-in `--report-xunit-trx --report-xunit-trx-filename ci.trx`
  after the `--` separator instead; the actual command passed 178 pure tests
  and generated `TestResults/ci.trx` without adding a dependency.
- Hosted CI excludes both `Integration` (real Ubuntu WSL required) and
  `UsbHardware`; full local integration verification remains in Task 9.
- The dependency audit parses version-1 JSON and fails on reported direct or
  transitive vulnerabilities, audit problems/errors, malformed or incomplete
  reports, and nonzero native command exit. The real audit and 12 controlled
  acceptance/rejection fixtures passed.
- SDK setup declares both exact `dotnet-version: 10.0.401` and `global.json`,
  then checks the selected SDK exactly matches the pin. This fails closed if
  the global `latestPatch` roll-forward would select a different hosted SDK.
- Fresh local gates with `CI=true` passed locked restore with transitive NuGet
  audit, formatting, Release build (zero warnings/errors), and 178 pure tests.
  Independent duplicate-key-aware YAML policy checks, PowerShell syntax checks,
  the repository contract, and whitespace checks passed. Implementation commit:
  `e361e17`. No hosted GitHub run, push, tag, or publication occurred.

---

### Task 8: Add the Draft GitHub Release Workflow

**Files:**

- Create: `.github/workflows/release.yml`
- Create: `.github/release-notes/v1.3.0.md`
- Modify: `scripts/test-repository-contract.ps1`

**Interfaces:**

- Consumes: semantic tag, project version, pinned action SHAs, exact toolchain, `scripts/build-release.ps1 -AllowUnsigned`.
- Produces: an unpublished GitHub draft containing exactly the installer, portable executable, and checksum file.

- [ ] **Step 1: Add failing release-workflow assertions**

  Require only `v*.*.*` tag pushes; read-only top-level permissions; `contents: write` only on the release job; full-SHA actions; exact SDK/Inno versions; strict tag-to-project-version comparison; the full CI gate sequence; explicit `-AllowUnsigned`; exact three assets; `gh release create --draft --verify-tag`; and no signing secret/material reference.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: nonzero exit because release automation is absent.

- [ ] **Step 2: Write v1.3.0 release notes**

  Describe WSL status tracking, tray notifications, optional supported Turing 3.5-inch Revision A USB output, installer/portable choices, WSL-only scope, the Unknown Publisher warning, and checksum verification. Do not describe the unsigned binary as trusted merely because it is downloadable.

- [ ] **Step 3: Implement the draft-only release workflow**

  Create a `windows-latest` release job that checks out without persisted credentials; sets up the exact SDK; installs `JRSoftware.InnoSetup.7` version `7.1.0` through exact noninteractive `winget`; verifies the tag is exactly `v` plus the project version; repeats locked restore/format/build/test/vulnerability gates; runs `scripts/build-release.ps1 -AllowUnsigned`; independently validates the asset set and hashes; then creates a draft via GitHub CLI with only `dist/CodexTraySetup.exe`, `dist/CodexTray.exe`, and `dist/SHA256SUMS.txt`.

- [ ] **Step 4: Verify least privilege and draft behavior**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Run: `rg -n "publish|draft|permissions|contents: write|p12|pfx|certificate|secret" .github/workflows/release.yml .github/release-notes/v1.3.0.md`

  Expected: contract exits 0; inspection shows draft creation, scoped write permission, and no signing material/secret reference.

- [ ] **Step 5: Commit release automation**

  Run: `git add .github/workflows/release.yml .github/release-notes/v1.3.0.md scripts/test-repository-contract.ps1; git diff --cached --check; git commit -m "ci: create verified draft releases from tags"`

  Expected: commit succeeds; workflow cannot publish a release automatically.

---

### Task 9: Run the Full Local Production-Readiness Verification

**Files:**

- Test: entire repository
- Generated and ignored: `artifacts/`
- Generated and ignored: `dist/CodexTray.exe`
- Generated and ignored: `dist/CodexTraySetup.exe`
- Generated and ignored: `dist/SHA256SUMS.txt`

**Interfaces:**

- Consumes: the complete local implementation from Tasks 1-8.
- Produces: a clean evidence bundle for source quality, dependency safety, release shape, hashes, tracked-file hygiene, and final review.

- [ ] **Step 1: Verify repository hygiene and absence of tracked secrets**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Run: `git ls-files | rg -i "(^|/)(dist|artifacts|\.tools|bin|obj|TestResults)/|\.(exe|p12|pfx|snk|key|pem)$"`

  Expected: contract exits 0; `rg` returns no tracked match.

- [ ] **Step 2: Verify a locked clean dependency graph**

  Run: `dotnet restore .\CodexTray.sln --locked-mode`

  Run: `dotnet list .\CodexTray.sln package --vulnerable --include-transitive`

  Expected: restore exits 0; audit reports no known vulnerable package.

- [ ] **Step 3: Verify format, analyzers, build, and tests**

  Run: `dotnet format .\CodexTray.sln --verify-no-changes --no-restore`

  Run: `dotnet build .\CodexTray.sln --configuration Release --no-restore`

  Run: `dotnet test .\CodexTray.sln --configuration Release --no-build --no-restore`

  Expected: all commands exit 0, no warnings; 177 pass and 3 opt-in hardware tests skip unless legitimate migration-only test count changes are explicitly reviewed.

- [ ] **Step 4: Build the final unsigned local release**

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -AllowUnsigned`

  Expected: exact three assets, valid x64 Windows GUI executable, unsigned status explicitly reported, hashes verified.

- [ ] **Step 5: Independently verify release assets**

  Run: `$expected = Get-Content .\dist\SHA256SUMS.txt; Get-FileHash .\dist\CodexTray.exe, .\dist\CodexTraySetup.exe -Algorithm SHA256 | Format-Table Hash, Path; $expected`

  Run: `Get-AuthenticodeSignature .\dist\CodexTray.exe, .\dist\CodexTraySetup.exe | Format-Table Status, StatusMessage, Path`

  Expected: calculated SHA-256 values exactly match both checksum lines; both signatures show the expected unsigned state.

- [ ] **Step 6: Review the complete branch diff and history**

  Run: `git diff --check; git status --short; git log --oneline --decorate 534f403..HEAD; git diff --stat 534f403..HEAD`

  Expected: no uncommitted source changes, no whitespace errors, generated assets ignored, and a readable sequence of focused commits.

- [ ] **Step 7: Run a final security diff review**

  Review `534f403..HEAD` with the repository security policy, focusing on WSL arguments, named-pipe ACL/limits, hook merging/trust, installer behavior, registry scope, serial bounds/timeouts, workflow permissions, release inputs, and secret exposure.

  Expected: no unresolved high-confidence vulnerability; any valid finding returns to a focused fix/test/verification cycle before Task 10.

- [ ] **Step 8: Record manual acceptance as pending**

  Report the ten manual checks from the spec as a checklist. Do not claim production release acceptance until Vasyl runs or observes them on the supported Windows/WSL/USB machine.

---

### Task 10: Configure and Publish to GitHub Through Separate Approval Gates

**Files:**

- Modify after owner confirmation: `SECURITY.md`
- Modify after owner confirmation: `.github/ISSUE_TEMPLATE/config.yml`
- Optional local Git configuration: `origin`
- External state: new public GitHub repository, branch settings, pushed branch/tag, draft release

**Interfaces:**

- Consumes: verified clean branch, confirmed GitHub owner, approved repository name `codex-tray-indicator`, and manual owner decisions.
- Produces: public source repository and, only after later approvals, a draft then published v1.3.0 release.

- [ ] **Step 1: Confirm owner, repository name, and public visibility**

  Ask Vasyl to confirm the exact GitHub account/organization, `codex-tray-indicator`, and `public`. Do not infer the owner from local Git identity.

- [ ] **Step 2: Replace repository URL templates and verify documentation links**

  Add the confirmed repository's absolute private-advisory URL to `SECURITY.md` and `.github/ISSUE_TEMPLATE/config.yml`; keep the README release links relative.

  Run: `rg -n "example\.com|github\.com/example|github\.com/your-" README.md README.uk.md SECURITY.md .github`

  Expected: no unfinished example URL match.

  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-repository-contract.ps1`

  Expected: exit 0.

- [ ] **Step 3: Commit final repository URLs**

  Run: `git add SECURITY.md .github/ISSUE_TEMPLATE/config.yml; git diff --cached --check; git commit -m "docs: finalize GitHub security links"`

  Expected: commit succeeds and working tree is clean.

- [ ] **Step 4: Obtain separate approval to create/configure the remote repository**

  After approval, create an empty public GitHub repository without generated README, `.gitignore`, or license; add `origin`; set the default branch to `main`; enable Issues and private vulnerability reporting; disable automatic release publication. Report every external mutation.

- [ ] **Step 5: Obtain separate approval for the initial source push**

  After approval, push the reviewed source history/branch only. Confirm GitHub CI starts and wait for a successful result before any tag.

- [ ] **Step 6: Configure recommended protection after explicit approval**

  Protect `main` with required CI checks, blocked force pushes/deletions, and required pull-request review where supported by the account plan. Enable Dependabot security updates and secret scanning where available. Do not enable automatic dependency merging.

- [ ] **Step 7: Complete manual acceptance**

  Execute and record the spec checklist: clean install; `/hooks` Trust; Inactive/Ready/Busy/completion transitions; single notification; autostart; reinstall; uninstall preservation; Revision A USB render/reconnect; downloaded checksum verification; Unknown Publisher messaging.

- [ ] **Step 8: Obtain separate approval to create and push tag `v1.3.0`**

  Before approval, show the exact commit SHA to be tagged and confirm the project version is `1.3.0`. After approval, create an annotated tag and push only that tag.

- [ ] **Step 9: Inspect the generated draft release**

  Confirm CI gates passed, the release remains draft, notes are correct, and exactly `CodexTraySetup.exe`, `CodexTray.exe`, and `SHA256SUMS.txt` are attached. Download to a temporary directory and independently verify both hashes.

- [ ] **Step 10: Obtain final approval to publish the draft**

  Publish only after Vasyl explicitly approves the verified draft. Report the public repository URL, release URL, tag/commit, attached asset names, hashes, and the unsigned-publisher status.

---

## Plan Completion Audit

- [ ] Compare every task and acceptance command against the approved design spec; resolve any mismatch before implementation.
- [ ] Search this plan for unfinished markers and vague implementation instructions; no ambiguity may remain.
- [ ] Verify every referenced file path exists now or has one explicit creation task.
- [ ] Verify package, SDK, Inno, and action release versions against official sources again immediately before implementing dependency/workflow tasks.
- [ ] Verify all GitHub mutations remain isolated in Task 10 and retain their individual approval gates.
