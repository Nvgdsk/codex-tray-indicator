[CmdletBinding()]
param(
    [ValidateSet('Ubuntu')]
    [string]$Distribution = 'Ubuntu'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'CodexTray.sln'
$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $solution -PathType Leaf) -or $null -eq $dotnetCommand) {
    throw 'Run scripts/bootstrap-build-tools.ps1 before this validation script.'
}
$dotnet = $dotnetCommand.Source

function Get-TestDirectories {
    $output = & wsl.exe -d $Distribution --exec sh -lc `
        "find /tmp -maxdepth 1 -type d -name 'codex-tray-tests-[0-9a-f]*' -print | sort"
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect isolated test directories in $Distribution."
    }
    return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$previousCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
Push-Location $repositoryRoot
try {
    $expectedSdk = (Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json).sdk.version
    $actualSdk = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $expectedSdk) {
        throw "This validation requires .NET SDK $expectedSdk; found '$actualSdk'."
    }
    $before = @(Get-TestDirectories)
    $env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    & $dotnet test (Join-Path $repositoryRoot 'tests\CodexTray.Tests\CodexTray.Tests.csproj') `
        --configuration Release --no-build --no-restore `
        --filter 'FullyQualifiedName~WslHookConfigStoreIntegrationTests'
    if ($LASTEXITCODE -ne 0) {
        throw "Isolated WSL hook installation test failed with exit code $LASTEXITCODE."
    }
    $after = @(Get-TestDirectories)
    $leaked = @($after | Where-Object { $_ -notin $before })
    if ($leaked.Count -gt 0) {
        throw "The integration test leaked WSL directories: $($leaked -join ', ')"
    }
    [pscustomobject]@{
        Distribution = $Distribution
        AtomicInstallTest = 'Passed'
        TemporaryDirectoriesLeaked = 0
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    Pop-Location
}
