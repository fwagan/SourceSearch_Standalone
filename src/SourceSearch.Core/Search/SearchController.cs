namespace SourceSearch.Core;
public enum SearchState { NotBuilt, Building, Ready, Failed }
public sealed record SearchReply(long RequestId, SearchPage Page);
public sealed class SearchController(IIndexBuilder builder, ISearchService search) : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim rebuildGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> active = [];
    private CancellationTokenSource? latestQuery, building;
    private IndexSnapshot? snapshot;
    private SearchState state;
    private long requestId;
    private bool disposed;
    private Task? disposal;
    public SearchState State { get { lock (sync) return state; } }
    public long CurrentRequestId { get { lock (sync) return requestId; } }
    public BuildReport? CurrentReport { get { lock (sync) return snapshot?.Report; } }

    public Task RebuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct) =>
        ReplaceAsync(async token => (await builder.BuildAsync(options, progress, token).ConfigureAwait(false)).Snapshot, ct);

    public Task OpenSnapshotAsync(string path, CancellationToken ct) =>
        ReplaceAsync(token => Task.Run(() => IndexSnapshot.Open(path), token), ct);

    private async Task ReplaceAsync(Func<CancellationToken, Task<IndexSnapshot>> create, CancellationToken ct)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        await rebuildGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        IndexSnapshot? replacement = null;
        try
        {
            Task[] queries;
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                state = SearchState.Building;
                building = cancellation;
                latestQuery?.Cancel(); requestId++;
                queries = active.ToArray();
            }
            // Queries keep the old reader alive, even if a service ignores cancellation.
            await Task.WhenAll(queries).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            replacement = await create(cancellation.Token).ConfigureAwait(false);
            IndexSnapshot? previous;
            lock (sync)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                previous = snapshot; snapshot = replacement; replacement = null;
                state = SearchState.Ready;
            }
            previous?.Dispose();
        }
        catch { lock (sync) state = SearchState.Failed; throw; }
        finally
        {
            replacement?.Dispose();
            lock (sync) building = null;
            rebuildGate.Release();
        }
    }

    public async Task<SearchReply> SearchAsync(SearchRequest request, SearchCursor? cursor, int pageSize, CancellationToken ct)
    {
        IndexSnapshot selected;
        CancellationTokenSource cancellation;
        long id;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (state == SearchState.Building || snapshot is null) throw new InvalidOperationException("Index is not ready.");
            latestQuery?.Cancel();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            latestQuery = cancellation; selected = snapshot; id = ++requestId;
            active.Add(finished.Task);
        }
        try
        {
            var page = await search.SearchAsync(selected, request, cursor, pageSize, cancellation.Token).ConfigureAwait(false);
            lock (sync)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (id != requestId) throw new OperationCanceledException("Superseded query.");
            }
            return new(id, page);
        }
        finally
        {
            lock (sync)
            {
                active.Remove(finished.Task);
                if (latestQuery == cancellation) latestQuery = null;
                cancellation.Dispose(); finished.SetResult();
            }
        }
    }

    public void CancelQuery()
    { lock (sync) { latestQuery?.Cancel(); requestId++; } }
    public void Cancel()
    {
        lock (sync) { latestQuery?.Cancel(); building?.Cancel(); requestId++; }
    }
    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposal is not null) return new(disposal);
            disposed = true; lifetime.Cancel(); requestId++;
            disposal = DisposeCoreAsync();
            return new(disposal);
        }
    }
    private async Task DisposeCoreAsync()
    {
        await rebuildGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Task[] queries;
            lock (sync) queries = active.ToArray();
            await Task.WhenAll(queries).ConfigureAwait(false);
            lock (sync) { snapshot?.Dispose(); snapshot = null; }
        }
        finally { rebuildGate.Release(); }
    }
}
