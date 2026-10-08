using SourceSearch.Validation;
using SourceSearch.Desktop;
namespace SourceSearch.UiValidation;
public sealed class UiBenchmarkRunner(MainWindow window)
{
    public async Task<IReadOnlyList<QueryTiming>> RunAsync(IReadOnlyList<QueryCase> cases, int warmups, int iterations, CancellationToken ct)
    {
        if (warmups < 5 || iterations < 30) throw new ArgumentOutOfRangeException(nameof(iterations));
        var results = new List<QueryTiming>();
        foreach (var query in cases)
        {
            var first = await SearchScenario.ExecuteAsync(window, query, ct);
            for (int i = 0; i < warmups; i++) await SearchScenario.ExecuteAsync(window, query, ct);
            var samples = new List<double>();
            int rows = 0; long candidates = 0;
            for (int i = 0; i < iterations; i++)
            {
                var result = await SearchScenario.ExecuteAsync(window, query, ct);
                samples.Add(result.UiElapsed.TotalMilliseconds); rows = result.Page.Hits.Count; candidates = result.Page.CandidatesVisited;
            }
            results.Add(new(query.Name, "ui-render-checkpoint", samples.Count, BenchmarkRunner.Percentile(samples, .5), BenchmarkRunner.Percentile(samples, .95), samples.Max(), rows, candidates, samples, first.UiElapsed.TotalMilliseconds));
        }
        return results;
    }
}
