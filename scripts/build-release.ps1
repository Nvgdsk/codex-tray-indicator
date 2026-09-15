[CmdletBinding()]
param(
    [switch]$AllowUnsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredDotNetVersion = '10.0.401'
$requiredInnoVersion = '7.1.0'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'CodexTray.sln'
$projectPath = Join-Path $repositoryRoot 'src\CodexTray\CodexTray.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\publish'
$distPath = Join-Path $repositoryRoot 'dist'
$installerPath = Join-Path $repositoryRoot 'installer\CodexTray.iss'
$checksumPath = Join-Path $distPath 'SHA256SUMS.txt'
$previousApplicationPath = Join-Path $distPath 'CodexTray.exe'

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

    $savedErrorActionPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 can promote native stderr to NativeCommandError.
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments
        $commandExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedErrorActionPreference
    }

    if ($commandExitCode -ne 0) {
        throw "$FilePath failed with exit code $commandExitCode."
    }
}

function Get-ExactTool {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string[]]$Candidates,
        [Parameter(Mandatory)][string]$RequiredVersion
    )

    foreach ($candidate in ($Candidates | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or
            -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $reportedVersion = (& $candidate --version 2>$null | Out-String).Trim()
            $commandExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }

        if ($commandExitCode -eq 0 -and $reportedVersion -eq $RequiredVersion) {
            return $candidate
        }
    }

    return $null
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

function Stop-PreviousDistApplication {
    if (-not (Test-Path -LiteralPath $previousApplicationPath -PathType Leaf)) {
        return
    }

    $runningProcesses = @(Get-Process -Name 'CodexTray' -ErrorAction SilentlyContinue | Where-Object {
        $processPath = $_.Path
        -not [string]::IsNullOrWhiteSpace($processPath) -and
        [IO.Path]::GetFullPath($processPath).Equals(
            [IO.Path]::GetFullPath($previousApplicationPath),
            [StringComparison]::OrdinalIgnoreCase)
    })
    if ($runningProcesses.Count -eq 0) {
        return
    }

    Invoke-Checked $previousApplicationPath @('--shutdown')
    foreach ($runningProcess in $runningProcesses) {
        Wait-Process -Id $runningProcess.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    $stillRunning = @(Get-Process -Name 'CodexTray' -ErrorAction SilentlyContinue | Where-Object {
        $processPath = $_.Path
        -not [string]::IsNullOrWhiteSpace($processPath) -and
        [IO.Path]::GetFullPath($processPath).Equals(
            [IO.Path]::GetFullPath($previousApplicationPath),
            [StringComparison]::OrdinalIgnoreCase)
    })
    if ($stillRunning.Count -gt 0) {
        throw "Refusing to replace a running dist application: $previousApplicationPath"
    }
}

$requiredSourceFiles = @($solutionPath, $projectPath, $installerPath)
$missingSourceFiles = @($requiredSourceFiles | Where-Object {
    -not (Test-Path -LiteralPath $_ -PathType Leaf)
})
if ($missingSourceFiles.Count -gt 0) {
    throw "Required source files are missing: $($missingSourceFiles -join ', ')"
}

[xml]$projectDocument = Get-Content -LiteralPath $projectPath -Raw
$versionNodes = @($projectDocument.Project.PropertyGroup.Version | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_)
})
if ($versionNodes.Count -ne 1) {
    throw 'CodexTray.csproj must contain exactly one Version element.'
}
$projectVersion = ([string]$versionNodes[0]).Trim()
if ($projectVersion -notmatch '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z') {
    throw "Project Version must use strict major.minor.patch format; found '$projectVersion'."
}

$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnetCommandPath = if ($null -eq $dotnetCommand) { $null } else { $dotnetCommand.Source }
$dotnetPath = Get-ExactTool -RequiredVersion $requiredDotNetVersion -Candidates @(
    $dotnetCommandPath,
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
    (Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe')
)
if ($null -eq $dotnetPath) {
    throw ".NET SDK $requiredDotNetVersion was not found. Run scripts/bootstrap-build-tools.ps1 first."
}

$innoCompiler = Get-ExactTool -RequiredVersion $requiredInnoVersion -Candidates @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
)
if ($null -eq $innoCompiler) {
    throw "Inno Setup $requiredInnoVersion was not found. Run scripts/bootstrap-build-tools.ps1 first."
}

