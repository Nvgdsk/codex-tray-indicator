[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$requiredDotNetVersion = '10.0.401'
$requiredInnoVersion = '7.1.0'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolRoot = Join-Path $repositoryRoot '.tools'
$env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Get-DotNetExecutable {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    $commandSource = if ($null -eq $command) { $null } else { $command.Source }
    $candidates = @(
        $commandSource,
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path $toolRoot 'dotnet\dotnet.exe')
    ) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $version = & $candidate --version 2>$null
            $candidateExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
        if ($candidateExitCode -eq 0 -and $version -eq $requiredDotNetVersion) {
            return $candidate
        }
    }

    return $null
}

function Get-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    ) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    }

    foreach ($candidate in $candidates) {
        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $version = & $candidate --version 2>$null
            $candidateExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
        if ($candidateExitCode -eq 0 -and ([string]$version).Trim() -eq $requiredInnoVersion) {
            return $candidate
        }
    }

    return $null
}

function Install-ExactWingetPackage {
    param(
        [Parameter(Mandatory)][string]$PackageId,
        [Parameter(Mandatory)][string]$Version
    )

    $winget = Get-Command winget.exe -ErrorAction Stop
    & $winget.Source install `
        --id $PackageId `
        --version $Version `
        --exact `
        --silent `
        --accept-package-agreements `
        --accept-source-agreements `
        --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install $PackageId $Version with exit code $LASTEXITCODE."
    }
}

$dotnetExecutable = Get-DotNetExecutable
if ($null -eq $dotnetExecutable) {
    Install-ExactWingetPackage -PackageId 'Microsoft.DotNet.SDK.10' -Version $requiredDotNetVersion
    $dotnetExecutable = Get-DotNetExecutable
}
if ($null -eq $dotnetExecutable) {
    throw ".NET SDK $requiredDotNetVersion was not found after installation."
}

$innoCompiler = Get-InnoCompiler
if ($null -eq $innoCompiler) {
    Install-ExactWingetPackage -PackageId 'JRSoftware.InnoSetup.7' -Version $requiredInnoVersion
    $innoCompiler = Get-InnoCompiler
}
if ($null -eq $innoCompiler) {
    throw "Inno Setup $requiredInnoVersion was not found after installation."
}

Write-Output "DOTNET=$dotnetExecutable"
Write-Output "DOTNET_VERSION=$requiredDotNetVersion"
Write-Output "ISCC=$innoCompiler"
Write-Output "INNO_VERSION=$requiredInnoVersion"
