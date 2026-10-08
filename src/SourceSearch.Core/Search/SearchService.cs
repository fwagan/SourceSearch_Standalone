using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucene.Net.Search;
namespace SourceSearch.Core;
public interface ISearchService
{
    Task<SearchPage> SearchAsync(IndexSnapshot snapshot, SearchRequest request, SearchCursor? cursor, int pageSize, CancellationToken ct);
}
public sealed class SearchService : ISearchService
{
    public Task<SearchPage> SearchAsync(IndexSnapshot snapshot, SearchRequest request, SearchCursor? cursor, int pageSize, CancellationToken ct) =>
        Task.Run(() => Search(snapshot, request, cursor, pageSize, ct), ct);

    private static SearchPage Search(IndexSnapshot snapshot, SearchRequest request, SearchCursor? cursor, int pageSize, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        ArgumentNullException.ThrowIfNull(request.Text);
        if (request.Text.Contains('\r') || request.Text.Contains('\n')) throw new ArgumentException("Cross-line queries are not supported.", nameof(request));
        if (!Enum.IsDefined(request.Mode)) throw new ArgumentOutOfRangeException(nameof(request));
        string normalized = TextRules.Normalize(request.Text, ct).Value;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Text = normalized, request.Mode,
            Include = request.Filter.Include.Select(e => e.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray(),
            Exclude = request.Filter.Exclude.Select(e => e.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray()
        }))));
        if (cursor is not null && (cursor.SnapshotId != snapshot.Id || cursor.RequestKey != key || cursor.AfterDocId < 0 || cursor.AfterDocId >= snapshot.Reader.MaxDoc))
            throw new ArgumentException("Cursor belongs to a different snapshot or query.", nameof(cursor));
        if (normalized.Length == 0) return new([], null, true, 0, timer.Elapsed);
        var query = CandidateQueryBuilder.Build(snapshot.Searcher, request, ct);
        var weight = snapshot.Searcher.CreateNormalizedWeight(query);
        int after = cursor?.AfterDocId ?? -1;
        long visited = 0;
        var hits = new List<SearchHit>(Math.Min(pageSize, 1000));
        foreach (var leaf in snapshot.Reader.Leaves)
        {
            ct.ThrowIfCancellationRequested();
            if (leaf.DocBase + leaf.AtomicReader.MaxDoc <= after + 1) continue;
            var scorer = weight.GetScorer(leaf, leaf.AtomicReader.LiveDocs);
            if (scorer is null) continue;
            int local = scorer.Advance(Math.Max(0, after + 1 - leaf.DocBase));
            while (local != DocIdSetIterator.NO_MORE_DOCS)
            {
                ct.ThrowIfCancellationRequested();
                int global = leaf.DocBase + local;
                visited++;
                var line = snapshot.GetLine(global, ct);
                var matches = LiteralMatcher.FindNormalized(line.Text, line.Normalized, normalized, request.Mode, ct);
                if (matches.Count > 0)
                {
                    hits.Add(new(snapshot.FileById(line.FileId).FullPath, line.LineNumber, line.Text, matches));
                    if (hits.Count == pageSize) return new(hits, new(snapshot.Id, key, global), false, visited, timer.Elapsed);
                }
                local = scorer.NextDoc();
            }
        }
        return new(hits, null, true, visited, timer.Elapsed);
    }
}
