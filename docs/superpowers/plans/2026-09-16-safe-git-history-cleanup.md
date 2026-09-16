# Safe Git History Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Owner override: inline execution only, no subagents, new branches, clones or worktrees. Obtain explicit approval before every task and before the real rewrite.

**Goal:** Make the existing publication branch's history suitable for an initial GitHub source push by removing historical `dist/` entries while preserving source history and local release files.

**Architecture:** First preserve the separately approved pending source work in ordinary commits. Then create and verify a local bundle of the exact publication branch, install a pinned local filtering tool, preview the filter, and rewrite only that branch after another approval. Keep the old `master` and Codex service refs unchanged; validate the publication branch rather than claiming that every local Git object has disappeared.

**Tech Stack:** Windows PowerShell, Git, isolated Python environment, git-filter-repo 2.47.0, the .NET SDK pinned by `global.json`, existing repository verification scripts.

**Spec:** `docs/superpowers/specs/2026-09-15-production-ready-open-source-release-design.md`, especially Release Model and Remote Publication; owner's subsequent instructions require one existing working branch/folder and task-by-task approval.

## Global Constraints

- Planning approval is not execution approval. This document does not authorize commits, installation, backup creation, history rewriting, push, tags or GitHub changes.
- Use the current folder and `codex/production-ready-v1.3.0` only. Do not switch to or modify `master`; do not delete Codex service refs.
- Never read, copy, import, upload, or log any `.p12`, `.pfx`, private key, or certificate password. Historical filename checks are metadata-only. If a prohibited path appears, stop before creating a bundle or export; do not inspect its contents.
- Keep v1.3.0 unsigned. Keep installer, portable EXE and checksums under local ignored `dist/`; distribute them later through GitHub Releases, not Git or Git LFS.
- No `git init`, blanket staging, stash, recursive deletion, automatic garbage collection, reflog expiry, force push, mirror push or all-branches push.
- An ordinary clean-tree commit of pending work requires separate approval. Never discard pending work to satisfy the clean-tree gate.
- Do not run the tray, authenticated Codex requests, full integration tests or USB tests without their separate approval.
- Stop on every failed native command, changed source hash, unexpected ref mutation or concurrent edit. Do not automatically retry a rewrite.
- Hand-authored repository changes use `apply_patch`. Temporary backup/evidence output is local, ignored and never uploaded.

## Preflight evidence — keep local and ignored

- Record the current branch, HEAD, origin destination and complete ref inventory in ignored local evidence.
- Inspect the publication branch's reachable object names and sizes for historical `dist/` entries and oversized blobs. [GitHub large-file documentation](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)
- Record pending paths and preserve them before cleanup; do not assume a fixed file count or clean working tree.
- Check for prohibited certificate/key filenames without reading their contents. Stop before backup or export if any appear.
- Verify available Git, Python and .NET tools before executing commands. Install the pinned filtering tool only after separate approval.
- Record local release hashes and their source commit separately; ignored existing assets do not establish verification of current source.
- Do not commit personal paths, installed-software inventories or local machine snapshots to this plan.

## File and state map

- Plan: `docs/superpowers/plans/2026-09-16-safe-git-history-cleanup.md` (this document only).
- Prerequisite, separately approved source work: the fifteen paths shown by `git status --porcelain=v1 --untracked-files=all`; README instructions for new opt-in tests also need separate approval.
- Local backup/evidence directory: a new unique child of `artifacts/history-cleanup/`; contains `publication-before.bundle`, source/file hashes, branch/ref metadata, commit mapping and verification evidence.
- Local tool environment: `.tools/git-filter-repo-2.47.0/`; no system Python or global PATH changes.
- Real rewrite target: `refs/heads/codex/production-ready-v1.3.0` only.
- Historical removal target: repository-relative `dist/` only, including its old EXEs and checksum manifest. Do not broaden to extensions, size-based deletion or unrelated directories.
- Public source files, `.gitignore`, workflows, hooks, settings and local release assets must remain byte-identical across the rewrite.

### Task 1: Preserve pending work and establish a clean source snapshot

