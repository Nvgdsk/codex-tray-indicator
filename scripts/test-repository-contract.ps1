[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$failures = [Collections.Generic.List[string]]::new()

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
        Assert-RepositoryContract `
            -Condition ($securityText.Contains('1.3.x') -and $securityText.Contains('../../security/advisories/new')) `
            -Message 'SECURITY.md must declare supported versions and a private advisory reporting route.'
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
        Assert-RepositoryContract `
            -Condition ($issueConfigText -match '(?m)^contact_links: \[\]\s*$' -and $issueConfigText.Contains('SECURITY.md')) `
            -Message 'Issue config must retain empty contacts until owner confirmation and reference SECURITY.md.'
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
