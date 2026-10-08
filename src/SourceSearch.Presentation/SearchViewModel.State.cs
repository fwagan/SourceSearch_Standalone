using SourceSearch.Core;
namespace SourceSearch.Presentation;
public sealed partial class SearchViewModel
{
    public BuildReport? Report => controller.CurrentReport;
    public BuildProgress? Progress { get; private set; }
    public SearchRequest? AppliedRequest { get; private set; }
    public bool HasPendingChanges { get; private set; }
    public string IndexError { get; private set; } = "";
    public string QueryError { get; private set; } = "";
    public TimeSpan? LastSearchElapsed { get; private set; }
    public bool CanEdit => !isBuilding && !isSwitching && !closed;
    public bool CanSearch => CanEdit && !stale && Report is not null && string.Equals(IndexedRoot, RootPath, StringComparison.OrdinalIgnoreCase);
    public bool CanSubmit => CanSearch && QueryText.Length > 0 && !QueryText.Contains('\n') && !QueryText.Contains('\r');
    public bool CanMore => CanSubmit && !isSearching && !HasPendingChanges && cursor is not null && Results.Count < 1000;
    public bool IsBuilding => isBuilding;
    public bool IsSearching => isSearching;
    public bool ShowBuildButton => !isBuilding;
    public bool ShowQueryCancel => isSearching && !isBuilding;
    public bool HasDetails => Report is not null && IndexedRoot.Length > 0;
    public bool ShowEmpty => Results.Count == 0;
    public bool HasNotice => NoticeText.Length > 0;
    public string FolderName => Path.GetFileName(RootPath) is { Length: > 0 } name ? name : RootPath;
    public string BranchLabel => Identity.Label;
    public string BuildLabel => HasDetails ? "重建索引" : "建立索引";
    public bool IsWarning => Phase is IndexPhase.Failed or IndexPhase.Cancelled or IndexPhase.Stale;
    public string StatusText => isSwitching ? "切换目录中" : isBuilding ? "建立中" : Phase switch
    { IndexPhase.Ready => "索引就绪", IndexPhase.Failed => "建立失败", IndexPhase.Cancelled => "建立已取消", IndexPhase.Stale => "需要重建", _ => "尚未建立" };
    public string IndexSummary => isBuilding ? Progress is { } p ? $"已处理 {p.FilesProcessed:N0} 个文件 · 已索引 {p.FilesIndexed:N0} 个 · 跳过 {p.FilesSkipped:N0} 个" : "正在准备索引…" :
        HasDetails && Report is { } r ? $"{r.Files.Count:N0} 个文件 · {r.IndexedLines:N0} 行 · 建立于 {r.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" : "当前文件夹尚无可搜索的索引";
    public string SnapshotSource => HasDetails ? $"{IndexedRoot} · {IndexedIdentity?.Label} · {Report!.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" : "无索引";
    public string NoticeText => stale ? $"目录的分支或 HEAD 已变化，请重建索引。原索引：{SnapshotSource}" :
        IndexError.Length > 0 ? IndexError + (CanSearch ? $" 正在使用旧快照：{SnapshotSource}" : " 当前无可搜索索引。") : "";
    public string QueryHint => QueryText.Contains('\n') || QueryText.Contains('\r') ? "暂不支持跨行查询。" :
        HasPendingChanges ? "条件已更改，按 Enter 搜索。列表保留上次提交的结果。" : "Enter 搜索 · Ctrl+F 聚焦 · 默认包含匹配，不区分大小写";
    public string AppliedLabel => AppliedRequest is { } r ? $"“{r.Text}” · {(r.Mode == MatchMode.WholeWord ? "全字匹配" : "包含匹配")}" : "";
    public string AppliedDescription => AppliedRequest is { } r ? $"{AppliedLabel}；包含：{string.Join(',', r.Filter.Include)}；排除：{string.Join(',', r.Filter.Exclude)}" : "";
    public string ResultCountText => isSearching ? "搜索中…" : AppliedRequest is null ? "" : $"已显示 {Results.Count:N0} 条" +
        (Results.Count >= 1000 ? " · 达到显示上限" : HasPendingChanges || QueryError.Length > 0 ? "" : cursor is null ? " · 已到末尾" : "");
    public string EmptyTitle => isBuilding ? "正在建立索引" : isSwitching ? "正在切换文件夹" : stale ? "分支已变化" : !CanSearch ? "先为这个文件夹建立索引" :
        isSearching ? "正在搜索" : QueryError.Length > 0 ? "搜索未完成" : AppliedRequest is null ? "索引已就绪" : "没有匹配结果";
    public string EmptyDescription => !CanSearch ? "索引建立完成后，即可搜索代码和文本。" : AppliedRequest is null ? "输入搜索文本并按 Enter。" : "检查搜索文本，或调整全字匹配和文件类型条件。";
    public string TimingText => QueryError.Length > 0 ? QueryError : Results.Count >= 1000 ? "达到显示上限，请缩小搜索范围。" :
        "结果来自索引快照 · 文件修改后需重建" + (LastSearchElapsed is { } elapsed ? $" · 用时 {elapsed.TotalMilliseconds:F1} ms" : "");
    private void NotifyState()
    {
        foreach (string property in StateProperties) PropertyChanged?.Invoke(this, new(property));
        BuildCommand.NotifyCanExecuteChanged(); SearchCommand.NotifyCanExecuteChanged(); MoreCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
    private static readonly string[] StateProperties = [nameof(Report), nameof(Progress), nameof(Identity), nameof(IndexedIdentity), nameof(IndexedRoot), nameof(Phase), nameof(AppliedRequest), nameof(HasPendingChanges), nameof(IndexError), nameof(QueryError), nameof(LastSearchElapsed), nameof(CanEdit), nameof(CanSearch), nameof(CanSubmit), nameof(CanMore), nameof(IsBuilding), nameof(IsSearching), nameof(ShowBuildButton), nameof(ShowQueryCancel), nameof(HasDetails), nameof(ShowEmpty), nameof(HasNotice), nameof(FolderName), nameof(BranchLabel), nameof(BuildLabel), nameof(IsWarning), nameof(StatusText), nameof(IndexSummary), nameof(SnapshotSource), nameof(NoticeText), nameof(QueryHint), nameof(AppliedLabel), nameof(AppliedDescription), nameof(ResultCountText), nameof(EmptyTitle), nameof(EmptyDescription), nameof(TimingText)];
}