**Files:** Inspect pending source/tests/docs and this plan. Ordinary commits are a prerequisite with their own exact-file approval; no source edits are part of history cleanup.

**Interfaces:** Consumes the owner's choice about pending work; produces a reviewed, committed, clean publication branch. The 202-test result from the previous review is historical evidence, not an automatic final release approval.

- [ ] Obtain approval for the exact pending feature/documentation scope and any ordinary commits. If the owner wants to leave the work uncommitted, stop cleanup here and preserve it unchanged.
- [ ] Before committing, confirm the index is empty, list all pending paths, inspect the exact diff, and obtain approval for an explicit staging list. Never use `git add .`, `git add -A` or a directory that could include unrelated work.
- [ ] Reverify changed source and approved README changes with isolated tests. Keep account and USB opt-ins unset in the verification subprocess. Do not use the full-suite command as a substitute.
- [ ] Commit only approved files, then show the commit summary and worktree state. Include or separately commit this plan only with explicit approval.
- [ ] Require an empty result from `git status --porcelain=v1 --untracked-files=all`, and no tracked `dist/` entries. If new edits appear later, stop instead of stashing or resetting them.

### Task 2: Capture and verify a recoverable local backup

**Files:** Create only a unique ignored `artifacts/history-cleanup/` evidence directory and bundle after task-specific approval.

**Interfaces:** Produces `$cleanupRoot`, `$cleanupRef`, `$cleanupBefore`, `$cleanupRefsBefore`, `$cleanupTreeBefore`, `$cleanupFileHashes`, `$cleanupBackup`, and `$cleanupCommitCountBefore` for Tasks 3–5. Keep these values in the same PowerShell execution session; persist evidence locally if the session changes and revalidate before continuing.

- [ ] Obtain approval for the local backup and briefly pause all tasks editing this repository. Initialize checked command execution and resolve the exact workspace:

```powershell
$ErrorActionPreference = 'Stop'
function Invoke-CleanupGit {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Git failed: ' + ($Arguments -join ' ')) }
    return $result
}
$cleanupRoot = [IO.Path]::GetFullPath((Invoke-CleanupGit -Arguments @('rev-parse', '--show-toplevel')))
$cleanupExpectedPath = Read-Host 'Enter the independently verified absolute repository path'
if ([string]::IsNullOrWhiteSpace($cleanupExpectedPath) -or -not [IO.Path]::IsPathRooted($cleanupExpectedPath)) { throw 'An absolute repository path is required.' }
$cleanupExpectedRoot = [IO.Path]::GetFullPath($cleanupExpectedPath)
if ($cleanupRoot -ne $cleanupExpectedRoot) { throw 'Unexpected repository root.' }
$cleanupRef = 'refs/heads/codex/production-ready-v1.3.0'
if ((Invoke-CleanupGit -Arguments @('symbolic-ref', 'HEAD')) -ne $cleanupRef) { throw 'Unexpected branch.' }
if (@(Invoke-CleanupGit -Arguments @('status', '--porcelain=v1', '--untracked-files=all')).Count) { throw 'Pending work: stop.' }
if (@(Invoke-CleanupGit -Arguments @('ls-files', '--', 'dist/')).Count) { throw 'Current dist files are tracked: stop.' }
$cleanupBefore = Invoke-CleanupGit -Arguments @('rev-parse', 'HEAD')
$cleanupRefsBefore = @(Invoke-CleanupGit -Arguments @('for-each-ref', '--format=%(refname) %(objectname)'))
$cleanupTreeBefore = @(Invoke-CleanupGit -Arguments @('ls-tree', '-r', 'HEAD'))
$cleanupCommitCountBefore = Invoke-CleanupGit -Arguments @('rev-list', '--count', 'HEAD')
$cleanupObjectNames = @(Invoke-CleanupGit -Arguments @('rev-list', '--objects', $cleanupRef))
$cleanupForbiddenNames = @($cleanupObjectNames | Where-Object { $_ -match ' (?i:.*\.(?:p12|pfx|snk|key|pem))$' })
if ($cleanupForbiddenNames.Count) { throw 'Prohibited historical filename found: do not copy or inspect contents.' }
$cleanupFileHashes = @(
    @(Invoke-CleanupGit -Arguments @('ls-files')) + @('dist/CodexTray.exe', 'dist/CodexTraySetup.exe', 'dist/SHA256SUMS.txt') |
        ForEach-Object { [pscustomobject]@{ Path = $_; Hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } }
)
$cleanupEvidence = Join-Path $cleanupRoot ('artifacts/history-cleanup/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cleanupEvidence | Out-Null
$cleanupBackup = Join-Path $cleanupEvidence 'publication-before.bundle'
Invoke-CleanupGit -Arguments @('check-ignore', $cleanupBackup) | Out-Null
Invoke-CleanupGit -Arguments @('bundle', 'create', $cleanupBackup, $cleanupRef)
Invoke-CleanupGit -Arguments @('bundle', 'verify', $cleanupBackup)
Invoke-CleanupGit -Arguments @('bundle', 'list-heads', $cleanupBackup)
Get-FileHash -LiteralPath $cleanupBackup -Algorithm SHA256
```

