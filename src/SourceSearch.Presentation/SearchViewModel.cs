using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SourceSearch.Core;
namespace SourceSearch.Presentation;

/// <summary>Call from the owning UI context; async continuations preserve that context.</summary>
public sealed class SearchViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly SearchController controller;
    private readonly string indexRoot;
    private readonly ISearchPresentation presentation;
    private SearchCursor? cursor;
    private long version, buildVersion;
    private bool isBuilding, isSearching, closed;
    private string indexedRoot = "";
    private string rootPath = @"C:\Coding\Ftrans\ftrans.main", queryText = "", includeText = "", excludeText = "";
    private string statusText = "尚未建立索引。", timingText = "默认包含匹配；全字匹配使用完整单词边界。";
    private bool wholeWord, includeEnabled, excludeEnabled;
    public event PropertyChangedEventHandler? PropertyChanged;
    public SearchViewModel(SearchController controller, string indexRoot, ISearchPresentation? presentation = null)
    {
        this.controller = controller; this.indexRoot = indexRoot; this.presentation = presentation ?? new ImmediatePresentation();
        BuildCommand = new(() => BuildAsync(), () => CanEdit, Error);
        SearchCommand = new(() => SearchAsync(), () => CanSearch, Error);
        MoreCommand = new(() => SearchAsync(true), () => CanMore, Error);
        CancelCommand = new(() => { Cancel(); return Task.CompletedTask; }, () => !closed && (isBuilding || isSearching), Error);
    }
    public AsyncCommand BuildCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand MoreCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public string RootPath { get => rootPath; set => Set(ref rootPath, value); }
    public string QueryText { get => queryText; set => SetQuery(ref queryText, value); }
    public string IncludeText { get => includeText; set => SetQuery(ref includeText, value); }
    public string ExcludeText { get => excludeText; set => SetQuery(ref excludeText, value); }
    public bool WholeWord { get => wholeWord; set => SetQuery(ref wholeWord, value); }
    public bool IncludeEnabled { get => includeEnabled; set => SetQuery(ref includeEnabled, value); }
    public bool ExcludeEnabled { get => excludeEnabled; set => SetQuery(ref excludeEnabled, value); }
    public string StatusText { get => statusText; private set => Set(ref statusText, value); }
    public string TimingText { get => timingText; private set => Set(ref timingText, value); }
    public bool CanEdit => !isBuilding && !closed;
    public bool CanSearch => !isBuilding && !closed && controller.CurrentReport is not null;
    public bool CanMore => CanSearch && !isSearching && cursor is not null && Results.Count < 1000;
    public bool IsBuilding => isBuilding;
    public ResultCollection Results { get; } = new();

    public async Task BuildAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (isBuilding) throw new InvalidOperationException("Index is already building.");
        string root = RootPath; long generation = ++buildVersion;
        InvalidateQuery(); isBuilding = true; NotifyState(); StatusText = "建立索引中…";
        try
        {
            var progress = new Progress<BuildProgress>(value =>
            {
                if (isBuilding && generation == buildVersion && !closed)
                    StatusText = $"建立中：已处理 {value.FilesProcessed:N0}，已索引 {value.FilesIndexed:N0}，跳过 {value.FilesSkipped:N0}";
            });
            await controller.RebuildAsync(new(new(root, CorpusPresets.Mixed), indexRoot), progress, ct);
            indexedRoot = root; Results.Clear(); ShowReady();
        }
        catch (OperationCanceledException) { StatusText = "建立已取消。" + PreviousSnapshotText(); }
        catch (Exception error) { StatusText = "建立失败：" + error.Message + PreviousSnapshotText(); }
        finally { isBuilding = false; buildVersion++; NotifyState(); }
    }
    public async Task OpenSnapshotAsync(string path, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (isBuilding) throw new InvalidOperationException("Index is already building.");
        InvalidateQuery(); isBuilding = true; NotifyState();
        try { await controller.OpenSnapshotAsync(path, ct); indexedRoot = "快照 " + path; Results.Clear(); ShowReady(); }
        finally { isBuilding = false; NotifyState(); }
    }
    public async Task<SearchExecution> SearchAsync(bool more = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!CanSearch) throw new InvalidOperationException("Index is not ready.");
        if (more && !CanMore) throw new InvalidOperationException("No further UI page available.");
        // Read effective conditions without rewriting editable/disabled filter values.
        var request = new SearchRequest(QueryText, WholeWord ? MatchMode.WholeWord : MatchMode.Contains,
            ExtensionFilter.Parse(IncludeEnabled, IncludeText, ExcludeEnabled, ExcludeText));
        long operation = ++version;
        var after = more ? cursor : null;
        cursor = null;
        isSearching = true; NotifyState();
        SearchReply? reply = null;
        try
        {
            var elapsed = await presentation.MeasureAsync(async () =>
            {
                reply = await controller.SearchAsync(request, after, Math.Min(100, more ? 1000 - Results.Count : 100), ct);
                EnsureCurrent(operation, reply.RequestId);
                Results.Publish(reply.Page.Hits.Select(ResultRow.From), more);
            }, ct);
            EnsureCurrent(operation, reply!.RequestId);
            cursor = reply.Page.Next;
            TimingText = $"已显示 {Results.Count:N0} 行；核心 {reply.Page.Elapsed.TotalMilliseconds:F1} ms；呈现检查点 {elapsed.TotalMilliseconds:F1} ms。" +
                (Results.Count >= 1000 ? "已达显示上限 1000 行，不代表全部命中数。" : reply.Page.Exhausted ? "已到结果末尾。" : "可加载下一批。");
            return new(reply.Page, elapsed);
        }
        catch (Exception error) when (operation != version || closed)
        { throw new OperationCanceledException("Superseded query.", error); }
        finally { if (operation == version) { isSearching = false; NotifyState(); } }
    }
    public void Cancel()
    {
        if (closed) return;
        controller.Cancel(); version++; cursor = null; isSearching = false; NotifyState();
    }
    private void EnsureCurrent(long operation, long requestId)
    {
        if (closed || operation != version || requestId != controller.CurrentRequestId) throw new OperationCanceledException("Superseded query.");
    }
    private void InvalidateQuery()
    { version++; controller.CancelQuery(); cursor = null; isSearching = false; NotifyState(); }
    private void ShowReady()
    {
        var report = controller.CurrentReport!;
        StatusText = $"索引就绪：{indexedRoot}；{report.Files.Count:N0} 文件 / {report.IndexedLines:N0} 行，跳过 {report.Issues.Count:N0} 项；建立于 {report.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}。显示上次建立索引时的内容。";
    }
    private string PreviousSnapshotText() => controller.CurrentReport is null ? "尚无可搜索索引。" : $"仍可搜索上次成功建立的快照：{indexedRoot}。";
    private void Error(Exception error) { if (!closed) TimingText = "操作失败：" + error.Message; }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
    private void SetQuery<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (Set(ref field, value, name)) InvalidateQuery(); }
    private void NotifyState()
    {
        foreach (string property in new[] { nameof(CanEdit), nameof(CanSearch), nameof(CanMore), nameof(IsBuilding) }) PropertyChanged?.Invoke(this, new(property));
        BuildCommand.NotifyCanExecuteChanged(); SearchCommand.NotifyCanExecuteChanged(); MoreCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
    public async ValueTask DisposeAsync()
    {
        closed = true; version++; cursor = null; isSearching = false; NotifyState();
        await controller.DisposeAsync();
    }
    private sealed class ImmediatePresentation : ISearchPresentation
    {
        public async Task<TimeSpan> MeasureAsync(Func<Task> updateResults, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); var timer = Stopwatch.StartNew(); await updateResults(); ct.ThrowIfCancellationRequested(); return timer.Elapsed; }
    }
}
