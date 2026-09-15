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