- [ ] Check that `bundle list-heads` names exactly the original publication ref at `$cleanupBefore`. The bundle contains that branch's complete reachable committed history, not uncommitted/index/configuration state. [Git bundle documentation](https://git-scm.com/docs/git-bundle)
- [ ] Save the captured values and backup hash as local evidence using a task-scoped evidence writer; never dump full Git configuration, credential helpers, environment variables or credential-bearing data.
- [ ] Show the backup location, original SHA, branch, and historical removal paths. Do not proceed without a verified bundle. Do not treat an old local branch or reflog as the only backup.

### Task 3: Install the pinned local tool and preview the exact filter

**Files:** Create `.tools/git-filter-repo-2.47.0/` only after installation approval; preview output stays under `.git/filter-repo/` and local ignored evidence.

**Interfaces:** Consumes Task 2's snapshot. Produces `$cleanupToolPython` pointing to the isolated interpreter and a reviewed preview. There is no production-code implementation or behavioral TDD change here; the failing acceptance gate is the already-proven oversized reachable history object.

- [ ] Obtain approval for the local tool installation. Create a new isolated environment, refuse to reuse an unexpected existing installation, and install only the specified package/version from PyPI:

```powershell
$cleanupToolDir = Join-Path $cleanupRoot '.tools/git-filter-repo-2.47.0'
if (Test-Path -LiteralPath $cleanupToolDir) { throw 'Tool directory already exists: inspect before reuse.' }
& python -m venv $cleanupToolDir
if ($LASTEXITCODE -ne 0) { throw 'Tool environment creation failed.' }
$cleanupToolPython = Join-Path $cleanupToolDir 'Scripts/python.exe'
& $cleanupToolPython -m pip --isolated install --index-url https://pypi.org/simple --only-binary=:all: --no-deps 'git-filter-repo==2.47.0'
if ($LASTEXITCODE -ne 0) { throw 'Pinned tool installation failed.' }
& $cleanupToolPython -m pip --isolated show git-filter-repo
if ($LASTEXITCODE -ne 0) { throw 'Tool package verification failed.' }
& $cleanupToolPython -m git_filter_repo --version
if ($LASTEXITCODE -ne 0) { throw 'Tool entrypoint verification failed.' }
```

Record version and installed tool SHA-256. The version pin is not independent publisher authentication; stronger package-digest pinning can be proposed separately, not silently added as a new bootstrap workflow.

- [ ] Recheck clean status, branch, HEAD, backup and local asset hashes. Then obtain approval for the preview, which creates local analysis files but does not rewrite refs:

```powershell
& $cleanupToolPython -m git_filter_repo --dry-run --invert-paths --path dist/ --prune-empty never --prune-degenerate never --preserve-commit-hashes --preserve-commit-encoding --replace-refs update-no-add --refs $cleanupRef
if ($LASTEXITCODE -ne 0) { throw 'Preview failed: stop; do not add force automatically.' }
```

