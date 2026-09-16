[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

foreach ($helper in @('test-usb-screen.ps1', 'test-hook-install.ps1')) {
    foreach ($case in @(
        @{ Name='Pinned SDK'; Sdk='10.0.401'; TestExit=0; Accept=$true; Tests=1; Wsl=2 },
        @{ Name='Old SDK rejected'; Sdk='8.0.408'; TestExit=0; Accept=$false; Tests=0; Wsl=0 },
        @{ Name='Test failure propagated'; Sdk='10.0.401'; TestExit=1; Accept=$false; Tests=1; Wsl=1 }
    )) {
        $observation = & {
            param($Helper, $Fixture, $RepositoryRoot)
            $testCalls = [Collections.Generic.List[object]]::new()
            $wslCalls = [Collections.Generic.List[object]]::new()
            $savedHome = $env:DOTNET_CLI_HOME
            $savedPort = $env:CODEXTRAY_TEST_USB_PORT
            $savedOrientation = $env:CODEXTRAY_TEST_USB_ORIENTATION
            $savedSkip = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
            $savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
            function Get-Command {
                param([string]$Name, [string]$ErrorAction)
                if ($Name -ne 'dotnet.exe') { throw 'Unexpected tool lookup.' }
                [pscustomobject]@{ Source='fixture-dotnet' }
            }
            function fixture-dotnet {
                if ($args[0] -eq '--version') { $global:LASTEXITCODE=0; return $Fixture.Sdk }
                $testCalls.Add([pscustomobject]@{
                    Arguments=($args -join '|'); Directory=(Get-Location).Path
                    Port=$env:CODEXTRAY_TEST_USB_PORT; Orientation=$env:CODEXTRAY_TEST_USB_ORIENTATION
                })
                $global:LASTEXITCODE = $Fixture.TestExit
            }
            function wsl.exe {
                if ($args.Count -ne 6 -or ($args[0..4] -join '|') -cne '-d|Ubuntu|--exec|sh|-lc' -or
                    $args[5] -notlike "find /tmp -maxdepth 1 -type d -name 'codex-tray-tests-*' -print | sort") {
                    throw 'Unexpected WSL command.'
                }
                $wslCalls.Add($args)
                $global:LASTEXITCODE=0
                return '/tmp/codex-tray-tests-existing-fixture'
            }
            $accepted = $false
            $failureMessage = $null
            Push-Location ([IO.Path]::GetTempPath())
            try {
                $callerDirectory = (Get-Location).Path
                $scriptPath = Join-Path $RepositoryRoot ('scripts/' + $Helper)
                try {
                    if ($Helper -eq 'test-usb-screen.ps1') { & $scriptPath -Port COM95 -Orientation Landscape | Out-Null }
                    else { & $scriptPath | Out-Null }
                    $accepted = $true
                }
                catch { $failureMessage = $_.Exception.Message }
                $restored = (Get-Location).Path -eq $callerDirectory -and
                    $env:DOTNET_CLI_HOME -eq $savedHome -and $env:CODEXTRAY_TEST_USB_PORT -eq $savedPort -and
                    $env:CODEXTRAY_TEST_USB_ORIENTATION -eq $savedOrientation -and
                    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE -eq $savedSkip -and
                    $env:DOTNET_CLI_TELEMETRY_OPTOUT -eq $savedTelemetry
                [pscustomobject]@{ Accepted=$accepted; Calls=@($testCalls.ToArray()); WslCalls=$wslCalls.Count; Restored=$restored; Error=$failureMessage }
            }
            finally {
                Pop-Location
                $env:DOTNET_CLI_HOME=$savedHome
                $env:CODEXTRAY_TEST_USB_PORT=$savedPort
                $env:CODEXTRAY_TEST_USB_ORIENTATION=$savedOrientation
                $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE=$savedSkip
                $env:DOTNET_CLI_TELEMETRY_OPTOUT=$savedTelemetry
            }
        } $helper $case $repositoryRoot
        if ($observation.Accepted -ne $case.Accept -or $observation.Calls.Count -ne $case.Tests -or -not $observation.Restored) {
            throw "Helper behavior failed: $helper / $($case.Name); accepted=$($observation.Accepted), calls=$($observation.Calls.Count), restored=$($observation.Restored), error=$($observation.Error)"
        }
        foreach ($call in $observation.Calls) {
            $target = if ($helper -eq 'test-usb-screen.ps1') { 'CodexTray.sln' } else { 'tests\CodexTray.Tests\CodexTray.Tests.csproj' }
            $filter = if ($helper -eq 'test-usb-screen.ps1') { 'Category=UsbHardware' } else { 'FullyQualifiedName~WslHookConfigStoreIntegrationTests' }
            $expected = 'test|' + (Join-Path $repositoryRoot $target) + '|--configuration|Release|--no-build|--no-restore|--filter|' + $filter
            if ($call.Arguments -cne $expected -or $call.Directory -ne $repositoryRoot) {
                throw "Helper emitted incorrect command or selected SDK from wrong directory: $helper"
            }
            if ($helper -eq 'test-usb-screen.ps1' -and ($call.Port -cne 'COM95' -or $call.Orientation -cne 'Landscape')) {
                throw 'USB helper did not forward explicit opt-in device settings.'
            }
        }
        if ($helper -eq 'test-usb-screen.ps1' -and $observation.WslCalls -ne 0) { throw 'USB helper unexpectedly invoked WSL.' }
        if ($helper -eq 'test-hook-install.ps1' -and $observation.WslCalls -ne $case.Wsl) { throw 'Hook helper performed unexpected WSL access.' }
        Write-Output "Helper case passed: $helper / $($case.Name)"
    }
}
Write-Output 'Validation helper behavioral tests passed (no real WSL/USB access).'
exit 0
