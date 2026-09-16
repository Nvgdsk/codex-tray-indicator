# Changelog

Notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and semantic versioning. v1.3.0 is not yet publicly released.
Entries for 1.1.0 and 1.2.0 record feature evolution; separate public release dates
were not recorded, so no dates are invented here.

## [1.3.0] - Unreleased

### Added

- Animated pixel robot with Ready, Busy, Inactive and Error poses.
- Independent 500 ms mascot animation and partial USB-frame updates.
- English and Ukrainian setup, usage, privacy and troubleshooting guides.
- MIT license and public contribution/community guidance.

### Changed

- Application and tests target .NET 10; SDK and NuGet dependencies are locked.
- Compiler/analyzer warnings are errors and formatting is checked.
- Product version comes from the application project and is passed to Inno Setup.
- Release builds validate x64 GUI output, exact assets and independent SHA-256 checks.
- Unsigned binaries require explicit `-AllowUnsigned`.
- Generated binaries and signing material are excluded from Git.

## [1.2.0]

### Added

- Default-enabled pixel-shift animation with monotonic timing and safe screen margins.
- Latest-frame rendering after reconnects or slow serial writes.

## [1.1.0]

### Added

- Optional Turing 3.5-inch Revision A USB status display.
- Automatic device discovery, manual COM selection, orientation and reconnect settings.
- Isolated serial worker, reconnect behavior and opt-in hardware tests.

## [1.0.0]

### Added

- Windows tray indicator for one selected WSL Codex CLI installation.
- RAM-only, current-user named-pipe lifecycle transport.
- Ready, Busy, Error and Inactive states with completion notifications.
- Per-user startup and installer/uninstaller integration.
- Atomic owned-hook merge/removal, foreign-hook preservation and original-file backup.
- Automated domain, IPC and WSL integration tests.
