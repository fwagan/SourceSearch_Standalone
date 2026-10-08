namespace SourceSearch.Core.Tests;
public class IndexCacheTests
{
    private sealed class Identity(WorkspaceIdentity value) : IWorkspaceIdentityReader
    {
        public WorkspaceIdentity Value = value;
        public Task<WorkspaceIdentity> ReadAsync(string root, CancellationToken ct) => Task.FromResult(Value);
    }
    private sealed class Fixture : IDisposable
    {
        public TestCorpus Corpus = new();
        public CatalogOptions Catalog;
        public Identity Reader;
        public IndexCache Cache;
        public string CacheRoot;
        public Fixture()
        {
            Corpus.Write("a.cs","User"); Catalog=new(Corpus.Root,CorpusPresets.Mixed);
            Reader=new(new(WorkspaceKind.Git,Corpus.Root,"main","1")); CacheRoot=Path.Combine(Corpus.Root,".index");
            Cache=new(CacheRoot,new IndexBuilder(new FileCatalog(),new StrictTextReader()),Reader);
        }
        public void Dispose()=>Corpus.Dispose();
    }
    [Fact]
    public void Keys_separate_branch_case_root_scope_settings_and_detached_commit()
    {
        using var f=new Fixture(); var id=f.Reader.Value; string Key(CatalogOptions c,WorkspaceIdentity i)=>IndexCache.KeyFor(c,i);
        Assert.Equal(Key(f.Catalog,id),Key(f.Catalog with{RootPath=f.Corpus.Root.ToUpperInvariant()},id));
        Assert.NotEqual(Key(f.Catalog,id),Key(f.Catalog,id with{Branch="Main"}));
        Assert.NotEqual(Key(f.Catalog,id),Key(f.Catalog with{RootPath=Path.Combine(f.Corpus.Root,"sub")},id));
        Assert.NotEqual(Key(f.Catalog,id),Key(f.Catalog with{MaxFileBytes=1},id));
        Assert.Equal(Key(f.Catalog,id),Key(f.Catalog,id with{Commit="2"}));
        Assert.NotEqual(Key(f.Catalog,id with{Branch=null}),Key(f.Catalog,id with{Branch=null,Commit="2"}));
        Assert.Throws<InvalidOperationException>(()=>Key(f.Catalog,new(WorkspaceKind.Plain)));
    }
    [Fact]
    public async Task Restart_reuses_cache_then_incrementally_updates_and_branches_are_isolated()
    {
        using var f=new Fixture(); Assert.False(f.Cache.Exists(f.Catalog,f.Reader.Value));
        Guid firstId;
        using(var first=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot) firstId=first.Id;
        Assert.True(f.Cache.Exists(f.Catalog,f.Reader.Value));
        var reopened=new IndexCache(f.CacheRoot,new IndexBuilder(new FileCatalog(),new StrictTextReader()),f.Reader);
        using(var next=(await reopened.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot) Assert.Equal(firstId,next.Id);
        f.Corpus.Write("b.cs","Added User");
        using(var next=(await reopened.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot){Assert.NotEqual(firstId,next.Id);Assert.Equal(1,next.Report.Update!.Reused);}
        f.Reader.Value=f.Reader.Value with{Branch="other"};Assert.False(reopened.Exists(f.Catalog,f.Reader.Value));
        using var other=(await reopened.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot;
        Assert.Equal(2,(await reopened.ListAsync(default)).Count);
    }
    [Fact]
    public async Task In_use_cache_cannot_be_deleted_and_untrusted_key_is_rejected()
    {
        using var f=new Fixture(); var built=await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default);
        string key=IndexCache.KeyFor(f.Catalog,f.Reader.Value);
        try{await Assert.ThrowsAsync<IOException>(()=>f.Cache.DeleteAsync(key,default));Assert.True(f.Cache.Exists(f.Catalog,f.Reader.Value));}
        finally{built.Snapshot.Dispose();}
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Cache.DeleteAsync("../outside",default));
        await f.Cache.DeleteAsync(key,default);Assert.Empty(await f.Cache.ListAsync(default));
    }
    private sealed class ChangeIdentityBuilder(IIndexBuilder inner,Identity reader) : IIndexBuilder
    {
        public async Task<BuildResult> BuildAsync(IndexBuildOptions options,IProgress<BuildProgress>? progress,CancellationToken ct)
        {var result=await inner.BuildAsync(options,progress,ct);reader.Value=reader.Value with{Commit="changed"};return result;}
    }
    [Fact]
    public async Task Head_change_during_update_does_not_publish_new_entry()
    {
        using var f=new Fixture(); Guid id;
        using(var first=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot)id=first.Id;
        f.Corpus.Write("a.cs","Modified User");
        var cache=new IndexCache(f.CacheRoot,new ChangeIdentityBuilder(new IndexBuilder(new FileCatalog(),new StrictTextReader()),f.Reader),f.Reader);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default));
        var record=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(f.CacheRoot,IndexCache.KeyFor(f.Catalog,f.Reader.Value),"entry.json")));
        Assert.Equal(id,record.RootElement.GetProperty("SnapshotId").GetGuid());record.Dispose();
    }
    [Fact]
    public async Task Corrupt_entry_is_reported_and_full_rebuild_repairs_it()
    {
        using var f=new Fixture(); using(var first=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot){}
        File.WriteAllText(Path.Combine(f.CacheRoot,IndexCache.KeyFor(f.Catalog,f.Reader.Value),"entry.json"),"invalid json");
        await Assert.ThrowsAsync<InvalidDataException>(()=>f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default));
        Assert.Contains("损坏",Assert.Single(await f.Cache.ListAsync(default)).Status);
        using var repaired=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,true,null,default)).Snapshot;
        Assert.Single(repaired.Report.Files);
    }
    [Fact]
    public async Task Cache_symlink_is_rejected_without_deleting_the_target()
    {
        using var f=new Fixture();using var outside=new TestCorpus();string marker=outside.Write("keep.cs","keep");
        string key=IndexCache.KeyFor(f.Catalog,f.Reader.Value);Directory.CreateDirectory(f.CacheRoot);
        Directory.CreateSymbolicLink(Path.Combine(f.CacheRoot,key),outside.Root);
        await Assert.ThrowsAsync<IOException>(()=>f.Cache.DeleteAsync(key,default));Assert.True(File.Exists(marker));
        Assert.Throws<IOException>(()=>f.Cache.Exists(f.Catalog,f.Reader.Value));
    }
    private sealed class HeldBuilder(IIndexBuilder inner) : IIndexBuilder
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BuildResult> BuildAsync(IndexBuildOptions options,IProgress<BuildProgress>? progress,CancellationToken ct)
        {Entered.TrySetResult();await Release.Task.WaitAsync(ct);return await inner.BuildAsync(options,progress,ct);}
    }
    [Fact]
    public async Task Concurrent_update_and_delete_are_rejected_and_cancel_preserves_registration()
    {
        using var f=new Fixture();Guid first;
        using(var built=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot)first=built.Id;
        var held=new HeldBuilder(new IndexBuilder(new FileCatalog(),new StrictTextReader()));
        var cache=new IndexCache(f.CacheRoot,held,f.Reader);using var ct=new CancellationTokenSource();
        var pending=cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,ct.Token);await held.Entered.Task;
        try
        {
            await Assert.ThrowsAsync<IOException>(()=>f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default));
            await Assert.ThrowsAsync<IOException>(()=>f.Cache.DeleteAsync(IndexCache.KeyFor(f.Catalog,f.Reader.Value),default));
        }
        finally{ct.Cancel();held.Release.TrySetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);}
        using var reopened=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot;Assert.Equal(first,reopened.Id);
    }
    [Theory]
    [InlineData("report")]
    [InlineData("files")]
    [InlineData("file-element")]
    public async Task Malformed_manifest_isolated_from_healthy_entries_and_can_be_deleted(string damage)
    {
        using var f=new Fixture();string snapshotPath;
        using(var first=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot)snapshotPath=first.DirectoryPath;
        string brokenKey=IndexCache.KeyFor(f.Catalog,f.Reader.Value);
        f.Reader.Value=f.Reader.Value with{Branch="healthy"};using(var healthy=(await f.Cache.PrepareAsync(f.Catalog,f.Reader.Value,false,null,default)).Snapshot){}
        string path=Path.Combine(snapshotPath,"manifest.json");var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        if(damage=="report")json["Report"]=null;
        else if(damage=="files")json["Report"]!["Files"]=null;
        else json["Report"]!["Files"]![0]=null;
        File.WriteAllText(path,json.ToJsonString());
        var entries=await f.Cache.ListAsync(default);Assert.Equal(2,entries.Count);Assert.Single(entries,e=>e.Status=="可用");
        Assert.Contains("损坏",entries.Single(e=>e.Key==brokenKey).Status);
        await f.Cache.DeleteAsync(brokenKey,default);Assert.Single(await f.Cache.ListAsync(default));
    }
}
