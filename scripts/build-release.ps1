[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'CodexTray.sln'
$projectPath = Join-Path $repositoryRoot 'src\CodexTray\CodexTray.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\publish'
$distPath = Join-Path $repositoryRoot 'dist'
$installerPath = Join-Path $repositoryRoot 'installer\CodexTray.iss'
$dotnetPath = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'

function Reset-RepositoryDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedRelativePath
    )

    $resolvedTarget = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $expectedTarget = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $ExpectedRelativePath)).TrimEnd('\')
    if (-not $resolvedTarget.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset unexpected path: $resolvedTarget"
    }
    if (-not $resolvedTarget.StartsWith(
        $repositoryRoot.TrimEnd('\') + '\',
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside the repository: $resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $resolvedTarget -Force
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Get-PeShape {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 512 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "$Path is not a valid PE image."
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 96 -ge $bytes.Length -or
        $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45) {
        throw "$Path has an invalid PE header."
    }

    [pscustomobject]@{
        Machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
        OptionalMagic = [BitConverter]::ToUInt16($bytes, $peOffset + 24)
        Subsystem = [BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68)
    }
}

if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $projectPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installerPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw 'Required source or build-tool files are missing. Run scripts/bootstrap-build-tools.ps1 first.'
}

Reset-RepositoryDirectory -Path $publishPath -ExpectedRelativePath 'artifacts\publish'
Reset-RepositoryDirectory -Path $distPath -ExpectedRelativePath 'dist'

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Invoke-Checked $dotnetPath @('restore', $solutionPath, '--runtime', 'win-x64')
Invoke-Checked $dotnetPath @('test', $solutionPath, '--configuration', 'Release', '--no-restore')
Invoke-Checked $dotnetPath @(
    'publish',
    $projectPath,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $publishPath)

$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'CodexTray.exe') {
    throw "Publish output must contain only CodexTray.exe; found: $($publishedFiles.Name -join ', ')"
}
$applicationPath = Join-Path $distPath 'CodexTray.exe'
Copy-Item -LiteralPath $publishedFiles[0].FullName -Destination $applicationPath

$shape = Get-PeShape -Path $applicationPath
if ($shape.Machine -ne 0x8664) {
    throw ('CodexTray.exe is not PE x64 (machine 0x{0:X4}).' -f $shape.Machine)
}
if ($shape.OptionalMagic -ne 0x020B) {
    throw ('CodexTray.exe is not PE32+ (magic 0x{0:X4}).' -f $shape.OptionalMagic)
}
if ($shape.Subsystem -ne 2) {
    throw "CodexTray.exe is not a Windows GUI subsystem executable (subsystem $($shape.Subsystem))."
}

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) }
$innoCompiler = $innoCandidates | Select-Object -First 1
if ($null -eq $innoCompiler) {
    throw 'ISCC.exe was not found. Run scripts/bootstrap-build-tools.ps1 first.'
}
Invoke-Checked $innoCompiler @($installerPath)

$setupPath = Join-Path $distPath 'CodexTraySetup.exe'
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw 'Inno Setup did not create dist\CodexTraySetup.exe.'
}

$signatures = @($applicationPath, $setupPath) | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_
    [pscustomobject]@{
        File = [IO.Path]::GetFileName($_)
        AuthenticodeStatus = $signature.Status
        Signer = $signature.SignerCertificate?.Subject
    }
}

$hashLines = @($applicationPath, $setupPath) | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToUpperInvariant()
    "$hash  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines(
    (Join-Path $distPath 'SHA256SUMS.txt'),
    $hashLines,
    [Text.UTF8Encoding]::new($false))

$distFiles = @(Get-ChildItem -LiteralPath $distPath -File)
if ($distFiles.Count -ne 3) {
    throw "Release output must contain exactly three files; found: $($distFiles.Name -join ', ')"
}

[pscustomobject]@{
    Application = $applicationPath
    Installer = $setupPath
    Architecture = 'x64'
    Subsystem = 'Windows GUI'
    ApplicationBytes = (Get-Item -LiteralPath $applicationPath).Length
    InstallerBytes = (Get-Item -LiteralPath $setupPath).Length
}
$signatures
$hashLines
