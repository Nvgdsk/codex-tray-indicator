$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $repositoryRoot '.tools'
$dotnetRoot = Join-Path $toolRoot 'dotnet'
$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
$installerScript = Join-Path $toolRoot 'dotnet-install.ps1'
$env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerScript
    & $installerScript -Channel 8.0 -InstallDir $dotnetRoot -NoPath
    if (-not $?) {
        throw 'dotnet-install.ps1 failed.'
    }
}

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)

$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    $winget = Get-Command winget.exe -ErrorAction Stop
    & $winget.Source install --id JRSoftware.InnoSetup --exact --scope user --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup installation failed with exit code $LASTEXITCODE."
    }
    $innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $innoCompiler) {
    throw 'ISCC.exe was not found after Inno Setup installation.'
}

& $dotnetExe --version
Write-Output "ISCC=$innoCompiler"
