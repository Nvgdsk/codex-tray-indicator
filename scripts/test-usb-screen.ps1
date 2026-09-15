[CmdletBinding()]
param(
    [ValidatePattern('^COM[1-9][0-9]*$')]
    [string]$Port = 'COM3',
    [ValidateSet('Portrait', 'ReversePortrait', 'Landscape', 'ReverseLandscape')]
    [string]$Orientation = 'Portrait'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetPath = Join-Path $repositoryRoot '.tools\dotnet\dotnet.exe'
$previousPort = $env:CODEXTRAY_TEST_USB_PORT
$previousOrientation = $env:CODEXTRAY_TEST_USB_ORIENTATION
$previousCliHome = $env:DOTNET_CLI_HOME
try {
    $env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.tools\dotnet-home'
    $env:CODEXTRAY_TEST_USB_PORT = $Port.ToUpperInvariant()
    $env:CODEXTRAY_TEST_USB_ORIENTATION = $Orientation
    & $dotnetPath test (Join-Path $repositoryRoot 'CodexTray.sln') --configuration Release --no-restore --filter 'Category=UsbHardware'
    if ($LASTEXITCODE -ne 0) { throw 'USB screen hardware test failed. Close the vendor app and set USB screen → Off in the tray before retrying.' }
    Write-Output "Status and animation RGB565 frames written to $Port. Previews: $repositoryRoot\artifacts\usb-screen-previews"
    Write-Output 'Confirm the visible colors and text on the physical screen; serial writes have no display acknowledgement.'
}
finally {
    $env:CODEXTRAY_TEST_USB_PORT = $previousPort
    $env:CODEXTRAY_TEST_USB_ORIENTATION = $previousOrientation
    $env:DOTNET_CLI_HOME = $previousCliHome
}