- [ ] Inspect the pinned tool's preview/debug evidence and confirm the filter selects only `dist/` removal and only the publication ref. Recompare every ref, HEAD and current file hash to Task 2. Preview is not proof of the final rewrite and can differ in commit-map handling.
- [ ] Explain the actual rewrite's effects before asking approval: commit IDs change; the tool can internally reset the tracked working tree; the clean-tree and identical-current-tree gates are therefore mandatory. `--refs` limits scope and implies partial filtering, preserving origin/unselected refs and avoiding automatic reflog expiry/GC. [Upstream filtering options](https://github.com/newren/git-filter-repo/blob/v2.47.0/Documentation/git-filter-repo.txt)

### Task 4: Rewrite only the publication branch after explicit approval

**Files/state:** Mutate only the approved publication history and tool-owned mapping output. No source edits, local EXE deletion, branch creation or external write.

**Interfaces:** Consumes the verified snapshot/backup/preview; produces `$cleanupAfter` and `.git/filter-repo/commit-map` plus a still-clean, byte-identical current source tree.

- [ ] Obtain explicit approval naming the branch, `dist/` historical removal, changed commit IDs, internal tracked-tree reset risk, verified backup and rollback procedure. A `+` for an earlier task is not this approval.
- [ ] Immediately revalidate Task 2's exact root, branch, clean tree, `$cleanupBefore`, complete ref listing, and all current file hashes. Stop on concurrent changes. Verify the bundle again.
- [ ] Execute once, only after these guards pass:

```powershell
& $cleanupToolPython -m git_filter_repo --force --invert-paths --path dist/ --prune-empty never --prune-degenerate never --preserve-commit-hashes --preserve-commit-encoding --replace-refs update-no-add --refs $cleanupRef
if ($LASTEXITCODE -ne 0) { throw 'Rewrite failed: stop, preserve evidence, request rollback approval.' }
$cleanupAfter = Invoke-CleanupGit -Arguments @('rev-parse', 'HEAD')
```

`--force` bypasses the fresh-clone safety check to respect the owner's same-folder requirement. It is NOT permission to force-push or overwrite user edits. Never run the command outside the exact guarded workspace.

- [ ] Retain the old/new commit map as local ignored evidence. Do not hand-edit existing sealed security reports or historical verification SHAs to impersonate a review of new commits.

### Task 5: Verify source identity, history scope and publication readiness

**Files:** Read current source, mapping and local artifacts; generated .NET output is ignored. Update this plan's execution evidence only with task-specific approval. No publication.

**Interfaces:** Produces a verified cleanup result, rollback instructions and an explicit separate initial-push approval gate.

- [ ] Verify HEAD changed, but its complete `git ls-tree -r HEAD` output equals `$cleanupTreeBefore`, all captured current file hashes match, commit count equals `$cleanupCommitCountBefore`, and worktree status is clean.
- [ ] Compare all refs with `$cleanupRefsBefore`. Only `$cleanupRef` may have changed; `master`, service refs and origin must be unchanged. Check the origin URL without dumping configuration.
- [ ] Require no historical `dist/` paths on the publication branch and no reachable blobs above 100 MiB. Run the size check with individually checked Git subprocesses, rather than a pipeline that can hide upstream failure:

```powershell
$cleanupObjectsAfter = @(Invoke-CleanupGit -Arguments @('rev-list', '--objects', $cleanupRef))
if (@($cleanupObjectsAfter | Where-Object { $_ -match ' dist/' }).Count) { throw 'Historical dist entry remains.' }
$cleanupObjectSizes = $cleanupObjectsAfter | & git cat-file '--batch-check=%(objectname) %(objecttype) %(objectsize) %(rest)'
if ($LASTEXITCODE -ne 0) { throw 'Object-size inspection failed.' }
$cleanupOversized = @($cleanupObjectSizes | Where-Object { $_ -match '^\S+ blob (\d+) ' -and [long]$Matches[1] -gt 100MB })
if ($cleanupOversized.Count) { throw 'Oversized publication blob remains.' }
Invoke-CleanupGit -Arguments @('diff', '--exit-code', $cleanupBefore, $cleanupAfter, '--', '.', ':(exclude)dist/**')
Invoke-CleanupGit -Arguments @('fsck', '--full')
```

