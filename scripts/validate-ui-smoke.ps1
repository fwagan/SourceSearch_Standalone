param([string]$Executable, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $workspace
try {
    if (-not $NoBuild) {
        dotnet build src/SourceSearch.Desktop -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not $Executable) { $Executable = Join-Path $workspace 'src/SourceSearch.Desktop/bin/Release/net10.0-windows/SourceSearch.Desktop.exe' }
    $fixture = Join-Path $workspace ('.tmp/ui-smoke/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    (1..1100 | ForEach-Object { "User row$_ GetUser UserService" }) | Set-Content -LiteralPath (Join-Path $fixture 'sample.cs') -Encoding utf8
    'GetUser UserService', 'User' | Set-Content -LiteralPath (Join-Path $fixture 'sample.js') -Encoding utf8
    'GetUser', 'UserService', 'User', 'User_ID', 'USER' | Set-Content -LiteralPath (Join-Path $fixture 'modes.txt') -Encoding utf8
    $output = Join-Path $workspace 'artifacts/reports/ui-smoke.json'
    $started = Get-Date
    $process = Start-Process -FilePath $Executable -ArgumentList @('--ui-smoke','--root',('"' + $fixture + '"'),'--output',('"' + $output + '"')) -WindowStyle Normal -PassThru -Wait
    if (-not (Test-Path -LiteralPath $output) -or (Get-Item -LiteralPath $output).LastWriteTime -lt $started) { throw 'Fresh smoke report was not produced.' }
    $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $report.Passed) { throw "UI smoke failed: $output" }
    Write-Output "UI smoke passed: $output"
} finally { Pop-Location }
