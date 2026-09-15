# Production-Ready Open-Source Release Design

**Date:** 2026-09-15
**Target release:** v1.3.0
**Repository:** Codex Tray Indicator
**License:** MIT
**Copyright holder:** Vasyl Danyliuk

## Purpose

Prepare the current Codex Tray Indicator codebase for a public open-source
GitHub repository and a controlled v1.3.0 release. The repository will contain
source code, tests, build automation, and documentation. Installable Windows
binaries will be distributed through GitHub Releases rather than committed to
Git.

The first public release will be unsigned because no trusted Authenticode
code-signing certificate is available. The release process must state this
clearly, publish SHA-256 checksums, and retain a clean extension point for
future signing.

## Current State

- The application currently targets .NET 8 for Windows x64 and is published as
  a self-contained single-file WinForms executable. The public v1.3.0 release
  will migrate to .NET 10 LTS because .NET 8 support ends on 2026-11-10.
- Inno Setup produces a per-user installer.
- Codex lifecycle events arrive through hooks configured in one selected WSL
  distribution and cross into Windows through a current-user-only named pipe.
- The current v1.3.0 working tree includes USB output for supported Turing
  3.5-inch Revision A displays.
- The Release test baseline is 177 passing automated tests and 3 skipped,
  explicit opt-in USB hardware tests.
- A repository-wide static security review found no reportable vulnerabilities.
  Release-trust gaps remain: current binaries are unsigned, release automation
  is absent, and the developer bootstrap downloads an upstream installer script
  without a repository-pinned digest.
- The v1.3.0 source and USB tests are currently uncommitted and must be preserved
  before repository-hardening changes begin.

## Release Model

Git stores only maintainable source artifacts:

- application and test source;
- installer definition;
- build and verification scripts;
- GitHub Actions workflows and dependency automation;
- English and Ukrainian documentation;
- licensing, contribution, security, and community files.

Generated content is excluded from Git:

- `dist/*.exe` and other release binaries;
- `artifacts/`, `bin/`, `obj/`, and test output;
- local tool installations;
- signing material including `.p12`, `.pfx`, and `.snk` files.

GitHub Releases distributes exactly these user-facing assets for v1.3.0:

1. `CodexTraySetup.exe` as the recommended installer;
2. `CodexTray.exe` as the portable executable;
3. `SHA256SUMS.txt` covering both executables.

The tag `v1.3.0` creates a draft release. A draft is published only after the
manual acceptance checklist passes and the repository owner explicitly
approves publication.

## Versioning and Build Reproducibility

`src/CodexTray/CodexTray.csproj` is the single source of the product version.
The release build passes that version to Inno Setup so the installer definition
does not carry an independently editable version number. A release job rejects
a tag whose semantic version differs from the project version.

The repository pins .NET 10 SDK 10.0.401 through `global.json` and targets
`net10.0-windows`. The self-contained release remains Windows x64 and does not
require an end user to install .NET separately. NuGet dependency lock files are
committed, and CI restores in locked mode. CI builds use deterministic and
continuous-integration build properties. GitHub Actions dependencies are pinned
to immutable commit SHAs.

The migration uses supported stable packages current at design approval:

- `System.IO.Ports` 10.0.12;
- `Microsoft.NET.Test.Sdk` 18.10.0;
- `xunit.v3` 4.0.0;
- `xunit.runner.visualstudio` 4.0.0;
- `xunit.analyzers` 2.1.0;
- `coverlet.collector` 10.0.1.

The existing xUnit.net v2 package is deprecated, so the test project moves to
xUnit.net v3 as part of the framework migration. Test semantics and hardware
opt-in behavior must remain unchanged.

The build script keeps its existing path-safety checks and validates:

- restore, Release build, and tests succeed;
- publish output contains exactly one portable application executable;
- the application is an x64 PE32+ Windows GUI image;
- Inno Setup produces the expected installer;
- the release directory contains exactly the two executables and checksum file;
- every checksum is generated from the final attached asset.

Unsigned output requires an explicit build option. A future signing stage can
replace that option with certificate-backed signing without changing asset names
or the rest of the release flow.

## Continuous Integration

The Windows CI workflow runs for pull requests and pushes to protected source
branches. It performs these gates in order:

1. check out the exact revision;
2. install the pinned .NET SDK;
3. restore NuGet packages in locked mode;
4. verify formatting and compiler/analyzer diagnostics;
5. build the solution in Release mode;
6. run all non-hardware automated tests;
7. check dependencies for known vulnerabilities;
8. upload diagnostic test results only when a gate fails.

USB hardware tests remain opt-in through `CODEXTRAY_TEST_USB_PORT`. Hosted CI
must never enumerate, reserve, or write to a physical COM port.

## Release Automation

The release workflow runs only for semantic version tags matching `v*.*.*`.
It repeats all CI gates from a clean checkout, validates the tag against the
project version, installs a pinned Inno Setup version, and invokes the repository
release script with the explicit unsigned-release option.

After verifying filenames, PE shape, and checksums, the workflow creates a
GitHub draft release and attaches the three expected assets. Release notes
include the unsigned-publisher warning and link to checksum verification
instructions. Workflow permissions remain read-only by default and grant
`contents: write` only to the release job that creates the draft.

