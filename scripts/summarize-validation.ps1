param([string]$CoreRevision = '6c292aa', [string]$SearchRevision)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SearchRevision) { $SearchRevision = (& git -c ('safe.directory=' + $workspace.Replace('\','/')) -C $workspace rev-parse HEAD).Trim() }
$destination = Join-Path $workspace 'docs/validation/search-core'
foreach ($preset in @('csharp', 'mixed')) {
    $reports = Join-Path $workspace "artifacts/reports/$preset"
    $build = Get-Content (Join-Path $reports 'build.json') -Raw | ConvertFrom-Json
    $core = Get-Content (Join-Path $reports 'timings.json') -Raw | ConvertFrom-Json
    $correct = Get-Content (Join-Path $reports 'correctness.json') -Raw | ConvertFrom-Json
    $queries = Get-Content (Join-Path $reports 'queries.json') -Raw | ConvertFrom-Json
    $hardware = Get-Content (Join-Path $reports 'hardware.json') -Raw | ConvertFrom-Json
    $before = Get-Content (Join-Path $reports 'timings-before-optimization.json') -Raw | ConvertFrom-Json
    $table = @($queries | ForEach-Object {
        $query = $_
        $verification = $correct.Results | Where-Object Name -eq $query.Name
        $timing = $core.Timings | Where-Object Name -eq $query.Name
        [ordered]@{ Name=$query.Name; Text=$query.Request.Text; Mode=$query.Request.Mode; Group=$query.Group;
            Include=$query.Request.Filter.Include; Exclude=$query.Request.Filter.Exclude;
            Frequency= if ($verification.ActualLines -eq 0) {'zero'} elseif ($verification.ActualLines -le 100) {'low'} elseif ($verification.ActualLines -lt 10000) {'medium'} else {'high'};
            Correct=$verification.Passed; AllHitLines=$verification.ActualLines; Timing=$timing }
    })
    $summary = [ordered]@{
        Preset=$preset; BuildRevision=$CoreRevision; SearchRevision=$SearchRevision; SnapshotId=$build.Report.SnapshotId;
        Files=$build.Report.Files.Count; IndexedBytes=($build.Report.Files | Measure-Object Bytes -Sum).Sum;
        Lines=$build.Report.IndexedLines; BuildElapsed=$build.Report.Elapsed; IndexBytes=$build.ActualIndexBytes;
        BuildPeakWorkingSetBytes=$build.PeakWorkingSetBytes; SearchPeakWorkingSetBytes=$core.PeakWorkingSetBytes;
        Issues=@($build.Report.Issues | Group-Object Reason | Select-Object Count,Name);
        LargestFiles=@($build.Report.Files | Sort-Object Bytes -Descending | Select-Object -First 10 @{n='Name';e={[IO.Path]::GetFileName($_.FullPath)}},Extension,Bytes,LineCount);
        Environment=$core.Environment; Hardware=$hardware; IndexOpenMs=$core.IndexOpenMs; FirstQueryMs=$core.FirstQueryMs;
        CorrectnessPassed=$correct.Passed; PerformancePassed=$core.Passed; Warmups=5; Iterations=30; PageSize=100;
        BeforeOptimization=$before.Timings; Queries=$table
    }
    $summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $destination "$preset-summary.json") -Encoding utf8
    $ui = Get-Content (Join-Path $reports 'ui-timings.json') -Raw | ConvertFrom-Json
    [ordered]@{ Preset=$preset; Revision=$SearchRevision; SnapshotId=$build.Report.SnapshotId; Report=$ui } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $destination "$preset-ui-summary.json") -Encoding utf8
    $markdown = [Collections.Generic.List[string]]::new()
    $markdown.Add("# $preset 逐查询结果")
    $markdown.Add('')
    $markdown.Add('单位 ms；first 是各组在本次序列中预热前的一次，其他组可能已访问同一候选行，不能视为完全冷缓存。core/UI 均为各组 5 次预热后的 30 次测量，P95 使用 nearest-rank。')
    $markdown.Add('')
    $markdown.Add('| 查询 | 模式 | 全部命中行 | core first | core P50 | core P95 | core max | UI first | UI P50 | UI P95 | UI max |')
    $markdown.Add('|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($entry in $table) {
        $timing = $entry.Timing; $render = $ui.Timings | Where-Object Name -eq $entry.Name
        $label = $entry.Name + ' / ' + $entry.Text
        $mode = if ($entry.Mode -eq 0) { '包含' } else { '全字' }
        $markdown.Add(('| {0} | {1} | {2} | {3:F2} | {4:F2} | {5:F2} | {6:F2} | {7:F2} | {8:F2} | {9:F2} | {10:F2} |' -f $label,$mode,$entry.AllHitLines,$timing.FirstInSequenceMs,$timing.P50Ms,$timing.P95Ms,$timing.MaxMs,$render.FirstInSequenceMs,$render.P50Ms,$render.P95Ms,$render.MaxMs))
    }
    $markdown | Set-Content (Join-Path $destination "$preset-query-results.md") -Encoding utf8
}
