using SourceSearch.Core.Tests;
namespace SourceSearch.Presentation.Tests;
public class CacheWorkflowTests
{
    private sealed class Reader(string root) : IWorkspaceIdentityReader
    {
        public WorkspaceIdentity Value=new(WorkspaceKind.Git,root,"main","1");
        public Task<WorkspaceIdentity> ReadAsync(string root,CancellationToken ct)=>Task.FromResult(Value with{RepositoryRoot=root});
    }
    private static SearchViewModel Model(TestCorpus corpus,Reader reader,IIndexCache cache) =>
        new(new(new IndexBuilder(new FileCatalog(),new StrictTextReader()),new SearchService()),Path.Combine(corpus.Root,".index"),identityReader:reader,cache:cache){RootPath=corpus.Root,QueryText="User"};
    [Fact]
    public async Task New_workspace_does_not_autobuild_but_restart_reuses_then_updates_existing_cache()
    {
        using var corpus=new TestCorpus();corpus.Write("a.cs","User");var reader=new Reader(corpus.Root);
        var cache=new IndexCache(Path.Combine(corpus.Root,".cache"),new IndexBuilder(new FileCatalog(),new StrictTextReader()),reader);
        Guid id;
        await using(var model=Model(corpus,reader,cache)){
            await model.ActivateAsync();Assert.False(model.CanSearch);
            await model.BuildAsync();id=model.Report!.SnapshotId;
        }
        await using(var model=Model(corpus,reader,cache)){
            await model.ActivateAsync();Assert.True(model.CanSearch);Assert.Equal(id,model.Report!.SnapshotId);
        }
        corpus.Write("b.cs","Second User");
        await using(var model=Model(corpus,reader,cache)){
            await model.ActivateAsync();Assert.Equal(2,(await model.SearchAsync()).Page.Hits.Count);Assert.NotEqual(id,model.Report!.SnapshotId);
        }
    }
    [Fact]
    public async Task Branch_change_clears_old_results_and_returning_branch_recovers_cached_index()
    {
        using var corpus=new TestCorpus();corpus.Write("a.cs","User");var reader=new Reader(corpus.Root);
        var cache=new IndexCache(Path.Combine(corpus.Root,".cache"),new IndexBuilder(new FileCatalog(),new StrictTextReader()),reader);
        await using var model=Model(corpus,reader,cache);await model.BuildAsync();await model.SearchAsync();
        reader.Value=reader.Value with{Branch="other"};await model.ActivateAsync();Assert.False(model.CanSearch);Assert.Empty(model.Results);
        reader.Value=reader.Value with{Branch="main"};await model.ActivateAsync();Assert.True(model.CanSearch);Assert.Single((await model.SearchAsync()).Page.Hits);
    }
    [Fact]
    public async Task Ordinary_folder_never_registers_persistent_cache()
    {
        using var corpus=new TestCorpus();corpus.Write("a.cs","User");var reader=new Reader(corpus.Root){Value=new(WorkspaceKind.Plain)};
        var cache=new IndexCache(Path.Combine(corpus.Root,".cache"),new IndexBuilder(new FileCatalog(),new StrictTextReader()),reader);
        await using var model=Model(corpus,reader,cache);await model.ActivateAsync();await model.BuildAsync();Assert.True(model.CanSearch);Assert.Empty(await cache.ListAsync(default));
    }
    [Fact]
    public async Task Unknown_identity_on_activation_disables_cached_search_until_identity_recovers()
    {
        using var corpus=new TestCorpus();corpus.Write("a.cs","User");var reader=new Reader(corpus.Root);
        var cache=new IndexCache(Path.Combine(corpus.Root,".cache"),new IndexBuilder(new FileCatalog(),new StrictTextReader()),reader);
        await using var model=Model(corpus,reader,cache);await model.BuildAsync();await model.SearchAsync();
        reader.Value=new(WorkspaceKind.Unknown,Error:"Git unavailable");await model.ActivateAsync();
        Assert.False(model.CanSearch);Assert.Empty(model.Results);
        reader.Value=new(WorkspaceKind.Git,corpus.Root,"main","1");await model.ActivateAsync();Assert.True(model.CanSearch);
    }
}
