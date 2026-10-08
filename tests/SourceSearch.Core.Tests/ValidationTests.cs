using SourceSearch.Validation;
namespace SourceSearch.Core.Tests;
public class ValidationTests
{
    [Fact] public async Task CliBuildVerifyAndBenchmarkProduceReports()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "public class LongActualUserService { } // 用户权限");
        string report = Path.Combine(corpus.Root, "build.json");
        Assert.Equal(0, await Program.Main(["build", "--root", corpus.Root, "--preset", "csharp", "--index-root", Path.Combine(corpus.Root, ".index"), "--report", report]));
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        string snapshot = document.RootElement.GetProperty("SnapshotPath").GetString()!;
        string queries = Path.Combine(corpus.Root, "queries.json");
        Assert.Equal(0, await Program.Main(["queries", "--snapshot", snapshot, "--output", queries]));
        Assert.Equal(0, await Program.Main(["verify", "--snapshot", snapshot, "--queries", queries, "--output", Path.Combine(corpus.Root, "verify.json")]));
        Assert.Equal(0, await Program.Main(["benchmark", "--snapshot", snapshot, "--queries", queries, "--output", Path.Combine(corpus.Root, "benchmark.json")]));
        Assert.Equal(2, await Program.Main(["invalid-command"]));
    }
    [Fact] public void OracleHandlesOverlapsAndUnicodeIndependently()
    {
        Assert.Equal(new[] { new TextMatch(0, 2), new TextMatch(1, 2) }, ReferenceMatcher.Find("aaa", "aa", MatchMode.Contains));
        Assert.Empty(ReferenceMatcher.Find("User_Name", "User", MatchMode.WholeWord));
        Assert.Equal(new TextMatch(2, 3), Assert.Single(ReferenceMatcher.Find("😀𐐀User", "𐐨u", MatchMode.Contains)));
    }
    [Fact] public void PercentileUsesNearestRank() => Assert.Equal(29d, BenchmarkRunner.Percentile(Enumerable.Range(1, 30).Select(i => (double)i).ToArray(), 0.95));
    [Fact] public async Task VerifierDetectsMissingResultsAndChangedCorpus()
    {
        using var corpus = new TestCorpus();
        string path = corpus.Write("a.cs", "User User\nGetUser");
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        QueryCase[] cases = [new("user", new("User", MatchMode.Contains, ExtensionFilter.Parse(false, "", false, "")), "common")];
        var good = await new CorrectnessVerifier(new SearchService()).VerifyAsync(snapshot, cases, default);
        Assert.True(Assert.Single(good).Passed);
        Assert.Equal(2, good[0].ExpectedLines);
        var bad = await new CorrectnessVerifier(new EmptySearch()).VerifyAsync(snapshot, cases, default);
        Assert.False(Assert.Single(bad).Passed);
        File.AppendAllText(path, "\nUser");
        Assert.False(Assert.Single(await new CorrectnessVerifier(new SearchService()).VerifyAsync(snapshot, cases, default)).Passed);
    }
    [Fact] public async Task QuerySetRoundTripsAndBenchmarkRetainsAllSamples()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", "public class LongActualUserService { } // 用户权限");
        using var snapshot = (await corpus.BuildAsync()).Snapshot;
        var cases = QuerySetBuilder.Create(snapshot);
        Assert.True(cases.Count >= 30);
        string path = Path.Combine(corpus.Root, "queries.json");
        QuerySetBuilder.Write(path, cases);
        Assert.Equal(cases.Count, QuerySetBuilder.Read(path).Count);
        var timings = await new BenchmarkRunner(new SearchService()).RunAsync(snapshot, cases.Take(1).ToArray(), 5, 30, default);
        Assert.Equal(30, Assert.Single(timings).SampleCount);
    }
    private sealed class EmptySearch : ISearchService
    { public Task<SearchPage> SearchAsync(IndexSnapshot s, SearchRequest r, SearchCursor? c, int n, CancellationToken ct) => Task.FromResult(new SearchPage([], null, true, 0, TimeSpan.Zero)); }
}