Old `master`, service refs, the bundle and retained local Git objects may still contain the old EXE. That is intentional. Do not merge them into the cleaned branch or push them. This procedure is publication-scope cleanup, not repository-wide secret eradication or disk reclamation.

- [ ] Run the repository contract and fresh isolated .NET verification at the new SHA. Before running, keep account/USB opt-ins unset in this child process and set `DOTNET_CLI_HOME` to a workspace-local `.tools/dotnet-home`. Resolve `dotnet` from the approved build environment and verify its SDK against `global.json`:

```powershell
& ./scripts/test-repository-contract.ps1
& dotnet restore ./CodexTray.sln --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all
& dotnet format ./CodexTray.sln --verify-no-changes --no-restore
& dotnet build ./CodexTray.sln --configuration Release --no-restore
& dotnet test ./CodexTray.sln --configuration Release --no-build --no-restore --filter 'Category!=Integration&Category!=UsbHardware' --results-directory TestResults -- --report-xunit-trx --report-xunit-trx-filename history-cleanup-pure.trx
```

Execute and check each command's exit status separately; stop immediately on failure. Report actual test counts, compiler warnings and errors. These gates do not run account/USB/manual acceptance and do not verify current release binaries. Any later source change requires fresh checks.

- [ ] Show original/new SHA, backup location/hash, mapping location, removed historical paths, unchanged local asset hashes, unchanged unselected refs, and actual verification results.
- [ ] Before a separately approved initial push, perform an approved read-only `git ls-remote --heads --tags origin` check. If the remote is no longer empty, stop for a new publication decision; do not force-push.
- [ ] Request separate approval for the exact final SHA and one explicit branch refspec. The intended GitHub destination is `refs/heads/main`; local rename to `main` is optional and needs its own approval. Do not execute push here. Never use `--all`, `--mirror`, `--tags` or `--force`. GitHub CI, settings, tags and release publication retain the original Task 10 approval gates.

## Rollback — only after separate explicit approval

Before rollback, record the failed state and check that no edits or new commits have appeared. If current files changed, stop and ask how to preserve them; do not overwrite them. Because the approved rewrite must leave the current tree identical, a branch-ref rollback can restore history without a working-tree reset when those identity checks still pass.

```powershell
Invoke-CleanupGit -Arguments @('bundle', 'verify', $cleanupBackup)
Invoke-CleanupGit -Arguments @('bundle', 'unbundle', $cleanupBackup)
$cleanupRollbackCurrent = Invoke-CleanupGit -Arguments @('rev-parse', $cleanupRef)
# Approve this exact old/current SHA pair before executing the next command.
Invoke-CleanupGit -Arguments @('update-ref', $cleanupRef, $cleanupBefore, $cleanupRollbackCurrent)
Invoke-CleanupGit -Arguments @('rev-parse', 'HEAD')
Invoke-CleanupGit -Arguments @('status', '--porcelain=v1', '--untracked-files=all')
```

Verify the restored SHA, original source tree, file hashes and unselected refs. Never delete the bundle or run garbage collection automatically. If the tree-identity precondition does not hold, this rollback command sequence is not sufficient: stop for an owner-approved recovery procedure rather than adding `reset --hard`.

## Plan self-review

- [x] Addresses the spec's source-only Git / release-only binary distribution model without introducing Git LFS.
- [x] Preserves pending user work with a clean-tree prerequisite instead of implicit stash/discard.
- [x] Limits the rewrite to one existing publication ref and historical `dist/` entries.
- [x] Includes a verified branch-history bundle, source/local-asset hashes, scope checks and conditional rollback.
- [x] Keeps installation, backup, preview, destructive rewrite, recovery and publication approvals distinct.
- [x] Keeps real account/USB/manual acceptance and production-ready release claims outside isolated verification.
- [x] Defines execution-session variables before use; no new helper/code feature is assumed.
- [x] This planning task creates only this document. All execution tasks above remain unchecked and unapproved.
