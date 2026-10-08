param(
    [string]$Root = 'C:\Coding\Ftrans\ftrans.main',
    [ValidateSet('csharp', 'mixed')][string]$Preset = 'csharp',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $workspace
try {
    if (-not $NoBuild) {
        dotnet build src/SourceSearch.Validation -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $reportDir = Join-Path $workspace "artifacts/reports/$Preset"
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    $hardware = [ordered]@{ Cpu = 'Unavailable'; MemoryBytes = $null; Disks = @(); Error = $null }
    try {
        $hardware.Cpu = @(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name)
        $hardware.MemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
        $hardware.Disks = @(Get-CimInstance Win32_DiskDrive | Select-Object Model, MediaType, Size)
    } catch { $hardware.Error = $_.Exception.Message }
    $hardware | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportDir 'hardware.json') -Encoding utf8
    $dll = Join-Path $workspace 'src/SourceSearch.Validation/bin/Release/net10.0/SourceSearch.Validation.dll'
    $buildReport = Join-Path $reportDir 'build.json'
    dotnet $dll build --root $Root --preset $Preset --index-root (Join-Path $workspace "artifacts/indexes/$Preset") --report $buildReport
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $snapshot = (Get-Content -LiteralPath $buildReport -Raw | ConvertFrom-Json).SnapshotPath
    $queries = Join-Path $reportDir 'queries.json'
    dotnet $dll queries --snapshot $snapshot --output $queries
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet $dll verify --snapshot $snapshot --queries $queries --output (Join-Path $reportDir 'correctness.json')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet $dll benchmark --snapshot $snapshot --queries $queries --warmups 5 --iterations 30 --output (Join-Path $reportDir 'timings.json')
    exit $LASTEXITCODE
} finally { Pop-Location }