No workflow receives a `.p12`, `.pfx`, password, or signing secret for v1.3.0.
Future signing secrets must use GitHub encrypted secrets or an external signing
service and must never be written to the repository or workflow logs.

## Documentation

`README.md` is the canonical English guide. `README.uk.md` is a complete
Ukrainian translation. Both files begin with a language switcher and link users
to the latest GitHub Release rather than to binaries in the repository.

The guides explain:

- product purpose and supported Windows, WSL, Codex CLI, and USB configurations;
- installation through `CodexTraySetup.exe`;
- the required one-time `/hooks` review and Trust action;
- tray states, notifications, Windows startup, and USB-screen settings;
- WSL-only scope and selection when multiple distributions contain Codex;
- SHA-256 verification in PowerShell;
- the expected `Unknown publisher` warning for the unsigned first release;
- source build prerequisites and commands;
- automated, WSL integration, and opt-in hardware test commands;
- reinstall, uninstall, troubleshooting, privacy, and security boundaries.

Additional public files are:

- `LICENSE` with the MIT text and `Vasyl Danyliuk` as copyright holder;
- `CHANGELOG.md` with v1.0.0 through v1.3.0 release history;
- `CONTRIBUTING.md` with local setup, build, test, style, and pull-request rules;
- `SECURITY.md` with supported-version policy and a private GitHub security
  advisory reporting route;
- `CODE_OF_CONDUCT.md` using the Contributor Covenant;
- GitHub issue forms for reproducible bug reports and feature requests.

## Security and Privacy

Existing security invariants remain release requirements:

- hook input is bounded and only lifecycle identifiers are forwarded;
- prompts, responses, transcript paths, working directories, and model metadata
  are discarded;
- named-pipe IPC remains restricted to the current Windows user;
- IPC frames, subprocesses, and serial writes remain bounded by size or timeout;
- WSL process arguments are passed through `ProcessStartInfo.ArgumentList`;
- hook configuration updates remain atomic and preserve foreign handlers;
- the installer never bypasses Codex hook trust;
- application preferences and startup entries remain under HKCU;
- USB output contains only the coarse indicator state and rendered graphics.

Dependency updates are monitored for NuGet packages and GitHub Actions. Public
release documentation never instructs users to disable antivirus, bypass hook
trust, or suppress Windows security controls. It explains the unsigned warning
without presenting bypassing security checks as routine behavior.

## Error Handling and Failure Policy

CI or release automation stops without publishing when any required gate fails.
In particular, a version mismatch, unlocked dependency graph, failed analyzer,
failed test, unexpected publish output, unexpected release asset, or checksum
failure is fatal.

The release workflow creates only a draft. Failed manual verification leaves
that draft unpublished. Corrections use a new commit and tag rather than
silently replacing an already published binary under an existing tag.

Runtime behavior retains its current principles: Codex hooks fail open so they
cannot block Codex, invalid IPC requests become a visible error state, USB
failures remain isolated from Codex state, and installation reports actionable
errors without partially enabling startup after a failed hook write.

## Verification and Acceptance

Automated acceptance requires:

- locked restore succeeds from a clean checkout;
- formatting and analyzer gates pass;
- Release build passes;
- all non-hardware tests pass;
- no unexpected secret or certificate file is tracked;
- release build produces exactly the declared assets;
- published checksum text matches independently calculated SHA-256 values.

Manual acceptance on a real supported machine verifies:

1. clean installer execution;
2. one-time `/hooks` Trust flow;
3. Inactive, Ready, Busy, and post-completion Ready transitions;
4. exactly one completion notification for a genuine completed turn;
5. Windows restart and per-user autostart;
6. reinstall without duplicate owned hooks and without changing foreign hooks;
7. uninstall cleanup while preserving foreign hooks and the original backup;
8. supported Revision A USB display rendering and reconnect behavior;
9. downloaded-asset checksums;
10. expected unsigned-publisher messaging.

## Implementation Stages and Approval Gates

Every stage requires explicit owner approval before it begins.

1. **Preserve v1.3.0 baseline.** Review the current diff, run the existing
   Release tests, and commit the current application/USB functionality without
   mixing in release-hardening changes.
2. **Open-source files and repository hygiene.** Add legal, community, security,
   bilingual documentation, and ignore rules; remove tracked executables from
   the repository index without deleting the user's local release copies.
3. **.NET 10 and build hardening.** Migrate the application and tests to .NET 10
   LTS and xUnit.net v3; add SDK and dependency pinning, analyzers, single-source
   versioning, unsigned-release acknowledgement, and corresponding tests.
4. **GitHub automation.** Add CI, Dependabot, issue forms, and draft-release
   workflows with least-privilege permissions and immutable action references.
5. **Full verification.** Run formatting, locked restore, Release build, tests,
   release build, checksum checks, and a final source/security review.
6. **Remote publication.** Confirm repository owner/name and visibility, then
   request separate approval for remote creation/configuration, initial push,
   release tag, and draft-release publication.

## Non-Goals for v1.3.0

- Authenticode signing without a suitable trusted code-signing certificate;
- Microsoft Store, WinGet, Chocolatey, or other package-manager publication;
- native Windows Codex CLI or Codex desktop-app tracking;
- automatic integration with more than one WSL distribution;
- support for USB display revisions other than the documented Turing 3.5-inch
  Revision A protocol;
- automatic publication of a release before manual acceptance and owner
  confirmation.
