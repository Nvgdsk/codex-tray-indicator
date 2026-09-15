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
}
finally {
    Pop-Location
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("Repository contract failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Output 'Repository contract passed.'
