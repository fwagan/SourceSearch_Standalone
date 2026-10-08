using SourceSearch.Core.Tests;
namespace SourceSearch.Presentation.Tests;
public class WorkspaceStateTests
{
    [Fact]
    public async Task Explicit_snapshot_displays_its_actual_source_folder()
    {
        using var source = new TestCorpus(); using var other = new TestCorpus(); source.Write("a.cs", "User");
        using var snapshot = (await source.BuildAsync()).Snapshot;
        await using var model = Create(other); await model.OpenSnapshotAsync(snapshot.DirectoryPath);
        Assert.Equal(source.Root, model.RootPath); Assert.Equal(source.Root, model.IndexedRoot);
        Assert.Single((await model.SearchAsync()).Page.Hits);
    }
    private sealed class FailAndChangeBuilder(IdentityReader reader) : IIndexBuilder
    {
        private int calls;
        public Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            if (++calls == 1) return new IndexBuilder(new FileCatalog(), new StrictTextReader()).BuildAsync(options, progress, ct);
            reader.Value = reader.Value with { Commit = "changed-during-failure" };
            throw new IOException("rebuild failed");
        }
    }
    [Fact]
    public async Task Failed_build_after_head_change_does_not_restore_old_snapshot_search()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); var reader = new IdentityReader();
        await using var model = Create(corpus, reader, new FailAndChangeBuilder(reader));
        await model.BuildAsync(); Assert.True(model.CanSearch); await model.BuildAsync();
        Assert.False(model.CanSearch); Assert.Equal(IndexPhase.Stale, model.Phase);
    }
    private sealed class FirstDelayedReader : IWorkspaceIdentityReader
    {
        private int calls;
        public TaskCompletionSource<WorkspaceIdentity> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceIdentity> ReadAsync(string path, CancellationToken ct) => ++calls == 1 ? First.Task : Task.FromResult(new WorkspaceIdentity(WorkspaceKind.Git, path, "new", "2"));
    }
    [Fact]
    public async Task Metadata_started_before_build_cannot_override_the_built_identity()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); var reader = new FirstDelayedReader();
        await using var model = Create(corpus, reader); var old = model.RefreshIdentityAsync();
        await model.BuildAsync(); reader.First.SetResult(new(WorkspaceKind.Git, corpus.Root, "old", "1")); await old;
        Assert.True(model.CanSearch); Assert.Equal("new", model.Identity.Branch);
    }
    private sealed class IdentityReader : IWorkspaceIdentityReader
    {
        public WorkspaceIdentity Value = new(WorkspaceKind.Git, "repo", "main", "1");
        public int Calls;
        public Task<WorkspaceIdentity> ReadAsync(string path, CancellationToken ct) { Calls++; return Task.FromResult(Value); }
    }
    private static SearchViewModel Create(TestCorpus corpus, IWorkspaceIdentityReader? reader = null, IIndexBuilder? builder = null) =>
        new(new(builder ?? new IndexBuilder(new FileCatalog(), new StrictTextReader()), new SearchService()), Path.Combine(corpus.Root, ".index"), identityReader: reader) { RootPath = corpus.Root, QueryText = "User" };
    [Fact]
    public async Task Different_folder_clears_results_and_reader_but_same_folder_keeps_them()
    {
        using var a = new TestCorpus(); using var b = new TestCorpus(); a.Write("a.cs", "User"); b.Write("b.cs", "User");
        await using var model = Create(a); await model.BuildAsync(); await model.SearchAsync();
        var id = model.Report!.SnapshotId; await model.SwitchFolderAsync(Path.Combine(a.Root, "."));
        Assert.Equal(id, model.Report!.SnapshotId); Assert.Single(model.Results);
        await model.SwitchFolderAsync(b.Root);
        Assert.False(model.CanSearch); Assert.Empty(model.Results); Assert.Null(model.Report); Assert.Equal(b.Root, model.RootPath);
        await model.BuildAsync(); Assert.EndsWith("b.cs", Assert.Single((await model.SearchAsync()).Page.Hits).FilePath);
    }
    [Fact]
    public async Task Identity_change_invalidates_snapshot_and_query_never_reads_git()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); var reader = new IdentityReader();
        await using var model = Create(corpus, reader); await model.BuildAsync(); int calls = reader.Calls;
        await model.SearchAsync(); Assert.Equal(calls, reader.Calls);
        reader.Value = reader.Value with { Branch = "other" }; await model.RefreshIdentityAsync();
        Assert.Equal(IndexPhase.Stale, model.Phase); Assert.False(model.CanSearch); Assert.Empty(model.Results);
        reader.Value = new(WorkspaceKind.Unknown, Error: "unavailable"); await model.RefreshIdentityAsync();
        Assert.False(model.CanSearch); // A failed refresh cannot make a stale index usable again.
    }
    [Fact]
    public async Task Edited_query_keeps_applied_request_until_submit_and_rejects_empty_or_multiline()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User GetUser");
        await using var model = Create(corpus); await model.BuildAsync(); await model.SearchAsync();
        model.QueryText = "GetUser"; Assert.True(model.HasPendingChanges); Assert.Equal("User", model.AppliedRequest!.Text);
        await model.SearchAsync(); Assert.False(model.HasPendingChanges); Assert.Equal("GetUser", model.AppliedRequest!.Text);
        model.QueryText = ""; Assert.False(model.SearchCommand.CanExecute(null));
        model.QueryText = "User\nGetUser"; Assert.False(model.SearchCommand.CanExecute(null));
    }
    private sealed class ChangingBuilder(IdentityReader reader) : IIndexBuilder
    {
        public async Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            var built = await new IndexBuilder(new FileCatalog(), new StrictTextReader()).BuildAsync(options, progress, ct);
            reader.Value = reader.Value with { Commit = "2" }; return built;
        }
    }
    [Fact]
    public async Task Changed_head_during_build_does_not_mark_index_ready()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs", "User"); var reader = new IdentityReader();
        await using var model = Create(corpus, reader, new ChangingBuilder(reader)); await model.BuildAsync();
        Assert.Equal(IndexPhase.Stale, model.Phase); Assert.False(model.CanSearch);
    }
    private sealed class DelayedReader(string oldRoot) : IWorkspaceIdentityReader
    {
        public TaskCompletionSource<WorkspaceIdentity> Old { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkspaceIdentity> ReadAsync(string root, CancellationToken ct) => root == oldRoot ? Old.Task : Task.FromResult(new WorkspaceIdentity(WorkspaceKind.Git, root, "new", "2"));
    }
    [Fact]
    public async Task Late_metadata_and_duplicate_refresh_cannot_overwrite_new_folder()
    {
        using var a = new TestCorpus(); using var b = new TestCorpus(); var reader = new DelayedReader(a.Root);
        await using var model = Create(a, reader);
        var old = model.RefreshIdentityAsync(); var duplicate = model.RefreshIdentityAsync();
        var changed = model.SwitchFolderAsync(b.Root);
        reader.Old.SetResult(new(WorkspaceKind.Git, a.Root, "old", "1")); await Task.WhenAll(old, duplicate, changed);
        Assert.Equal(b.Root, model.RootPath); Assert.Equal("new", model.Identity.Branch);
    }
}
