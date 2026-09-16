[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workflowPath = Join-Path $repositoryRoot '.github/workflows/release.yml'

function Get-WorkflowRunBlock {
    param([string]$StepName)
    $lines = [IO.File]::ReadAllLines($workflowPath)
    $stepHeader = '      - name: ' + $StepName
    $stepPosition = [Array]::IndexOf($lines, $stepHeader)
    if ($stepPosition -lt 0) { throw "Workflow step not found: $StepName" }
    $code = [Collections.Generic.List[string]]::new()
    $inRun = $false
    for ($i = $stepPosition + 1; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line.StartsWith('      - name: ')) { break }
        if (-not $inRun) {
            if ($line -eq '        run: |') { $inRun = $true }
            continue
        }
        if ($line.StartsWith('          ')) { $code.Add($line.Substring(10)) }
        elseif ([string]::IsNullOrWhiteSpace($line)) { $code.Add('') }
        else { break }
    }
    if (-not $inRun -or $code.Count -eq 0) { throw "Missing run block: $StepName" }
    return ($code -join [Environment]::NewLine)
}

function Import-WorkflowFunction {
    param([string]$Code, [string]$Name)
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Code, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name
    }, $true)
    if ($null -eq $definition) { throw "Workflow function not found: $Name" }
    return [scriptblock]::Create($definition.Extent.Text)
}

function Assert-WorkflowCase {
    param([string]$Name, [bool]$Accept, [scriptblock]$Run)
    $accepted = $false
    try { & $Run | Out-Null; $accepted = $true }
    catch { if ($Accept) { throw } }
    if ($accepted -ne $Accept) { throw "Unexpected acceptance: $Name" }
    Write-Output "Release case passed: $Name"
}

if (-not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
    throw 'Release workflow is missing.'
}

$tagCode = Get-WorkflowRunBlock 'Validate tag and release notes'
. (Import-WorkflowFunction $tagCode 'Assert-ReleaseTag')
foreach ($case in @(
    @{ Tag='v1.3.0'; Ref='tag'; Version='1.3.0'; Accept=$true },
    @{ Tag='v0.0.0'; Ref='tag'; Version='0.0.0'; Accept=$true },
    @{ Tag='v1.3.1'; Ref='tag'; Version='1.3.0'; Accept=$false },
    @{ Tag='v1.3.0'; Ref='branch'; Version='1.3.0'; Accept=$false },
    @{ Tag='v01.3.0'; Ref='tag'; Version='01.3.0'; Accept=$false },
    @{ Tag='v1.3.0-beta'; Ref='tag'; Version='1.3.0'; Accept=$false },
    @{ Tag='v1.3.0/../../other'; Ref='tag'; Version='1.3.0'; Accept=$false },
    @{ Tag='v1.3.0'; Ref='tag'; Version='1.3.0.0'; Accept=$false },
    @{ Tag="v1.3.0`n"; Ref='tag'; Version='1.3.0'; Accept=$false }
)) {
    Assert-WorkflowCase -Name "Tag $($case.Tag), ref $($case.Ref), version $($case.Version)" -Accept $case.Accept -Run {
        Assert-ReleaseTag -Tag $case.Tag -RefType $case.Ref -ProjectVersion $case.Version
    }
}

