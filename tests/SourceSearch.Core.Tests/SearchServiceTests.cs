namespace SourceSearch.Core.Tests;
public class SearchServiceTests
{
    private static SearchRequest Query(string text, MatchMode mode = MatchMode.Contains) => new(text, mode, ExtensionFilter.Parse(false, "", false, ""));
    [Fact] public async Task FindsRealHitBeyondHundredsOfFalseCandidates()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", string.Join('\n', Enumerable.Repeat("abc---bcd---cde", 250).Append("abcde")));
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var page = await new SearchService().SearchAsync(snapshot, Query("abcde"), null, 100, default);
        Assert.Equal(251, Assert.Single(page.Hits).LineNumber);
        Assert.True(page.Exhausted);
        Assert.Equal(251, page.CandidatesVisited);
    }
    [Theory] [InlineData(100)] [InlineData(101)] [InlineData(1000)] [InlineData(1001)]
    public async Task PagesWithoutDuplicatesOrTruncation(int count)
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", string.Join('\n', Enumerable.Repeat("User", count)));
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var search = new SearchService();
        var hits = new List<SearchHit>();
        SearchCursor? cursor = null;
        do
        {
            var page = await search.SearchAsync(snapshot, Query("User"), cursor, 100, default);
            if (hits.Count == 0) Assert.Equal(100, page.CandidatesVisited);
            hits.AddRange(page.Hits); cursor = page.Next;
        } while (cursor is not null);
        Assert.Equal(Enumerable.Range(1, count), hits.Select(h => h.LineNumber));
    }
    [Theory] [InlineData("I", 2)] [InlineData("Id", 1)] [InlineData("IO", 1)] [InlineData("用户", 1)] [InlineData("a.b", 1)] [InlineData("aaaa", 1)] [InlineData("absent1234", 0)]
    public async Task SupportsShortUnicodeLiteralAndAbsentQueries(string text, int expected)
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "Id\nIO\n用户服务\na.b\naaaa");
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var page = await new SearchService().SearchAsync(snapshot, Query(text), null, 100, default);
        Assert.Equal(expected, page.Hits.Count);
    }
    [Fact] public async Task WholeWordKeepsUnderscoresAndFiltersDuringQuery()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "User_Name\nGetUser\nList<User>");
        corpus.Write("b.js", "User");
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var request = new SearchRequest("User", MatchMode.WholeWord, ExtensionFilter.Parse(true, "cs,js", true, "js"));
        var page = await new SearchService().SearchAsync(snapshot, request, null, 100, default);
        Assert.Equal(3, Assert.Single(page.Hits).LineNumber);
        Assert.Equal(1, page.CandidatesVisited);
    }
    [Fact] public async Task LongQueriesAndLongWordsDoNotExceedEngineLimits()
    {
        using var corpus = new TestCorpus();
        string longWord = new('x', 40000);
        string varied = string.Concat(Enumerable.Range(0, 1500).Select(i => $"token{i:X4} "));
        corpus.Write("a.cs", longWord + "\n" + varied);
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var search = new SearchService();
        Assert.Single((await search.SearchAsync(snapshot, Query(longWord, MatchMode.WholeWord), null, 100, default)).Hits);
        Assert.Single((await search.SearchAsync(snapshot, Query(varied), null, 100, default)).Hits);
    }
    [Fact] public async Task RejectsForeignCursorAndHandlesCancellation()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "User\nUser");
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var search = new SearchService();
        var first = await search.SearchAsync(snapshot, Query("User"), null, 1, default);
        await Assert.ThrowsAsync<ArgumentException>(() => search.SearchAsync(snapshot, Query("Id"), first.Next, 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => search.SearchAsync(snapshot, Query("User"), first.Next! with { SnapshotId = Guid.NewGuid() }, 1, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search.SearchAsync(snapshot, Query("User"), null, 100, new CancellationToken(true)));
    }
}