Stop-PreviousDistApplication
Reset-RepositoryDirectory -Path $publishPath -ExpectedRelativePath 'artifacts\publish'
Reset-RepositoryDirectory -Path $distPath -ExpectedRelativePath 'dist'

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Invoke-Checked $dotnetPath @('restore', $solutionPath, '--locked-mode')
Invoke-Checked $dotnetPath @('restore', $projectPath, '--locked-mode', '--runtime', 'win-x64')
Invoke-Checked $dotnetPath @('build', $solutionPath, '--configuration', 'Release', '--no-restore')
Invoke-Checked $dotnetPath @(
    'test',
    $solutionPath,
    '--configuration', 'Release',
    '--no-build',
    '--no-restore')
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

Invoke-Checked $innoCompiler @("/DMyAppVersion=$projectVersion", $installerPath)

$setupPath = Join-Path $distPath 'CodexTraySetup.exe'
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw 'Inno Setup did not create dist\CodexTraySetup.exe.'
}

$releaseBinaryPaths = @($applicationPath, $setupPath)
$signatures = @($releaseBinaryPaths | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_
    $signer = if ($null -eq $signature.SignerCertificate) {
        $null
    }
    else {
        $signature.SignerCertificate.Subject
    }

    [pscustomobject]@{
        File = [IO.Path]::GetFileName($_)
        AuthenticodeStatus = $signature.Status
        Signer = $signer
    }
})

$invalidSignatures = @($signatures | Where-Object {
    $_.AuthenticodeStatus -ne [System.Management.Automation.SignatureStatus]::Valid -and
    $_.AuthenticodeStatus -ne [System.Management.Automation.SignatureStatus]::NotSigned
})
if ($invalidSignatures.Count -gt 0) {
    throw "Release files have invalid Authenticode signatures: $($invalidSignatures.File -join ', ')"
}
$unsignedFiles = @($signatures | Where-Object {
    $_.AuthenticodeStatus -eq [System.Management.Automation.SignatureStatus]::NotSigned
})
if ($unsignedFiles.Count -gt 0 -and -not $AllowUnsigned) {
    throw "Release files are unsigned: $($unsignedFiles.File -join ', '). Re-run with -AllowUnsigned to acknowledge an unsigned release."
}

$hashLines = @($releaseBinaryPaths | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToUpperInvariant()
    "$hash  $([IO.Path]::GetFileName($_))"
})
[IO.File]::WriteAllLines($checksumPath, $hashLines, [Text.UTF8Encoding]::new($false))

$expectedHashes = @{}
foreach ($line in (Get-Content -LiteralPath $checksumPath)) {
    if ($line -notmatch '\A([0-9A-F]{64})  (CodexTray(?:Setup)?\.exe)\z') {
        throw "Invalid checksum manifest line: '$line'"
    }
    if ($expectedHashes.ContainsKey($Matches[2])) {
        throw "Duplicate checksum manifest entry: '$($Matches[2])'"
    }
    $expectedHashes.Add($Matches[2], $Matches[1])
}
if ($expectedHashes.Count -ne $releaseBinaryPaths.Count) {
    throw 'Checksum manifest must contain exactly one entry for each release binary.'
}
foreach ($releaseBinaryPath in $releaseBinaryPaths) {
    $fileName = [IO.Path]::GetFileName($releaseBinaryPath)
    if (-not $expectedHashes.ContainsKey($fileName)) {
        throw "Checksum manifest is missing $fileName."
    }
    $verifiedHash = (Get-FileHash -LiteralPath $releaseBinaryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($verifiedHash -ne $expectedHashes[$fileName]) {
        throw "Checksum verification failed for $fileName."
    }
}

$expectedAssetNames = @('CodexTray.exe', 'CodexTraySetup.exe', 'SHA256SUMS.txt') | Sort-Object
$actualAssetNames = @(Get-ChildItem -LiteralPath $distPath -File | ForEach-Object Name | Sort-Object)
$assetDifferences = @(Compare-Object -ReferenceObject $expectedAssetNames -DifferenceObject $actualAssetNames)
if ($assetDifferences.Count -gt 0) {
    throw "Release output must contain exactly $($expectedAssetNames -join ', '); found: $($actualAssetNames -join ', ')"
}

[pscustomobject]@{
    Version = $projectVersion
    Application = $applicationPath
    Installer = $setupPath
    ChecksumManifest = $checksumPath
    Architecture = 'x64'
    Subsystem = 'Windows GUI'
    UnsignedReleaseExplicitlyAllowed = [bool]$AllowUnsigned
    ApplicationBytes = (Get-Item -LiteralPath $applicationPath).Length
    InstallerBytes = (Get-Item -LiteralPath $setupPath).Length
}
$signatures
$hashLines
