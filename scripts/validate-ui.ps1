param(
    [ValidateSet('csharp', 'mixed')][string]$Preset = 'csharp',
    [string]$Executable,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $workspace
try {
    if (-not $NoBuild) {
        dotnet build src/SourceSearch.UiValidation -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not $Executable) { $Executable = Join-Path $workspace 'src/SourceSearch.UiValidation/bin/Release/net10.0-windows/SourceSearch.UiValidation.exe' }
    $reportDir = Join-Path $workspace "artifacts/reports/$Preset"
    $snapshot = (Get-Content -LiteralPath (Join-Path $reportDir 'build.json') -Raw | ConvertFrom-Json).SnapshotPath
    $queries = Join-Path $reportDir 'queries.json'
    $output = Join-Path $reportDir 'ui-timings.json'
    # Quote each path for Windows process argument parsing; input paths cannot contain quotes.
    $arguments = @('--ui-benchmark', '--snapshot', ('"' + $snapshot + '"'), '--queries', ('"' + $queries + '"'), '--output', ('"' + $output + '"'))
    $started = Get-Date
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Normal -PassThru -Wait
    if (-not (Test-Path -LiteralPath $output) -or (Get-Item -LiteralPath $output).LastWriteTime -lt $started) { throw 'Fresh UI report was not produced.' }
    $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $report.Valid -or -not $report.Passed) { Write-Error "UI validation failed; see $output"; exit 1 }
    Write-Output "UI validation passed: $output"
} finally { Pop-Location }
