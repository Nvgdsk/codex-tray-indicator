# Contributing

Thank you for helping improve Codex Tray Indicator.
Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Contributions are under [MIT](LICENSE);
do not contribute code or assets you do not have permission to license.

## Before making a change

Use Issues for reproducible non-security bugs and proposed features.
For vulnerabilities, follow [SECURITY.md](SECURITY.md) privately.
Describe the problem and expected behavior before proposing a large change.
Native Windows/desktop Codex tracking and non-Revision-A USB support are not existing features.

Never upload prompts, responses, transcripts, credentials, certificate material or
unsanitized configuration. Describe lifecycle events instead of conversation contents.

## Development setup

Use Windows x64, Git, .NET SDK 10.0.401 and Inno Setup 7.1.0.
Install WSL2 Ubuntu with Codex CLI for real integration tests.
See [README.md](README.md#source-build) for prerequisites, script review,
execution-policy considerations and separate build-tool licensing.

From the repository root:

```powershell
./scripts/bootstrap-build-tools.ps1
dotnet restore ./CodexTray.sln --locked-mode
dotnet format ./CodexTray.sln --verify-no-changes --no-restore
dotnet build ./CodexTray.sln --configuration Release --no-restore
dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category!=Integration&Category!=UsbHardware'
powershell.exe -NoProfile -File ./scripts/test-repository-contract.ps1
```

Full tests require the named Ubuntu/Codex baseline.
Hardware tests are explicit opt-in; do not set `CODEXTRAY_TEST_USB_PORT` unless you intend
to write to your physical screen. Follow [README test instructions](README.md#tests).
State whether WSL/hardware tests were run or skipped; do not represent skipped tests as passed.

## Style and tests

- Follow `.editorconfig`: four-space C#/PowerShell indentation, file-scoped C# namespaces,
  and two-space JSON/YAML indentation.
- Keep compiler/analyzer warnings at zero; do not broadly suppress diagnostics.
- Add a failing regression test before a behavioral fix, then verify it passes.
- Keep IPC input, subprocesses and serial writes bounded.
- Preserve fail-open hooks, current-user IPC, foreign handlers, and manual hook Trust.
- Keep English and Ukrainian guides aligned when user-facing behavior changes.

Only change product version in `src/CodexTray/CodexTray.csproj`.
Intentional package changes update exact references and lock files together,
then run locked restore. Do not delete locks as a workaround.

## Pull requests

Fork the repository and create a focused branch. Open a PR with the problem,
implementation summary, tests and documentation impact. Small, reviewable changes
are preferred. Explain privacy/security implications and any hardware requirements.
Use clear commits such as `fix: ...`, `feat: ...` or `docs: ...`.

Do not commit `dist`, `artifacts`, local tools, `bin`, `obj`, test output,
executables, signing keys or personal diagnostics. Passing automated checks does
not replace manual acceptance for installer, Trust, notifications or USB behavior.

## Release responsibilities

A maintainer approves version changes and publication.
Build locally with `scripts/build-release.ps1 -AllowUnsigned` only when an unsigned
release is intended. Verify both executables against `SHA256SUMS.txt`.
Future release automation must remain draft-only until manual acceptance;
do not silently replace assets under a published version.
