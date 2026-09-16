[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$failures = [Collections.Generic.List[string]]::new()
$securityReportUrl = 'https://github.com/Nvgdsk/codex-tray-indicator/security/advisories/new'

function Assert-RepositoryContract {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        $script:failures.Add($Message)
    }
}

function Get-PackageVersion {
    param(
        [Parameter(Mandatory)][xml]$Project,
        [Parameter(Mandatory)][string]$PackageName
    )

    $reference = @($Project.SelectNodes('//PackageReference')) |
        Where-Object { $_.Include -eq $PackageName } |
        Select-Object -First 1
    if ($null -eq $reference) {
        return $null
    }

    return [string]$reference.Version
}

Push-Location $repositoryRoot
try {
    foreach ($requiredFile in @('LICENSE', '.gitattributes', '.editorconfig')) {
        Assert-RepositoryContract `
            -Condition (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
            -Message "Required repository file is missing: $requiredFile"
    }

    $publicDocuments = @('README.md', 'README.uk.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'CODE_OF_CONDUCT.md')
    foreach ($document in $publicDocuments) {
        Assert-RepositoryContract `
            -Condition (Test-Path -LiteralPath $document -PathType Leaf) `
            -Message "Required public document is missing: $document"
    }

    $readmeSections = @(
        'supported-scope', 'installation', 'hook-trust', 'tray-states',
        'notifications', 'startup', 'usb-screen', 'wsl-selection',
        'checksums', 'unsigned-release', 'source-build', 'tests',
        'reinstall', 'uninstall', 'troubleshooting', 'privacy', 'security'
    )
    foreach ($readme in @('README.md', 'README.uk.md')) {
        if (-not (Test-Path -LiteralPath $readme -PathType Leaf)) {
            continue
        }
        $readmeText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $readme), [Text.Encoding]::UTF8)
        Assert-RepositoryContract `
            -Condition ($readmeText -match '\A\[English\]\(README\.md\) \| \[[^\]]+\]\(README\.uk\.md\)') `
            -Message "$readme must begin with reciprocal English/Ukrainian language links."
        foreach ($section in $readmeSections) {
            $sectionPattern = '(?m)^<a id="' + [regex]::Escape($section) + '"></a>\r?\n\r?\n## \S'
            Assert-RepositoryContract `
                -Condition ($readmeText -match $sectionPattern) `
                -Message "$readme is missing a headed documentation section: $section"
        }
        foreach ($requiredContent in @(
            '../../releases/latest', 'CodexTraySetup.exe', 'CodexTray.exe',
            '/hooks', 'Trust', 'Ready', 'Busy', 'Error', 'Inactive',
            'Start with Windows', 'Notifications', 'USB35INCHIPSV2',
            'VID_1A86&PID_5722', 'Revision A', 'WslDistribution',
            'SHA256SUMS.txt', 'Get-FileHash', 'Unknown Publisher',
            '10.0.401', '7.1.0', '--locked-mode', '-AllowUnsigned',
            '--query-state', '--hook-test', '--install', '--uninstall',
            'CODEXTRAY_TEST_USB_PORT', 'SECURITY.md', 'LICENSE'
        )) {
            Assert-RepositoryContract `
                -Condition ($readmeText.Contains($requiredContent)) `
                -Message "$readme is missing required setup/usage content: $requiredContent"
        }
    }

    if ((Test-Path -LiteralPath 'README.md' -PathType Leaf) -and
        (Test-Path -LiteralPath 'README.uk.md' -PathType Leaf)) {
        $englishReadme = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'README.md'))
        $ukrainianReadme = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'README.uk.md'))
        $codeBlockPattern = '(?s)```powershell\r?\n(.*?)```'
        $englishCommands = @([regex]::Matches($englishReadme, $codeBlockPattern) | ForEach-Object {
            $_.Groups[1].Value.Replace("`r`n", "`n").Trim()
        })
        $ukrainianCommands = @([regex]::Matches($ukrainianReadme, $codeBlockPattern) | ForEach-Object {
            $_.Groups[1].Value.Replace("`r`n", "`n").Trim()
        })
        Assert-RepositoryContract `
            -Condition (($englishCommands | ConvertTo-Json -Compress) -ceq ($ukrainianCommands | ConvertTo-Json -Compress)) `
            -Message 'English and Ukrainian README command blocks must be identical.'
        foreach ($readmeText in @($englishReadme, $ukrainianReadme)) {
            foreach ($block in [regex]::Matches($readmeText, $codeBlockPattern)) {
                $parseTokens = $null
                $parseErrors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput(
                    $block.Groups[1].Value, [ref]$parseTokens, [ref]$parseErrors)
                Assert-RepositoryContract `
                    -Condition ($parseErrors.Count -eq 0) `
                    -Message 'A README PowerShell command block contains a syntax error.'
            }
        }
    }

    if (Test-Path -LiteralPath 'CHANGELOG.md' -PathType Leaf) {
        $changelogText = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'CHANGELOG.md'))
        foreach ($version in @('1.0.0', '1.1.0', '1.2.0', '1.3.0')) {
            Assert-RepositoryContract `
                -Condition ($changelogText.Contains("## [$version]")) `
                -Message "CHANGELOG.md is missing release history for $version."
        }
    }
    if (Test-Path -LiteralPath 'SECURITY.md' -PathType Leaf) {
        $securityText = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'SECURITY.md'))
        $securityReportLinks = [regex]::Matches($securityText, '\[Report a vulnerability\]\(([^)]+)\)')
        Assert-RepositoryContract `
            -Condition ($securityText.Contains('1.3.x') -and $securityReportLinks.Count -eq 1 -and
                $securityReportLinks[0].Groups[1].Value -ceq $securityReportUrl) `
            -Message 'SECURITY.md must declare supported versions and link to the confirmed repository private advisory reporting route.'
    }
    if (Test-Path -LiteralPath 'CODE_OF_CONDUCT.md' -PathType Leaf) {
        $conductText = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'CODE_OF_CONDUCT.md'))
        Assert-RepositoryContract `
            -Condition ($conductText.Contains('Contributor Covenant') -and $conductText.Contains('version 2.1')) `
            -Message 'CODE_OF_CONDUCT.md must attribute Contributor Covenant version 2.1.'
        Assert-RepositoryContract `
            -Condition ($conductText.Contains('../../security/advisories/new')) `
            -Message 'CODE_OF_CONDUCT.md must provide the approved private owner-contact route.'
    }

    $githubMetadataFiles = @(
        '.github/ISSUE_TEMPLATE/bug_report.yml',
        '.github/ISSUE_TEMPLATE/feature_request.yml',
        '.github/ISSUE_TEMPLATE/config.yml',
        '.github/pull_request_template.md',
        '.github/dependabot.yml'
    )
    foreach ($metadataFile in $githubMetadataFiles) {
        Assert-RepositoryContract `
            -Condition (Test-Path -LiteralPath $metadataFile -PathType Leaf) `
            -Message "Required GitHub metadata is missing: $metadataFile"
    }

    $requiredFormIds = [ordered]@{
        '.github/ISSUE_TEMPLATE/bug_report.yml' = @(
            'privacy', 'windows-version', 'wsl-distribution', 'codex-version',
            'app-version', 'reproduction', 'expected-behavior', 'actual-behavior', 'diagnostics'
        )
        '.github/ISSUE_TEMPLATE/feature_request.yml' = @('privacy', 'problem', 'proposal', 'alternatives')
    }
    foreach ($formPath in $requiredFormIds.Keys) {
        if (-not (Test-Path -LiteralPath $formPath -PathType Leaf)) {
            continue
        }
        $formText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $formPath))
        foreach ($header in @('name', 'description')) {
            Assert-RepositoryContract `
                -Condition ($formText -match ('(?m)^' + $header + ': [^\r\n]+$')) `
                -Message "$formPath is missing a nonempty $header."
        }
        Assert-RepositoryContract `
            -Condition ($formText -match '(?m)^body:\s*$') `
            -Message "$formPath must define a body."
        $idMatches = [regex]::Matches($formText, '(?m)^    id: ([a-zA-Z0-9_-]+)\s*$')
        $formIds = @($idMatches | ForEach-Object { $_.Groups[1].Value })
        Assert-RepositoryContract `
            -Condition ($formIds.Count -gt 0 -and @($formIds | Select-Object -Unique).Count -eq $formIds.Count) `
            -Message "$formPath must have valid, unique input IDs."
        foreach ($requiredId in $requiredFormIds[$formPath]) {
            Assert-RepositoryContract `
                -Condition ($formIds -contains $requiredId) `
                -Message "$formPath is missing required input: $requiredId"
        }
        foreach ($inputBlock in ([regex]::Split($formText, '(?m)^  - type: ') | Select-Object -Skip 1)) {
            if ($inputBlock -match '\Amarkdown\s') {
                continue
            }
            Assert-RepositoryContract `
                -Condition ($inputBlock -match '(?m)^    id: [a-zA-Z0-9_-]+\s*$' -and
                    $inputBlock -match '(?m)^      label: [^\r\n]+$') `
                -Message "$formPath contains an input without a valid ID or label."
        }
        foreach ($privacyToken in @('prompts', 'responses', 'transcripts', 'certificate material', 'secrets', 'SECURITY.md')) {
            Assert-RepositoryContract `
                -Condition ($formText.Contains($privacyToken)) `
                -Message "$formPath is missing privacy/security guidance: $privacyToken"
        }
    }

    $issueConfigPath = '.github/ISSUE_TEMPLATE/config.yml'
    if (Test-Path -LiteralPath $issueConfigPath -PathType Leaf) {
        $issueConfigText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $issueConfigPath))
        Assert-RepositoryContract `
            -Condition ($issueConfigText -match '(?m)^blank_issues_enabled: false\s*$') `
            -Message 'Blank GitHub issues must be disabled.'
        $contactNames = [regex]::Matches($issueConfigText, '(?m)^  - name: [^\r\n]+$')
        $contactUrls = [regex]::Matches($issueConfigText, '(?m)^    url: ([^\r\n]+)$')
        $contactDescriptions = [regex]::Matches($issueConfigText, '(?m)^    about: [^\r\n]+$')
        Assert-RepositoryContract `
            -Condition ($issueConfigText -match '(?m)^contact_links:[ \t]*\r?\n' -and
                $contactNames.Count -eq 1 -and $contactUrls.Count -eq 1 -and
                $contactDescriptions.Count -eq 1 -and
                $contactUrls[0].Groups[1].Value.Trim() -ceq $securityReportUrl -and
                $issueConfigText.Contains('SECURITY.md')) `
            -Message 'Issue config must provide one named private security contact for the confirmed repository, with a description and SECURITY.md guidance.'
    }

    $pullRequestTemplatePath = '.github/pull_request_template.md'
    if (Test-Path -LiteralPath $pullRequestTemplatePath -PathType Leaf) {
        $pullRequestText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $pullRequestTemplatePath))
        foreach ($checklistTopic in @('tests', 'documentation', 'privacy', 'security', 'hardware')) {
            Assert-RepositoryContract `
                -Condition ($pullRequestText -match ('(?im)^- \[ \] .*' + $checklistTopic)) `
                -Message "PR template is missing a checklist item for $checklistTopic."
        }
    }

    $dependabotPath = '.github/dependabot.yml'
    if (Test-Path -LiteralPath $dependabotPath -PathType Leaf) {
        $dependabotText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $dependabotPath))
        Assert-RepositoryContract `
            -Condition ($dependabotText -match '(?m)^version: 2\s*$') `
            -Message 'Dependabot must use configuration version 2.'
        $updateBlocks = [regex]::Matches($dependabotText,
            '(?ms)^  - package-ecosystem: "?([a-z-]+)"?[ \t]*\r?\n(.*?)(?=^  - package-ecosystem:|\z)')
        $ecosystems = @($updateBlocks | ForEach-Object { $_.Groups[1].Value })
        Assert-RepositoryContract `
            -Condition ($ecosystems.Count -eq 2 -and $ecosystems -contains 'nuget' -and $ecosystems -contains 'github-actions') `
            -Message 'Dependabot must configure NuGet and GitHub Actions exactly once each.'
        foreach ($updateBlock in $updateBlocks) {
            $ecosystem = $updateBlock.Groups[1].Value
            $configuration = $updateBlock.Groups[2].Value
            foreach ($requiredSetting in @(
                '(?m)^    directory: "/"\s*$',
                '(?m)^      interval: weekly\s*$',
                '(?m)^    open-pull-requests-limit: 5\s*$',
                '(?m)^      prefix: deps\s*$',
                '(?m)^    groups:\s*$',
                '(?m)^        patterns: \["\*"\]\s*$',
                '(?m)^          - minor\s*$',
                '(?m)^          - patch\s*$'
            )) {
                Assert-RepositoryContract `
                    -Condition ($configuration -match $requiredSetting) `
                    -Message "Dependabot $ecosystem is missing a required root/weekly/limit/prefix/group setting: $requiredSetting"
            }
        }
        Assert-RepositoryContract `
            -Condition ($dependabotText -notmatch '(?i)(enable-auto-merge|gh\s+pr\s+merge|--auto)') `
            -Message 'Dependabot configuration must not enable automatic merge.'
    }

    $ciPath = '.github/workflows/ci.yml'
    Assert-RepositoryContract `
        -Condition (Test-Path -LiteralPath $ciPath -PathType Leaf) `
        -Message 'Required CI workflow is missing: .github/workflows/ci.yml'
    if (Test-Path -LiteralPath $ciPath -PathType Leaf) {
        $ciText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $ciPath))
        foreach ($requiredCiPattern in @(
            '(?m)^  pull_request:\s*$',
            '(?m)^  push:\s*$',
            '(?m)^permissions:\r?\n  contents: read\s*$',
            '(?m)^  cancel-in-progress: true\s*$',
            '(?m)^    runs-on: windows-latest\s*$',
            '(?m)^          persist-credentials: false\s*$',
            '(?m)^          global-json-file: global.json\s*$',
            '(?m)^          dotnet-version: 10\.0\.401\s*$',
            '(?m)^          cache: true\s*$',
            '(?m)^          cache-dependency-path: "\*\*/packages\.lock\.json"\s*$',
            '(?m)^  CODEXTRAY_TEST_USB_PORT: ""\s*$',
            '(?m)^  TESTINGPLATFORM_TELEMETRY_OPTOUT: "1"\s*$',
            'dotnet restore .*--locked-mode',
            'dotnet format .*--verify-no-changes --no-restore',
            'dotnet build .*--configuration Release --no-restore',
            'dotnet test .*--configuration Release --no-build --no-restore',
            'Category!=Integration&Category!=UsbHardware',
            '--results-directory TestResults',
            '--report-xunit-trx --report-xunit-trx-filename ci\.trx',
            'dotnet list .* package --vulnerable --include-transitive --format json --output-version 1 --no-restore',
            'ConvertFrom-Json',
            'Assert-SafeAuditReport',
            '(?m)^        if: failure\(\)\s*$',
            '(?m)^          path: TestResults/\*\*/\*\.trx\s*$',
            '(?m)^          retention-days: 7\s*$'
        )) {
            Assert-RepositoryContract `
                -Condition ($ciText -match $requiredCiPattern) `
                -Message "CI workflow is missing a required quality/safety gate: $requiredCiPattern"
        }
        $ciActions = [regex]::Matches($ciText, '(?m)^\s*uses: ([^\s#]+)')
        Assert-RepositoryContract `
            -Condition ($ciActions.Count -eq 3) `
            -Message 'CI must use the three declared checkout, setup-dotnet and upload-artifact actions.'
        foreach ($ciAction in $ciActions) {
            Assert-RepositoryContract `
                -Condition ($ciAction.Groups[1].Value -match '^actions/(checkout|setup-dotnet|upload-artifact)@[0-9a-f]{40}(?:[0-9a-f]{24})?$') `
                -Message "CI action must use an official immutable commit SHA: $($ciAction.Groups[1].Value)"
        }
        Assert-RepositoryContract `
            -Condition ($ciText -notmatch '(?i)(pull_request_target|secrets\s*\.|contents: write|packages: write|id-token: write|--logger|test-usb-screen|gh\s+pr\s+merge)') `
            -Message 'CI must not use privileged PR triggers, write permissions, secrets, VSTest-only logger or hardware/merge commands.'
        $orderedCiGates = @('dotnet restore ', 'dotnet format ', 'dotnet build ', 'dotnet test ', 'dotnet list ')
        $previousGatePosition = -1
        foreach ($orderedCiGate in $orderedCiGates) {
            $gatePosition = $ciText.IndexOf($orderedCiGate, [StringComparison]::Ordinal)
            Assert-RepositoryContract `
                -Condition ($gatePosition -gt $previousGatePosition) `
                -Message "CI gates must execute in restore/format/build/test/audit order: $orderedCiGate"
            $previousGatePosition = $gatePosition
        }
    }

    $releaseWorkflowPath = '.github/workflows/release.yml'
    Assert-RepositoryContract `
        -Condition (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf) `
        -Message 'Required draft-release workflow is missing: .github/workflows/release.yml'
    Assert-RepositoryContract `
        -Condition (Test-Path -LiteralPath '.github/release-notes/v1.3.0.md' -PathType Leaf) `
        -Message 'Required v1.3.0 release notes are missing.'
    if (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf) {
        $releaseText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $releaseWorkflowPath))
        foreach ($requiredReleasePattern in @(
            '(?m)^  push:\r?\n    tags:\r?\n      - "v\*\.\*\.\*"\s*$',
            '(?m)^permissions:\r?\n  contents: read\s*$',
            '(?m)^    permissions:\r?\n      contents: write\s*$',
            '(?m)^    runs-on: windows-latest\s*$',
            '(?m)^          persist-credentials: false\s*$',
            '(?m)^          dotnet-version: 10\.0\.401\s*$',
            '(?m)^          global-json-file: global.json\s*$',
            '(?m)^  CODEXTRAY_TEST_USB_PORT: ""\s*$',
            'Assert-ReleaseTag',
            'innosetup-7\.1\.0-x64\.exe',
            '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F',
            'Get-FileHash .* -Algorithm SHA256',
            'dotnet restore .*--locked-mode',
            'dotnet format .*--verify-no-changes --no-restore',
            'dotnet build .*--configuration Release --no-restore',
            'dotnet test .*--configuration Release --no-build --no-restore',
            'Category!=Integration&Category!=UsbHardware',
            '--report-xunit-trx --report-xunit-trx-filename release\.trx',
            'dotnet list .* package --vulnerable --include-transitive --format json --output-version 1 --no-restore',
            'Assert-SafeAuditReport',
            'scripts/build-release\.ps1 -AllowUnsigned -PureTestsOnly',
            'Assert-ReleaseAssets',
            'gh release create .*--draft --verify-tag',
            'gh release view .*--json ''isDraft,tagName,assets''',
            '(?m)^          GH_TOKEN: \$\{\{ github\.token \}\}\s*$',
            '(?m)^        if: failure\(\)\s*$',
            '(?m)^          path: TestResults/\*\*/\*\.trx\s*$',
            '(?m)^          retention-days: 7\s*$'
        )) {
            Assert-RepositoryContract `
                -Condition ($releaseText -match $requiredReleasePattern) `
                -Message "Release workflow is missing a required gate: $requiredReleasePattern"
        }
        $releaseActions = [regex]::Matches($releaseText, '(?m)^\s*uses: ([^\s#]+)')
        Assert-RepositoryContract `
            -Condition ($releaseActions.Count -eq 3) `
            -Message 'Release workflow must use only its three declared official actions.'
        foreach ($releaseAction in $releaseActions) {
            Assert-RepositoryContract `
                -Condition ($releaseAction.Groups[1].Value -match '^actions/(checkout|setup-dotnet|upload-artifact)@[0-9a-f]{40}(?:[0-9a-f]{24})?$') `
                -Message 'Release actions must use official immutable commit SHAs.'
        }
        Assert-RepositoryContract `
            -Condition ($releaseText -notmatch '(?i)(pull_request|workflow_dispatch|workflow_run|secrets\s*\.|\.p12|\.pfx|certificate|--logger|test-usb-screen|gh\s+release\s+(edit|upload|delete)|--clobber|--draft=false)') `
            -Message 'Release workflow must remain tag-only, draft-only, and free of signing material and unsafe overwrite commands.'
        $previousReleaseGatePosition = -1
        foreach ($releaseGate in @('Assert-ReleaseTag -', 'dotnet restore ', 'dotnet format ', 'dotnet build ', 'dotnet test ', 'dotnet list ', 'scripts/build-release.ps1 -AllowUnsigned -PureTestsOnly', 'Assert-ReleaseAssets -', 'gh release create ')) {
            $releaseGatePosition = $releaseText.IndexOf($releaseGate, [StringComparison]::Ordinal)
            Assert-RepositoryContract `
                -Condition ($releaseGatePosition -gt $previousReleaseGatePosition) `
                -Message "Release gates must execute in validation/quality/build/assets/draft order: $releaseGate"
            $previousReleaseGatePosition = $releaseGatePosition
        }
        & (Join-Path $PSScriptRoot 'test-release-workflow.ps1')
        Assert-RepositoryContract `
            -Condition ($LASTEXITCODE -eq 0) `
            -Message 'Release workflow behavioral tests failed.'
    }

    $releaseBuildPath = Join-Path $repositoryRoot 'scripts/build-release.ps1'
    $buildTokens = $null
    $buildParseErrors = $null
    $releaseBuildAst = [Management.Automation.Language.Parser]::ParseFile(
        $releaseBuildPath, [ref]$buildTokens, [ref]$buildParseErrors)
    $testArgumentAssignment = $releaseBuildAst.Find({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'testArguments'
    }, $true)
    $testInvocation = $releaseBuildAst.Find({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Invoke-Checked' -and
        $node.CommandElements[-1].Extent.Text -eq '$testArguments'
    }, $true)
    Assert-RepositoryContract `
        -Condition ($buildParseErrors.Count -eq 0 -and $null -ne $testArgumentAssignment -and $null -ne $testInvocation) `
        -Message 'Release build must expose test argument selection for full/default and opt-in pure tests.'
    if ($null -ne $testArgumentAssignment -and $null -ne $testInvocation) {
        $buildText = [IO.File]::ReadAllText($releaseBuildPath)
        $testSelection = [scriptblock]::Create($releaseBuildAst.ParamBlock.Extent.Text +
            [Environment]::NewLine + $buildText.Substring(
            $testArgumentAssignment.Extent.StartOffset,
            $testInvocation.Extent.EndOffset - $testArgumentAssignment.Extent.StartOffset))
        foreach ($pureOnly in @($false, $true)) {
            $actualArguments = & {
                param($Selection, $PureOnly)
                $solutionPath = 'CodexTray.sln'
                $dotnetPath = 'controlled-dotnet-boundary'
                function Invoke-Checked {
                    param([string]$FilePath, [string[]]$Arguments)
                    if ($FilePath -ne 'controlled-dotnet-boundary') {
                        throw 'Unexpected executable in test selection.'
                    }
                    $Arguments -join '|'
                }
                if ($PureOnly) { & $Selection -PureTestsOnly }
                else { & $Selection }
            } $testSelection $pureOnly
            $expectedArguments = 'test|CodexTray.sln|--configuration|Release|--no-build|--no-restore'
            if ($pureOnly) {
                $expectedArguments += '|--filter|Category!=Integration&Category!=UsbHardware'
            }
            Assert-RepositoryContract `
                -Condition ($actualArguments -ceq $expectedArguments) `
                -Message "Release build emitted incorrect test arguments for PureTestsOnly=$pureOnly."
        }
    }

    foreach ($helperPath in @('scripts/test-hook-install.ps1', 'scripts/test-usb-screen.ps1')) {
        $helperText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $helperPath))
        Assert-RepositoryContract `
            -Condition ($helperText -notmatch '\.tools\\dotnet\\dotnet\.exe' -and
                $helperText -match 'Get-Command dotnet\.exe' -and
                $helperText -match 'sdk\.version' -and
                $helperText -match '--version' -and
                $helperText -match '--configuration Release' -and
                $helperText -match '--no-build') `
            -Message "$helperPath must resolve and verify the installed pinned SDK and use the verified Release build."
    }

    & (Join-Path $PSScriptRoot 'test-validation-helpers.ps1')
    Assert-RepositoryContract `
        -Condition ($LASTEXITCODE -eq 0) `
        -Message 'Validation helper behavioral tests failed.'

    $trackedFiles = @(& git -c core.excludesFile=NUL ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed with exit code $LASTEXITCODE."
    }

    foreach ($trackedFile in $trackedFiles) {
        $normalizedPath = $trackedFile.Replace('\', '/')
        Assert-RepositoryContract `
            -Condition ($normalizedPath -notmatch '(?i)(^|/)(dist|artifacts|\.tools|bin|obj|TestResults)(/|$)') `
            -Message "Generated path is tracked: $trackedFile"
        Assert-RepositoryContract `
            -Condition ($normalizedPath -notmatch '(?i)\.(exe|p12|pfx|snk|key|pem)$') `
            -Message "Binary or signing material is tracked: $trackedFile"
    }

    $ignoredSentinels = @(
        '.tools/example.txt',
        'artifacts/example.txt',
        'dist/example.exe',
        'src/CodexTray/bin/example.dll',
        'src/CodexTray/obj/example.dll',
        'TestResults/example.trx',
        '.vs/example.txt',
        'example.user',
        'example.suo',
        'example.p12',
        'example.pfx',
        'example.snk',
        'example.key',
        'example.pem'
    )
    foreach ($sentinel in $ignoredSentinels) {
        & git -c core.excludesFile=NUL check-ignore --quiet --no-index -- $sentinel
        Assert-RepositoryContract `
            -Condition ($LASTEXITCODE -eq 0) `
            -Message ".gitignore does not exclude: $sentinel"
    }

    if (Test-Path -LiteralPath 'LICENSE' -PathType Leaf) {
        $licenseText = Get-Content -LiteralPath 'LICENSE' -Raw
        Assert-RepositoryContract `
            -Condition ($licenseText -match 'Copyright \(c\) 2026 Vasyl Danyliuk') `
            -Message 'LICENSE does not contain the approved copyright line.'
        Assert-RepositoryContract `
            -Condition ($licenseText -match 'Permission is hereby granted, free of charge') `
            -Message 'LICENSE does not contain the MIT permission grant.'
        Assert-RepositoryContract `
            -Condition ($licenseText -match 'THE SOFTWARE IS PROVIDED "AS IS"') `
            -Message 'LICENSE does not contain the MIT warranty disclaimer.'
    }

    if (Test-Path -LiteralPath 'global.json' -PathType Leaf) {
        $globalJson = Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json
        Assert-RepositoryContract `
            -Condition ($globalJson.sdk.version -eq '10.0.401') `
            -Message 'global.json must pin .NET SDK 10.0.401.'
        Assert-RepositoryContract `
            -Condition ($globalJson.sdk.rollForward -eq 'latestPatch') `
            -Message 'global.json must use rollForward latestPatch.'
        Assert-RepositoryContract `
            -Condition ($globalJson.sdk.allowPrerelease -eq $false) `
            -Message 'global.json must disable preview SDKs.'
        Assert-RepositoryContract `
            -Condition ($globalJson.test.runner -eq 'Microsoft.Testing.Platform') `
            -Message 'global.json must select Microsoft.Testing.Platform for .NET 10 tests.'
    }
    else {
        $failures.Add('Required toolchain file is missing: global.json')
    }

    $appProjectPath = 'src/CodexTray/CodexTray.csproj'
    $testProjectPath = 'tests/CodexTray.Tests/CodexTray.Tests.csproj'
    [xml]$appProject = Get-Content -LiteralPath $appProjectPath -Raw
    [xml]$testProject = Get-Content -LiteralPath $testProjectPath -Raw

    Assert-RepositoryContract `
        -Condition ([string]$appProject.Project.PropertyGroup.TargetFramework -eq 'net10.0-windows') `
        -Message 'The application must target net10.0-windows.'
    Assert-RepositoryContract `
        -Condition ([string]$testProject.Project.PropertyGroup.TargetFramework -eq 'net10.0-windows') `
        -Message 'The test project must target net10.0-windows.'

    $expectedPackages = [ordered]@{
        'System.IO.Ports' = @($appProject, '10.0.12')
        'Microsoft.NET.Test.Sdk' = @($testProject, '18.10.0')
        'xunit.v3' = @($testProject, '4.0.0')
        'xunit.runner.visualstudio' = @($testProject, '4.0.0')
        'xunit.analyzers' = @($testProject, '2.1.0')
        'coverlet.collector' = @($testProject, '10.0.1')
    }
    foreach ($packageName in $expectedPackages.Keys) {
        $projectAndVersion = $expectedPackages[$packageName]
        $actualVersion = Get-PackageVersion -Project $projectAndVersion[0] -PackageName $packageName
        $expectedVersion = $projectAndVersion[1]
        Assert-RepositoryContract `
            -Condition ($actualVersion -eq $expectedVersion) `
            -Message "Package $packageName must be pinned to $expectedVersion; found '$actualVersion'."
    }

    foreach ($lockFile in @(
        'src/CodexTray/packages.lock.json',
        'tests/CodexTray.Tests/packages.lock.json'
    )) {
        Assert-RepositoryContract `
            -Condition (Test-Path -LiteralPath $lockFile -PathType Leaf) `
            -Message "NuGet lock file is missing: $lockFile"
    }

    $bootstrapText = Get-Content -LiteralPath 'scripts/bootstrap-build-tools.ps1' -Raw
    Assert-RepositoryContract `
        -Condition ($bootstrapText -notmatch '(?i)Invoke-WebRequest') `
        -Message 'The bootstrap script must not download remote scripts with Invoke-WebRequest.'
    Assert-RepositoryContract `
        -Condition ($bootstrapText -notmatch '(?i)dotnet-install\.ps1') `
        -Message 'The bootstrap script must not download or execute dotnet-install.ps1.'
}
finally {
    Pop-Location
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("Repository contract failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Output 'Repository contract passed.'
