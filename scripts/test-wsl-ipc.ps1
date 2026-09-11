[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\artifacts\publish\CodexTray.exe'),
    [string]$Distribution = 'Ubuntu',
    [ValidateRange(1, 10000)]
    [int]$Cycles = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$publishDirectory = Split-Path -Parent $resolvedExecutable
$trayProcess = $null

function Invoke-WindowsClient {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start $resolvedExecutable."
        }
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = (($output | Out-String).Trim())
            Error = (($errorOutput | Out-String).Trim())
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-WslClient {
    param(
        [Parameter(Mandatory)][string]$WslExecutable,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    Invoke-WslProcess -Arguments (@($WslExecutable) + $Arguments)
}

function Invoke-WslProcess {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$StandardInput
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'wsl.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in (@('-d', $Distribution, '--exec') + $Arguments)) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Unable to start wsl.exe.'
        }
        if ($PSBoundParameters.ContainsKey('StandardInput')) {
            $process.StandardInput.Write($StandardInput)
        }
        $process.StandardInput.Close()
        $output = $process.StandardOutput.ReadToEnd()
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = (($output | Out-String).Trim())
            Error = (($errorOutput | Out-String).Trim())
        }
    }
    finally {
        $process.Dispose()
    }
}

function Assert-ClientResult {
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][int]$ExpectedExitCode,
        [string]$ExpectedOutput,
        [Parameter(Mandatory)][string]$Operation
    )

    if ($Result.ExitCode -ne $ExpectedExitCode) {
        throw "$Operation returned exit code $($Result.ExitCode); expected $ExpectedExitCode. Output: $($Result.Output)"
    }

    if ($PSBoundParameters.ContainsKey('ExpectedOutput') -and $Result.Output -ne $ExpectedOutput) {
        throw "$Operation returned '$($Result.Output)'; expected '$ExpectedOutput'."
    }
}

function Wait-ForState {
    param(
        [Parameter(Mandatory)][string]$Expected,
        [int]$TimeoutMilliseconds = 10000
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $query = Invoke-WindowsClient -Arguments @('--query-state')
        if ($query.ExitCode -eq 0 -and $query.Output -eq $Expected) {
            return
        }
        Start-Sleep -Milliseconds 50
    }

    throw "Tray did not report $Expected within $TimeoutMilliseconds ms."
}

function Get-RuntimeSnapshot {
    $snapshot = @{}
    $roots = [Collections.Generic.List[string]]::new()
    $roots.Add($publishDirectory)

    Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'CodexTray*' -Force -ErrorAction SilentlyContinue |
        ForEach-Object { $roots.Add($_.FullName) }
    $dotnetExtraction = Join-Path $env:TEMP '.net'
    if (Test-Path -LiteralPath $dotnetExtraction) {
        Get-ChildItem -LiteralPath $dotnetExtraction -Directory -Filter 'CodexTray*' -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $roots.Add($_.FullName) }
    }

    foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $snapshot[$_.FullName] = "$($_.Length):$($_.LastWriteTimeUtc.Ticks)"
        }
    }
    return $snapshot
}

function Assert-SnapshotsEqual {
    param(
        [Parameter(Mandatory)][hashtable]$Before,
        [Parameter(Mandatory)][hashtable]$After
    )

    $changed = [Collections.Generic.List[string]]::new()
    foreach ($path in ($Before.Keys + $After.Keys | Sort-Object -Unique)) {
        if (-not $Before.ContainsKey($path) -or
            -not $After.ContainsKey($path) -or
            $Before[$path] -ne $After[$path]) {
            $changed.Add($path)
        }
    }

    if ($changed.Count -gt 0) {
        throw "Runtime file set changed during hook traffic: $($changed -join ', ')"
    }

    $forbidden = $After.Keys | Where-Object {
        $name = [IO.Path]::GetFileName($_)
        $name -match '^(?i:status)' -or $name -match '(?i:\.(log|json|db)$)'
    }
    if ($forbidden) {
        throw "Forbidden runtime state/log files found: $($forbidden -join ', ')"
    }
}

$preexisting = Invoke-WindowsClient -Arguments @('--query-state')
if ($preexisting.ExitCode -eq 0) {
    throw 'A Codex Tray instance already owns the pipe. Exit it before running this proof.'
}

