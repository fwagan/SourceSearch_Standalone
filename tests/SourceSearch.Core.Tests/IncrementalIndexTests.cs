namespace SourceSearch.Core.Tests;
public class IncrementalIndexTests
{
    private sealed class CountingSource : ITextSource
    {
        public List<string> Reads { get; } = [];
        public Task<TextReadResult> ReadAsync(SourceFile file, long maxBytes, CancellationToken ct)
        { Reads.Add(file.FullPath); return new StrictTextReader().ReadAsync(file, maxBytes, ct); }
    }
    private static Task<BuildResult> Build(TestCorpus corpus, ITextSource source, string? previous = null, CancellationToken ct = default) =>
        new IndexBuilder(new FileCatalog(), source).BuildAsync(new(new(corpus.Root, CorpusPresets.Mixed), Path.Combine(corpus.Root, ".index"), previous), null, ct);
    private static async Task<string[]> Hits(IndexSnapshot snapshot, string text) => (await new SearchService().SearchAsync(snapshot,
        new(text, MatchMode.Contains, ExtensionFilter.Parse(false,"",false,"")), null, 100, default)).Hits.Select(h => $"{h.FilePath}:{h.LineNumber}:{h.LineText}").Order().ToArray();
    [Fact]
    public async Task Unchanged_files_are_not_read_and_same_snapshot_is_reused()
    {
        using var corpus = new TestCorpus(); corpus.Write("a.cs","User");
        using var first=(await Build(corpus,new StrictTextReader())).Snapshot;
        var source=new CountingSource(); using var next=(await Build(corpus,source,first.DirectoryPath)).Snapshot;
        Assert.Empty(source.Reads); Assert.Equal(first.Id,next.Id); Assert.Equal(await Hits(first,"User"),await Hits(next,"User"));
    }
    [Fact]
    public async Task Incremental_add_change_delete_and_rename_match_full_build_and_preserve_old_reader()
    {
        using var corpus=new TestCorpus(); corpus.Write("a.cs","Old User");corpus.Write("b.cs","Stable User");corpus.Write("c.cs","Rename User");
        using var first=(await Build(corpus,new StrictTextReader())).Snapshot;
        File.Delete(Path.Combine(corpus.Root,"a.cs")); File.Move(Path.Combine(corpus.Root,"c.cs"),Path.Combine(corpus.Root,"d.cs"));corpus.Write("e.cs","Added User");
        var source=new CountingSource(); using var next=(await Build(corpus,source,first.DirectoryPath)).Snapshot;
        using var full=(await Build(corpus,new StrictTextReader())).Snapshot;
        Assert.Equal(2,source.Reads.Count); Assert.Equal(await Hits(full,"User"),await Hits(next,"User"));
        Assert.Equal(3,(await Hits(first,"User")).Length); Assert.Single(await Hits(first,"Old")); Assert.Empty(await Hits(next,"Old"));
        corpus.Write("b.cs","Changed User text"); using var changed=(await Build(corpus,source,next.DirectoryPath)).Snapshot;
        Assert.Single(await Hits(changed,"Changed")); Assert.Empty(await Hits(changed,"Stable"));
        Assert.Equal(1,changed.Report.Update!.Changed);
    }
    [Fact]
    public async Task Changed_stamp_with_same_content_reuses_terms_and_failed_reads_remove_stale_content()
    {
        using var corpus=new TestCorpus();string path=corpus.Write("a.cs","User");
        using var first=(await Build(corpus,new StrictTextReader())).Snapshot;
        File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddMinutes(-2));
        using var next=(await Build(corpus,new StrictTextReader(),first.DirectoryPath)).Snapshot;
        Assert.Equal(1,next.Report.Update!.Reused);Assert.Equal(0,next.Report.Update.Changed);
        File.WriteAllBytes(path,[0,1,2]);using var skipped=(await Build(corpus,new StrictTextReader(),next.DirectoryPath)).Snapshot;
        Assert.Empty(await Hits(skipped,"User"));Assert.Single(skipped.Report.Issues);
    }
    [Fact]
    public async Task Cancelled_update_leaves_previous_completed_snapshot_searchable()
    {
        using var corpus=new TestCorpus();corpus.Write("a.cs","User");using var first=(await Build(corpus,new StrictTextReader())).Snapshot;
        corpus.Write("b.cs","New User");using var ct=new CancellationTokenSource();ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Build(corpus,new StrictTextReader(),first.DirectoryPath,ct.Token));
        Assert.Single(await Hits(first,"User"));
    }
    [Fact]
    public async Task New_git_ignore_rule_removes_previously_indexed_untracked_file()
    {
        using var corpus=new TestCorpus();await corpus.GitAsync("","init","-b","test");corpus.Write("a.cs","User");corpus.Write("ignored.cs","User hidden");
        using var first=(await Build(corpus,new StrictTextReader())).Snapshot;
        corpus.Write(".gitignore","ignored.cs\n.index/\n");
        using var next=(await Build(corpus,new StrictTextReader(),first.DirectoryPath)).Snapshot;
        Assert.Single(await Hits(next,"User"));Assert.Equal(1,next.Report.Update!.Removed);
    }
}
