using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SourceSearch.Core;
namespace SourceSearch.Presentation;

/// <summary>Owns one selected folder. Call on the owning UI context; continuations preserve it.</summary>
public sealed partial class SearchViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly SearchController controller;
    private readonly string indexRoot;
    private readonly ISearchPresentation presentation;
    private readonly IWorkspaceIdentityReader identityReader;
    private readonly IIndexCache? cache;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? buildCancellation, queryCancellation;
    private SearchCursor? cursor;
    private long version, buildVersion, workspaceVersion;
    private bool isBuilding, isSearching, isSwitching, closed, stale;
    private string rootPath = @"C:\Coding\Ftrans\ftrans.main", queryText = "", includeText = "", excludeText = "";
    private bool wholeWord, includeEnabled, excludeEnabled;
    public event PropertyChangedEventHandler? PropertyChanged;
    public SearchViewModel(SearchController controller, string indexRoot, ISearchPresentation? presentation = null, IWorkspaceIdentityReader? identityReader = null, IIndexCache? cache = null)
    {
        this.controller = controller; this.indexRoot = indexRoot;
        this.presentation = presentation ?? new ImmediatePresentation();
        this.identityReader = identityReader ?? new WorkspaceIdentityReader();
        this.cache = cache;
        BuildCommand = new(() => BuildAsync(), () => CanEdit, Error);
        FullRebuildCommand = new(() => BuildCoreAsync(true), () => CanEdit, Error);
        SearchCommand = new(() => SearchAsync(), () => CanSubmit, Error);
        MoreCommand = new(() => SearchAsync(true), () => CanMore, Error);
        CancelCommand = new(() => { Cancel(); return Task.CompletedTask; }, () => !closed && (isBuilding || isSearching), Error);
    }
    public AsyncCommand BuildCommand { get; }
    public AsyncCommand FullRebuildCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand MoreCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public string RootPath { get => rootPath; init => rootPath = NormalizePath(value); }
    public string QueryText { get => queryText; set => SetQuery(ref queryText, value); }
    public string IncludeText { get => includeText; set => SetQuery(ref includeText, value); }
    public string ExcludeText { get => excludeText; set => SetQuery(ref excludeText, value); }
    public bool WholeWord { get => wholeWord; set => SetQuery(ref wholeWord, value); }
    public bool IncludeEnabled { get => includeEnabled; set => SetQuery(ref includeEnabled, value); }
    public bool ExcludeEnabled { get => excludeEnabled; set => SetQuery(ref excludeEnabled, value); }
    public ResultCollection Results { get; } = new();

    public Task BuildAsync(CancellationToken ct = default) => BuildCoreAsync(false, ct);
    private async Task BuildCoreAsync(bool forceRebuild, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!CanEdit) throw new InvalidOperationException("Workspace is busy.");
        string root = RootPath; long generation = ++buildVersion;
        workspaceVersion++; identityRefresh = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        buildCancellation = cancellation;
        InvalidateQuery(); isBuilding = true; Phase = IndexPhase.Building; IndexError = ""; Progress = null; CacheSummary = "正在检查文件并准备索引…"; NotifyState();
        try
        {
            var before = await identityReader.ReadAsync(root, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); ApplyIdentity(before);
            var progress = new Progress<BuildProgress>(value =>
            {
                if (isBuilding && generation == buildVersion && !closed)
                {
                    Progress = value;
                    PropertyChanged?.Invoke(this, new(nameof(Progress)));
                    PropertyChanged?.Invoke(this, new(nameof(IndexSummary)));
                }
            });
            if (cache is not null && before.Kind == WorkspaceKind.Git)
            {
                BuildResult? prepared = null;
                await controller.ReplaceWithAsync(async token => prepared = await cache.PrepareAsync(new(root,CorpusPresets.Mixed),before,forceRebuild,progress,token), cancellation.Token);
                CacheSummary = prepared!.Reused ? "已复用缓存 · 文件未变化" : prepared.Report.Update is { } u ?
                    $"{(forceRebuild ? "完整重建" : "索引更新")} · 复用 {u.Reused:N0} · 新增 {u.Added:N0} · 修改 {u.Changed:N0} · 移除 {u.Removed:N0}" : "缓存已建立";
            }
            else
            {
                await controller.RebuildAsync(new(new(root, CorpusPresets.Mixed), indexRoot), progress, cancellation.Token);
                CacheSummary = "本次会话索引 · 不自动复用缓存";
            }
            IndexedRoot = root; IndexedIdentity = before; stale = false; ClearResults();
            Phase = IndexPhase.Ready;
        }
        catch (OperationCanceledException) { if (!closed) { Phase = stale ? IndexPhase.Stale : IndexPhase.Cancelled; IndexError = "建立已取消。"; } }
        catch (Exception error) { if (!closed) { Phase = stale ? IndexPhase.Stale : IndexPhase.Failed; IndexError = "建立失败：" + error.Message; } }
        finally
        {
            try
            {
                // Also check failed/cancelled builds: HEAD may have changed while the builder ran.
                if (!closed)
                {
                    var after = await identityReader.ReadAsync(root, lifetime.Token);
                    if (!closed) ApplyIdentity(after);
                }
            }
            catch (OperationCanceledException) when (closed) { }
            finally { isBuilding = false; buildCancellation = null; buildVersion++; if (!closed) NotifyState(); }
        }
    }
    /// <summary>Validation entry: an explicit snapshot is not claimed to match the current Git HEAD.</summary>
    public async Task OpenSnapshotAsync(string path, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!CanEdit) throw new InvalidOperationException("Workspace is busy.");
        workspaceVersion++; identityRefresh = null;
        InvalidateQuery(); isBuilding = true; NotifyState();
        try
        {
            await controller.OpenSnapshotAsync(path, ct);
            Set(ref rootPath, NormalizePath(controller.CurrentSourceRoot!), nameof(RootPath));
            IndexedRoot = RootPath; IndexedIdentity = new(WorkspaceKind.Unknown, Error: "显式验证快照；未验证当前分支。");
            Identity = IndexedIdentity; stale = false; Phase = IndexPhase.Ready; IndexError = ""; ClearResults();
        }
        finally { isBuilding = false; NotifyState(); }
    }
    public async Task<SearchExecution> SearchAsync(bool more = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!CanSubmit) throw new InvalidOperationException("Index or query is not ready.");
        if (more && !CanMore) throw new InvalidOperationException("No further UI page available.");
        var request = new SearchRequest(QueryText, WholeWord ? MatchMode.WholeWord : MatchMode.Contains,
            ExtensionFilter.Parse(IncludeEnabled, IncludeText, ExcludeEnabled, ExcludeText));
        long operation = ++version; var after = more ? cursor : null; cursor = null;
        queryCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        queryCancellation = cancellation; isSearching = true; QueryError = ""; NotifyState();
        SearchReply? reply = null;
        try
        {
            var elapsed = await presentation.MeasureAsync(async () =>
            {
                reply = await controller.SearchAsync(request, after, Math.Min(100, more ? 1000 - Results.Count : 100), cancellation.Token);
                EnsureCurrent(operation, reply.RequestId);
                AppliedRequest = request; HasPendingChanges = false;
                Results.Publish(reply.Page.Hits.Select(ResultRow.From), more);
                // Publish the visible result identity and empty overlay before the render checkpoint.
                foreach (string property in new[] { nameof(ShowEmpty), nameof(AppliedRequest), nameof(AppliedLabel), nameof(AppliedDescription), nameof(HasPendingChanges), nameof(QueryHint), nameof(EmptyDescription) })
                    PropertyChanged?.Invoke(this, new(property));
            }, cancellation.Token);
            EnsureCurrent(operation, reply!.RequestId);
            cursor = reply.Page.Next; LastSearchElapsed = elapsed;
            return new(reply.Page, elapsed);
        }
        catch (Exception error) when (operation != version || closed)
        { throw new OperationCanceledException("Superseded query.", error); }
        catch (OperationCanceledException) { QueryError = "搜索已取消。"; throw; }
        catch (Exception error) { QueryError = "搜索失败：" + error.Message; throw; }
        finally
        {
            if (ReferenceEquals(queryCancellation, cancellation)) queryCancellation = null;
            if (operation == version) { isSearching = false; NotifyState(); }
        }
    }
    public void Cancel()
    {
        if (closed) return;
        buildCancellation?.Cancel(); controller.Cancel(); InvalidateQuery();
        if (!isBuilding) QueryError = "搜索已取消。";
        NotifyState();
    }
    private void EnsureCurrent(long operation, long requestId)
    { if (closed || operation != version || requestId != controller.CurrentRequestId) throw new OperationCanceledException("Superseded query."); }
    private void InvalidateQuery()
    { version++; queryCancellation?.Cancel(); controller.CancelQuery(); cursor = null; isSearching = false; }
    private void ClearResults()
    { Results.Clear(); AppliedRequest = null; HasPendingChanges = false; QueryError = ""; LastSearchElapsed = null; cursor = null; }
    private void Error(Exception error) { if (!closed) { QueryError = "操作失败：" + error.Message; NotifyState(); } }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(name)); return true; }
    private void SetQuery<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (Set(ref field, value, name)) { InvalidateQuery(); HasPendingChanges = AppliedRequest is not null; QueryError = ""; NotifyState(); } }
    public async ValueTask DisposeAsync()
    {
        if (closed) { await controller.DisposeAsync(); return; }
        closed = true; workspaceVersion++; lifetime.Cancel(); InvalidateQuery(); NotifyState();
        await controller.DisposeAsync();
    }
    private sealed class ImmediatePresentation : ISearchPresentation
    {
        public async Task<TimeSpan> MeasureAsync(Func<Task> updateResults, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); var timer = Stopwatch.StartNew(); await updateResults(); ct.ThrowIfCancellationRequested(); return timer.Elapsed; }
    }
}
