using SourceSearch.Core.Tests;
namespace SourceSearch.Presentation.Tests;
public sealed class SearchViewModelTests
{
    private sealed class FailingPresentation : ISearchPresentation
    {
        public bool Fail { get; set; }
        public async Task<TimeSpan> MeasureAsync(Func<Task> update, CancellationToken ct)
        { await update(); if (Fail) throw new TimeoutException("render timeout"); return TimeSpan.Zero; }
    }
    [Fact]
    public async Task Failed_render_cannot_reuse_an_old_page_cursor_for_new_results()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", string.Join('\n', Enumerable.Repeat("User", 400)));
        var presentation = new FailingPresentation();
        await using var model = new SearchViewModel(new(new IndexBuilder(new FileCatalog(), new StrictTextReader()), new SearchService()), Path.Combine(corpus.Root, ".index"), presentation) { RootPath = corpus.Root, QueryText = "User" };
        await model.BuildAsync(); await model.SearchAsync(); await model.SearchAsync(true);
        presentation.Fail = true;
        await Assert.ThrowsAsync<TimeoutException>(() => model.SearchAsync());
        Assert.False(model.CanMore);
    }
    private sealed class ControlledBuilder : IIndexBuilder
    {
        public List<(TaskCompletionSource<BuildResult> Completion, CancellationToken Token)> Calls { get; } = [];
        public Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            var source = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Add((source, ct)); return source.Task.WaitAsync(ct);
        }
    }
    [Fact]
    public async Task Commands_follow_build_state_and_query_edits_do_not_cancel_build()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        var built = await corpus.BuildAsync(); var builder = new ControlledBuilder();
        await using var model = new SearchViewModel(new(builder, new SearchService()), Path.Combine(corpus.Root, ".index")) { RootPath = corpus.Root };
        Assert.True(model.BuildCommand.CanExecute(null)); Assert.False(model.SearchCommand.CanExecute(null));
        var pending = model.BuildCommand.ExecuteAsync();
        Assert.True(model.IsBuilding); Assert.False(model.BuildCommand.CanExecute(null)); Assert.True(model.CancelCommand.CanExecute(null));
        model.QueryText = "User"; Assert.False(builder.Calls[0].Token.IsCancellationRequested);
        builder.Calls[0].Completion.SetResult(built); await pending;
        Assert.True(model.SearchCommand.CanExecute(null)); Assert.False(model.CancelCommand.CanExecute(null));
        var cancelled = model.BuildCommand.ExecuteAsync(); await model.CancelCommand.ExecuteAsync(); await cancelled;
        Assert.True(builder.Calls[1].Token.IsCancellationRequested); Assert.Contains("取消", model.StatusText);
        await model.SearchCommand.ExecuteAsync(); Assert.Single(model.Results);
    }
    private static SearchViewModel Create(TestCorpus corpus, ISearchService? service = null) =>
        new(new(new IndexBuilder(new FileCatalog(), new StrictTextReader()), service ?? new SearchService()), Path.Combine(corpus.Root, ".index")) { RootPath = corpus.Root };
    [Fact]
    public async Task Disabled_filters_keep_their_values_after_search_and_can_be_enabled_again()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); corpus.Write("a.js", "User");
        await using var model = Create(corpus);
        await model.BuildAsync(); model.QueryText = "User"; model.IncludeText = "cs"; model.ExcludeText = "js";
        Assert.Equal(2, (await model.SearchAsync()).Page.Hits.Count);
        Assert.Equal("cs", model.IncludeText); Assert.Equal("js", model.ExcludeText);
        model.IncludeEnabled = true; model.ExcludeEnabled = true;
        Assert.EndsWith("a.cs", Assert.Single((await model.SearchAsync()).Page.Hits).FilePath);
    }
    [Fact]
    public async Task Build_search_page_and_query_changes_keep_result_state_consistent()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", string.Join('\n', Enumerable.Repeat("User", 1001)));
        await using var model = Create(corpus);
        Assert.False(model.CanSearch);
        await model.BuildAsync(); Assert.True(model.CanSearch);
        model.QueryText = "User";
        await model.SearchAsync(); Assert.Equal(100, model.Results.Count); Assert.True(model.CanMore);
        for (int i = 0; i < 9; i++) await model.SearchAsync(true);
        Assert.Equal(1000, model.Results.Count); Assert.False(model.CanMore);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.SearchAsync(true));
        await model.SearchAsync(); model.WholeWord = true;
        Assert.False(model.CanMore);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.SearchAsync(true));
    }
    [Fact]
    public async Task Failed_build_leaves_old_snapshot_searchable()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User");
        await using var model = Create(corpus);
        await model.BuildAsync(); model.RootPath = Path.Combine(corpus.Root, "missing");
        await model.BuildAsync();
        Assert.Contains("建立失败", model.StatusText); Assert.True(model.CanSearch);
        model.QueryText = "User"; Assert.Single((await model.SearchAsync()).Page.Hits);
    }
    private sealed class RacingSearch : ISearchService
    {
        public TaskCompletionSource<SearchPage> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public Task<SearchPage> SearchAsync(IndexSnapshot snapshot, SearchRequest request, SearchCursor? cursor, int size, CancellationToken ct) =>
            Interlocked.Increment(ref calls) == 1 ? First.Task : new SearchService().SearchAsync(snapshot, request, cursor, size, ct);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Superseded_query_cannot_publish_results_or_errors(bool fail)
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "Old\nNew"); var service = new RacingSearch();
        await using var model = Create(corpus, service);
        await model.BuildAsync(); model.QueryText = "Old"; var first = model.SearchAsync();
        model.QueryText = "New"; var second = await model.SearchAsync();
        if (fail) service.First.SetException(new IOException("stale error")); else service.First.SetResult(new([], null, true, 0, TimeSpan.Zero));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Single(model.Results); Assert.Equal("New", Assert.Single(second.Page.Hits).LineText);
    }
    [Fact]
    public async Task Closing_disables_operations()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); var model = Create(corpus);
        await model.BuildAsync(); await model.DisposeAsync();
        Assert.False(model.CanSearch);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.SearchAsync());
    }
}
