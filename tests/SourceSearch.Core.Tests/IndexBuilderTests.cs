using Lucene.Net.Index;
namespace SourceSearch.Core.Tests;
public class IndexBuilderTests
{
    [Fact] public async Task PreservesLinesAndIndexesShortTerms()
    {
        using var corpus = new TestCorpus();
        corpus.Write("User.cs", "// header\r\nGetUser(Id);\r\n");
        var result = await corpus.BuildAsync();
        using var snapshot = result.Snapshot;
        Assert.Equal(2, result.Report.IndexedLines);
        Assert.Equal(2, Assert.Single(result.Report.Files).LineCount);
        Assert.Equal(1, snapshot.Reader.DocFreq(new Term("gram1", "i")));
        Assert.Equal(1, snapshot.Reader.DocFreq(new Term("gram2", "id")));
        Assert.Equal(1, snapshot.Reader.DocFreq(new Term("gram3", "use")));
        Assert.Equal(1, snapshot.Reader.DocFreq(new Term("words", "getuser")));
        Assert.Equal("GetUser(Id);", snapshot.Searcher.Doc(1).Get("text"));
        using var reopened = IndexSnapshot.Open(snapshot.DirectoryPath);
        Assert.Equal(snapshot.Id, reopened.Id);
        Assert.Equal(2, reopened.Reader.NumDocs);
    }
    [Fact] public async Task LongWordsAndEmptyLinesAreNotLost()
    {
        using var corpus = new TestCorpus();
        string longWord = new('a', 40000);
        corpus.Write("long.cs", longWord + "\n\nUser");
        var result = await corpus.BuildAsync();
        using var snapshot = result.Snapshot;
        Assert.Equal(3, snapshot.Reader.NumDocs);
        Assert.Equal(longWord, snapshot.Searcher.Doc(0).Get("text"));
        Assert.Equal("", snapshot.Searcher.Doc(1).Get("text"));
        Assert.Equal(1, snapshot.Reader.DocFreq(new Term("gram3", "aaa")));
    }
    [Fact] public async Task EmptyCorpusProducesUsableEmptyIndex()
    {
        using var corpus = new TestCorpus();
        var result = await corpus.BuildAsync();
        using var snapshot = result.Snapshot;
        Assert.Equal(0, snapshot.Reader.NumDocs);
    }
    [Fact] public async Task FailedBuildDoesNotReplaceCompletedSnapshot()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "User");
        var result = await corpus.BuildAsync();
        using var snapshot = result.Snapshot;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IndexBuilder(new FileCatalog(), new FailingSource()).BuildAsync(
            new(new(corpus.Root, CorpusPresets.CSharp), Path.Combine(corpus.Root, ".index")), null, default));
        using var reopened = IndexSnapshot.Open(snapshot.DirectoryPath);
        Assert.Equal(1, reopened.Reader.NumDocs);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(corpus.Root, ".index"), "completed", SearchOption.AllDirectories));
    }
    [Fact] public async Task CancelledBuildIsNotPublished()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "User");
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IndexBuilder(new FileCatalog(), new CancellingSource(cts)).BuildAsync(
            new(new(corpus.Root, CorpusPresets.CSharp), Path.Combine(corpus.Root, ".index")), null, cts.Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(corpus.Root, ".index"), "completed", SearchOption.AllDirectories));
    }
    [Fact] public void RejectsIncompleteSnapshot()
    {
        using var corpus = new TestCorpus();
        Assert.Throws<InvalidDataException>(() => IndexSnapshot.Open(corpus.Root));
    }
    private sealed class FailingSource : ITextSource
    { public Task<TextReadResult> ReadAsync(SourceFile f, long maxBytes, CancellationToken ct) => throw new InvalidOperationException("Injected storage failure."); }
    private sealed class CancellingSource(CancellationTokenSource cts) : ITextSource
    {
        public async Task<TextReadResult> ReadAsync(SourceFile f, long maxBytes, CancellationToken ct)
        { var result = await new StrictTextReader().ReadAsync(f, maxBytes, ct); cts.Cancel(); return result; }
    }
}
