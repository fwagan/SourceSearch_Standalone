using Xunit;
namespace SourceSearch.Core.Tests;
public sealed class SearchControllerTests
{
    [Fact]
    public async Task Clear_waits_for_old_reader_then_removes_searchable_snapshot()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        using var built = (await corpus.BuildAsync()).Snapshot; var search = new ControlledSearch();
        await using var controller = new SearchController(new ControlledBuilder(), search);
        await controller.OpenSnapshotAsync(built.DirectoryPath, default);
        var pending = controller.SearchAsync(Request, null, 100, default);
        var clear = controller.ClearAsync(default);
        try { Assert.False(clear.IsCompleted); }
        finally { search.Release(0); try { await pending; } catch (OperationCanceledException) { } }
        await clear;
        Assert.Null(controller.CurrentReport); Assert.Equal(SearchState.NotBuilt, controller.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SearchAsync(Request, null, 100, default));
    }
    private static readonly SearchRequest Request = new("User", MatchMode.Contains, ExtensionFilter.Parse(false, "", false, ""));
    private sealed class ControlledSearch : ISearchService
    {
        public List<TaskCompletionSource<SearchPage>> Calls { get; } = [];
        public async Task<SearchPage> SearchAsync(IndexSnapshot snapshot, SearchRequest request, SearchCursor? cursor, int pageSize, CancellationToken ct)
        {
            var release = new TaskCompletionSource<SearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Add(release);
            var page = await release.Task;
            // Deliberately ignores cancellation until it has accessed the reader, testing its lifetime.
            await new SearchService().SearchAsync(snapshot, request, cursor, pageSize, default);
            return page;
        }
        public void Release(int index) => Calls[index].SetResult(new([], null, true, 0, TimeSpan.Zero));
    }
    private sealed class ControlledBuilder : IIndexBuilder
    {
        public List<TaskCompletionSource<BuildResult>> Calls { get; } = [];
        public Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            var release = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Add(release);
            return release.Task.WaitAsync(ct);
        }
    }
    [Fact]
    public async Task Query_only_invalidation_does_not_cancel_build()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        var built = await corpus.BuildAsync();
        var builder = new ControlledBuilder();
        await using var controller = new SearchController(builder, new SearchService());
        var pending = controller.RebuildAsync(new(new(corpus.Root, CorpusPresets.Mixed), Path.Combine(corpus.Root, ".index")), null, default);
        controller.CancelQuery();
        builder.Calls[0].SetResult(built);
        await pending;
        Assert.Equal(SearchState.Ready, controller.State);
    }
    [Fact]
    public async Task New_request_wins_even_when_old_service_ignores_cancellation()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        using var built = (await corpus.BuildAsync()).Snapshot;
        var service = new ControlledSearch();
        await using var controller = new SearchController(new ControlledBuilder(), service);
        await controller.OpenSnapshotAsync(built.DirectoryPath, default);
        var first = controller.SearchAsync(Request, null, 100, default);
        var second = controller.SearchAsync(Request, null, 100, default);
        service.Release(1);
        Assert.Equal(controller.CurrentRequestId, (await second).RequestId);
        service.Release(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }
    [Fact]
    public async Task Rebuild_failure_or_cancel_preserves_old_snapshot_and_serializes_builds()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        using var built = (await corpus.BuildAsync()).Snapshot;
        var builder = new ControlledBuilder();
        await using var controller = new SearchController(builder, new SearchService());
        await controller.OpenSnapshotAsync(built.DirectoryPath, default);
        var options = new IndexBuildOptions(new(corpus.Root, CorpusPresets.Mixed), Path.Combine(corpus.Root, ".index"));
        var first = controller.RebuildAsync(options, null, default);
        var second = controller.RebuildAsync(options, null, default);
        Assert.Single(builder.Calls);
        Assert.Equal(SearchState.Building, controller.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SearchAsync(Request, null, 100, default));
        builder.Calls[0].SetException(new IOException("failed build"));
        await Assert.ThrowsAsync<IOException>(() => first);
        for (int i = 0; i < 100 && builder.Calls.Count < 2; i++) await Task.Delay(10);
        Assert.Equal(2, builder.Calls.Count);
        controller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(SearchState.Failed, controller.State);
        Assert.Equal(built.Id, controller.CurrentReport!.SnapshotId);
        Assert.Single((await controller.SearchAsync(Request, null, 100, default)).Page.Hits);
    }
    [Fact]
    public async Task Close_waits_for_inflight_reader_use_and_rejects_new_work()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        using var built = (await corpus.BuildAsync()).Snapshot;
        var service = new ControlledSearch();
        var controller = new SearchController(new ControlledBuilder(), service);
        await controller.OpenSnapshotAsync(built.DirectoryPath, default);
        var pending = controller.SearchAsync(Request, null, 100, default);
        var close = controller.DisposeAsync().AsTask();
        Assert.False(close.IsCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.SearchAsync(Request, null, 100, default));
        service.Release(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await close;
    }
    [Fact]
    public async Task Successful_rebuild_waits_for_old_queries_then_replaces_snapshot()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        using var old = (await corpus.BuildAsync()).Snapshot;
        var replacement = await corpus.BuildAsync();
        var builder = new ControlledBuilder(); var search = new ControlledSearch();
        await using var controller = new SearchController(builder, search);
        await controller.OpenSnapshotAsync(old.DirectoryPath, default);
        var pending = controller.SearchAsync(Request, null, 100, default);
        var rebuild = controller.RebuildAsync(new(new(corpus.Root, CorpusPresets.Mixed), Path.Combine(corpus.Root, ".index")), null, default);
        Assert.Empty(builder.Calls);
        search.Release(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        for (int i = 0; i < 100 && builder.Calls.Count == 0; i++) await Task.Delay(10);
        Assert.Single(builder.Calls);
        builder.Calls[0].SetResult(replacement);
        await rebuild;
        Assert.Equal(SearchState.Ready, controller.State);
        Assert.Equal(replacement.Report.SnapshotId, controller.CurrentReport!.SnapshotId);
    }
    [Fact]
    public async Task No_snapshot_rejects_search_and_explicit_cancel_invalidates_reply()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User\nUser");
        using var built = (await corpus.BuildAsync()).Snapshot;
        var service = new ControlledSearch();
        await using var controller = new SearchController(new ControlledBuilder(), service);
        Assert.Equal(SearchState.NotBuilt, controller.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SearchAsync(Request, null, 100, default));
        await controller.OpenSnapshotAsync(built.DirectoryPath, default);
        var page = await new SearchService().SearchAsync(built, Request, null, 1, default);
        var pending = controller.SearchAsync(Request, page.Next, 1, default);
        controller.Cancel(); service.Release(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