$assetCode = Get-WorkflowRunBlock 'Verify exact release assets and checksums'
. (Import-WorkflowFunction $assetCode 'Assert-ReleaseAssets')
$fixturePath = Join-Path $repositoryRoot ('artifacts/workflow-tests/' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixturePath -Force
try {
    foreach ($asset in @(@('CodexTray.exe', 'app-fixture'), @('CodexTraySetup.exe', 'setup-fixture'))) {
        [IO.File]::WriteAllText((Join-Path $fixturePath $asset[0]), $asset[1], [Text.UTF8Encoding]::new($false))
    }
    $manifestPath = Join-Path $fixturePath 'SHA256SUMS.txt'
    $manifest = @('CodexTray.exe', 'CodexTraySetup.exe') | ForEach-Object {
        (Get-FileHash -LiteralPath (Join-Path $fixturePath $_) -Algorithm SHA256).Hash + '  ' + $_
    }
    [IO.File]::WriteAllLines($manifestPath, [string[]]$manifest)
    Assert-WorkflowCase 'Exact assets with matching hashes' $true { Assert-ReleaseAssets -DistPath $fixturePath }
    . (Import-WorkflowFunction (Get-WorkflowRunBlock 'Install hash-pinned Inno Setup') 'Install-VerifiedInnoSetup')
    $installerFixturePath = Join-Path $fixturePath 'CodexTraySetup.exe'
    $installerFixtureHash = (Get-FileHash -LiteralPath $installerFixturePath -Algorithm SHA256).Hash
    foreach ($case in @(
        @{ Name='Verified installer executes once'; Hash=$installerFixtureHash; Exit=0; Accept=$true; Calls=1 },
        @{ Name='Wrong installer hash blocks execution'; Hash=('0' * 64); Exit=0; Accept=$false; Calls=0 },
        @{ Name='Installer failure stops release'; Hash=$installerFixtureHash; Exit=1; Accept=$false; Calls=1 }
    )) {
        $observation = & {
            param($Fixture, $InstallerPath)
            $calls = [Collections.Generic.List[object]]::new()
            function Start-Process {
                param([string]$FilePath, [string[]]$ArgumentList, [switch]$Wait, [switch]$PassThru, [string]$WindowStyle)
                if ($FilePath -cne $InstallerPath -or
                    ($ArgumentList -join '|') -cne '/VERYSILENT|/SUPPRESSMSGBOXES|/NORESTART|/CURRENTUSER|/SP-' -or
                    -not $Wait -or -not $PassThru -or $WindowStyle -cne 'Hidden') {
                    throw 'Unexpected installer process arguments.'
                }
                $calls.Add($FilePath)
                [pscustomobject]@{ ExitCode=$Fixture.Exit }
            }
            $accepted = $false
            try {
                Install-VerifiedInnoSetup -InstallerPath $InstallerPath -ExpectedHash $Fixture.Hash
                $accepted = $true
            }
            catch { }
            [pscustomobject]@{ Accepted=$accepted; Calls=$calls.Count }
        } $case $installerFixturePath
        if ($observation.Accepted -ne $case.Accept -or $observation.Calls -ne $case.Calls) {
            throw "Installer verification failed: $($case.Name)"
        }
        Write-Output "Release case passed: $($case.Name)"
    }
    foreach ($case in @(
        @{ Name='Missing checksum entry'; Lines=@($manifest[0]) },
        @{ Name='Duplicate checksum entry'; Lines=@($manifest[0], $manifest[0]) },
        @{ Name='Wrong checksum'; Lines=@(('0' * 64) + '  CodexTray.exe', $manifest[1]) },
        @{ Name='Manifest path traversal'; Lines=@(('0' * 64) + '  ../CodexTray.exe', $manifest[1]) },
        @{ Name='Malformed checksum'; Lines=@('invalid  CodexTray.exe', $manifest[1]) },
        @{ Name='Blank extra line'; Lines=@($manifest[0], $manifest[1], '') }
    )) {
        [IO.File]::WriteAllLines($manifestPath, [string[]]$case.Lines)
        Assert-WorkflowCase $case.Name $false { Assert-ReleaseAssets -DistPath $fixturePath }
    }
    [IO.File]::WriteAllLines($manifestPath, [string[]]$manifest)
    $extraPath = Join-Path $fixturePath 'unexpected.txt'
    [IO.File]::WriteAllText($extraPath, 'unexpected')
    Assert-WorkflowCase 'Extra file' $false { Assert-ReleaseAssets -DistPath $fixturePath }
    Remove-Item -LiteralPath $extraPath
    $extraDirectory = Join-Path $fixturePath 'unexpected-directory'
    $null = New-Item -ItemType Directory -Path $extraDirectory
    Assert-WorkflowCase 'Extra directory' $false { Assert-ReleaseAssets -DistPath $fixturePath }
    Remove-Item -LiteralPath $extraDirectory
    $installerFixture = Join-Path $fixturePath 'CodexTraySetup.exe'
    Remove-Item -LiteralPath $installerFixture
    Assert-WorkflowCase 'Missing installer' $false { Assert-ReleaseAssets -DistPath $fixturePath }
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixturePath)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/workflow-tests')).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notmatch '\A[0-9a-f]{32}\z') {
        throw 'Refusing to remove an unexpected fixture directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}

$draftScript = [scriptblock]::Create((Get-WorkflowRunBlock 'Create and inspect unpublished draft'))
$savedRef = $env:GITHUB_REF_NAME
$savedRepository = $env:GITHUB_REPOSITORY
try {
    $env:GITHUB_REF_NAME = 'v1.3.0'
    $env:GITHUB_REPOSITORY = 'fixture-owner/codex-tray-indicator'
    foreach ($case in @(
        @{ Name='Draft with exact assets'; CreateExit=0; ViewExit=0; Draft=$true; Tag='v1.3.0'; Names=@('CodexTray.exe','CodexTraySetup.exe','SHA256SUMS.txt'); Accept=$true },
        @{ Name='Create command fails'; CreateExit=1; ViewExit=0; Draft=$true; Tag='v1.3.0'; Names=@('CodexTray.exe','CodexTraySetup.exe','SHA256SUMS.txt'); Accept=$false },
        @{ Name='Inspection fails'; CreateExit=0; ViewExit=1; Draft=$true; Tag='v1.3.0'; Names=@('CodexTray.exe','CodexTraySetup.exe','SHA256SUMS.txt'); Accept=$false },
        @{ Name='Unexpected published state'; CreateExit=0; ViewExit=0; Draft=$false; Tag='v1.3.0'; Names=@('CodexTray.exe','CodexTraySetup.exe','SHA256SUMS.txt'); Accept=$false },
        @{ Name='Wrong inspected tag'; CreateExit=0; ViewExit=0; Draft=$true; Tag='v1.3.1'; Names=@('CodexTray.exe','CodexTraySetup.exe','SHA256SUMS.txt'); Accept=$false },
        @{ Name='Missing uploaded checksum'; CreateExit=0; ViewExit=0; Draft=$true; Tag='v1.3.0'; Names=@('CodexTray.exe','CodexTraySetup.exe'); Accept=$false }
    )) {
        Assert-WorkflowCase $case.Name $case.Accept {
            & {
                param($Fixture, $DraftScript)
                function gh {
                    $arguments = @($args)
                    if ($arguments[0] -ne 'release') { throw 'Unexpected GitHub CLI command.' }
                    if ($arguments[1] -eq 'create') {
                        $expected = @('release','create','v1.3.0','--draft','--verify-tag','--repo','fixture-owner/codex-tray-indicator','--title','Codex Tray Indicator v1.3.0','--notes-file','.github/release-notes/v1.3.0.md','dist/CodexTraySetup.exe','dist/CodexTray.exe','dist/SHA256SUMS.txt')
                        if (($arguments -join '|') -cne ($expected -join '|')) { throw 'Incorrect draft creation arguments.' }
                        $global:LASTEXITCODE = $Fixture.CreateExit
                        return 'https://example.invalid/draft-fixture'
                    }
                    if ($arguments[1] -eq 'view') {
                        $expected = @('release','view','v1.3.0','--repo','fixture-owner/codex-tray-indicator','--json','isDraft,tagName,assets')
                        if (($arguments -join '|') -cne ($expected -join '|')) { throw 'Incorrect draft inspection arguments.' }
                        $global:LASTEXITCODE = $Fixture.ViewExit
                        return (@{ isDraft=$Fixture.Draft; tagName=$Fixture.Tag; assets=@($Fixture.Names | ForEach-Object { @{name=$_} }) } | ConvertTo-Json -Depth 5)
                    }
                    throw 'Workflow attempted a forbidden release mutation.'
                }
                & $DraftScript
            } $case $draftScript
        }
    }
}
finally {
    $env:GITHUB_REF_NAME = $savedRef
    $env:GITHUB_REPOSITORY = $savedRepository
}

Write-Output 'Release workflow behavioral tests passed (no real GitHub mutation).'
exit 0
