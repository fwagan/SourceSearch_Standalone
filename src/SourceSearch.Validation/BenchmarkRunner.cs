using SourceSearch.Core;
using System.Diagnostics;
namespace SourceSearch.Validation;
public sealed class BenchmarkRunner(ISearchService search)
{
    public static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        if (samples.Count == 0 || percentile <= 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        return samples.Order().ElementAt((int)Math.Ceiling(percentile * samples.Count) - 1);
    }
    public async Task<IReadOnlyList<QueryTiming>> RunAsync(IndexSnapshot snapshot, IReadOnlyList<QueryCase> queries, int warmups, int iterations, CancellationToken ct)
    {
        if (warmups < 5 || iterations < 30) throw new ArgumentOutOfRangeException(nameof(iterations), "Use at least 5 warmups and 30 samples.");
        var results = new List<QueryTiming>();
        foreach (var query in queries)
        {
            for (int i = 0; i < warmups; i++) await search.SearchAsync(snapshot, query.Request, null, 100, ct).ConfigureAwait(false);
            var times = new List<double>();
            SearchPage? last = null;
            for (int i = 0; i < iterations; i++)
            {
                var timer = Stopwatch.StartNew();
                last = await search.SearchAsync(snapshot, query.Request, null, 100, ct).ConfigureAwait(false);
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var result = new QueryTiming(query.Name, "core", times.Count, Percentile(times, .5), Percentile(times, .95), times.Max(), last!.Hits.Count, last.CandidatesVisited, times);
            results.Add(result);
            Console.Error.WriteLine($"Benchmark {query.Name}: P95={result.P95Ms:F2} ms, rows={last.Hits.Count}");
        }
        return results;
    }
}
