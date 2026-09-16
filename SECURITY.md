# Security Policy

## Supported versions

| Version | Security maintenance |
|---|---|
| 1.3.x | Current development line; supported after publication |
| <= 1.2.x | No new security updates planned |

## Private reporting

Use [Report a vulnerability](https://github.com/Nvgdsk/codex-tray-indicator/security/advisories/new)
on GitHub.
Do not disclose vulnerabilities in public Issues or pull requests.
If private reporting is unavailable, use contact methods on the repository
owner's GitHub profile, or ask for a private contact without incident details.

Include the affected version, realistic impact, reproduction steps and
sanitized diagnostics. Never attach prompts, responses, transcripts,
credentials or certificate material. Coordinate public disclosure with
maintainers after a fix or mitigation is available.

## System and trust boundaries

Scope includes application source, WSL hooks, installation, named-pipe IPC,
USB output, dependencies and build/release automation.
The indicator runs per user without a network listener.
Treat hook payloads, configuration and device metadata as untrusted inputs.
Current-user IPC is not isolation from malicious same-user software.

## Security invariants

- Bound hook input, IPC frames, subprocess execution and serial writes.
- Forward lifecycle identifiers only; discard conversation content and paths.
- Keep runtime state in RAM and IPC restricted to the current Windows user.
- Hooks fail open without blocking Codex.
- Preserve foreign hooks and backups during atomic configuration updates.
- Never bypass Codex hook Trust or require elevated runtime privileges.
- Keep preferences/startup per user and USB failures isolated from Codex state.
- Keep secrets out of Git/logs; validate final assets and checksums.
- Require explicit acknowledgement for unsigned release output.

## Reportable findings

Report reachable violations of these invariants, unauthorized data exposure,
unsafe command execution, destructive configuration changes, and meaningful
security-boundary failures. Explain attacker access and actual impact.
No blanket vulnerability-class exclusions are authorized by this policy.

## Known limitations

v1.3.0 is intentionally unsigned. SHA-256 checks detect corruption but do not
independently authenticate the publisher. The indicator is not a sandbox or
Codex authorization mechanism. These limitations do not waive other findings.