try {
    $trayProcess = Start-Process -FilePath $resolvedExecutable -PassThru -WindowStyle Hidden
    Wait-ForState -Expected 'Inactive'

    $wslPathResult = Invoke-WslProcess -Arguments @('wslpath', '-a', '-u', '--', $resolvedExecutable)
    if ($wslPathResult.ExitCode -ne 0) {
        throw "wslpath failed for $Distribution with exit code $($wslPathResult.ExitCode): $($wslPathResult.Error)"
    }
    $wslExecutable = $wslPathResult.Output
    if ([string]::IsNullOrWhiteSpace($wslExecutable)) {
        throw 'wslpath returned an empty executable path.'
    }

    $busy = Invoke-WslClient -WslExecutable $wslExecutable -Arguments @('--hook-test', 'busy')
    Assert-ClientResult $busy 0 -Operation 'Initial WSL Busy send'
    Assert-ClientResult (Invoke-WslClient $wslExecutable @('--query-state')) 0 'Busy' 'Initial WSL Busy query'

    $ready = Invoke-WslClient -WslExecutable $wslExecutable -Arguments @('--hook-test', 'ready')
    Assert-ClientResult $ready 0 -Operation 'Initial WSL Ready send'
    Assert-ClientResult (Invoke-WslClient $wslExecutable @('--query-state')) 0 'Ready' 'Initial WSL Ready query'

    $before = Get-RuntimeSnapshot
    for ($index = 1; $index -le $Cycles; $index++) {
        Assert-ClientResult (Invoke-WslClient $wslExecutable @('--hook-test', 'busy')) 0 -Operation "Busy send $index"
        Assert-ClientResult (Invoke-WslClient $wslExecutable @('--hook-test', 'ready')) 0 -Operation "Ready send $index"
    }
    Assert-ClientResult (Invoke-WslClient $wslExecutable @('--query-state')) 0 'Ready' 'Final WSL query'
    $after = Get-RuntimeSnapshot
    Assert-SnapshotsEqual -Before $before -After $after

    Assert-ClientResult (Invoke-WindowsClient @('--shutdown')) 0 -Operation 'First shutdown'
    if (-not $trayProcess.WaitForExit(5000)) {
        throw 'Tray did not exit within five seconds after shutdown.'
    }
    $trayProcess.Dispose()
    $trayProcess = $null

    $trayProcess = Start-Process -FilePath $resolvedExecutable -PassThru -WindowStyle Hidden
    Wait-ForState -Expected 'Inactive'
    Assert-ClientResult (Invoke-WindowsClient @('--shutdown')) 0 -Operation 'Second shutdown'
    if (-not $trayProcess.WaitForExit(5000)) {
        throw 'Restarted tray did not exit within five seconds after shutdown.'
    }
    $trayProcess.Dispose()
    $trayProcess = $null

    $failOpenPayload = '{"hook_event_name":"SessionEnd","session_id":"offline-proof"}'
    $failOpenWatch = [Diagnostics.Stopwatch]::StartNew()
    $failOpenResult = Invoke-WslProcess `
        -Arguments @($wslExecutable, '--hook', '--integration-id', 'codex-tray-indicator-v1') `
        -StandardInput $failOpenPayload
    $failOpenWatch.Stop()
    if ($failOpenResult.ExitCode -ne 0) {
        throw "Offline hook returned exit code $($failOpenResult.ExitCode); expected 0."
    }
    if ($failOpenWatch.ElapsedMilliseconds -ge 750) {
        throw "Offline hook took $($failOpenWatch.ElapsedMilliseconds) ms; expected under 750 ms."
    }

    [pscustomobject]@{
        Executable = $resolvedExecutable
        WslExecutable = $wslExecutable
        Cycles = $Cycles
        FinalState = 'Ready'
        RestartState = 'Inactive'
        OfflineHookMilliseconds = $failOpenWatch.ElapsedMilliseconds
        RuntimeFilesChanged = 0
    }
}
finally {
    if ($null -ne $trayProcess) {
        $null = Invoke-WindowsClient -Arguments @('--shutdown')
        if (-not $trayProcess.WaitForExit(3000)) {
            $trayProcess.Kill($true)
            $trayProcess.WaitForExit()
        }
        $trayProcess.Dispose()
    }
}
