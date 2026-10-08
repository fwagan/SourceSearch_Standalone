using SourceSearch.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SourceSearch.Validation;
public sealed class CorrectnessVerifier(ISearchService search)
{
    public async Task<IReadOnlyList<VerificationResult>> VerifyAsync(IndexSnapshot snapshot, IReadOnlyList<QueryCase> queries, CancellationToken ct)
    {
        var expected = queries.Select(_ => new HitDigest()).ToArray();
        var needles = queries.Select(q => ReferenceMatcher.PrepareQuery(q.Request.Text)).ToArray();
        var reader = new StrictTextReader();
        int processed = 0;
        foreach (var file in snapshot.Report.Files)
        {
            ct.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(new(file.FullPath, Path.GetFileName(file.FullPath), file.Extension), Math.Max(file.Bytes, 1), ct).ConfigureAwait(false);
            if (read.Content is null || read.Content.Sha256 != file.Sha256)
                return queries.Select(q => new VerificationResult(q.Name, false, 0, 0, "CorpusChanged: " + file.FullPath)).ToArray();
            using var lines = new StringReader(read.Content.Text);
            int number = 0;
            string? line;
            while ((line = lines.ReadLine()) is not null)
            {
                number++;
                var prepared = new ReferenceMatcher.PreparedLine(line);
                for (int i = 0; i < queries.Count; i++)
                {
                    if (!queries[i].Request.Filter.Allows(file.Extension)) continue;
                    var matches = prepared.Find(needles[i], queries[i].Request.Mode, ct);
                    if (matches.Count > 0) expected[i].Add(new(file.FullPath, number, line, matches));
                }
            }
            processed++;
            if (processed % 500 == 0) Console.Error.WriteLine($"Oracle: {processed}/{snapshot.Report.Files.Count} files");
        }
        var results = new List<VerificationResult>();
        for (int i = 0; i < queries.Count; i++)
        {
            var actual = new HitDigest();
            SearchCursor? cursor = null;
            do
            {
                var page = await search.SearchAsync(snapshot, queries[i].Request, cursor, 1000, ct).ConfigureAwait(false);
                foreach (var hit in page.Hits) actual.Add(hit);
                cursor = page.Next;
            } while (cursor is not null);
            bool passed = expected[i].Equivalent(actual);
            results.Add(new(queries[i].Name, passed, expected[i].Rows, actual.Rows, passed ? null : "Hit multiset differs (path, line, original text or offsets)."));
            Console.Error.WriteLine($"Verify {i + 1}/{queries.Count}: {queries[i].Name}: {(passed ? "PASS" : "FAIL")} ({actual.Rows} lines)");
        }
        return results;
    }

    // 逐条覆盖所有结果，使用256位摘要的多重集合累加，避免高频词的全量结果常驻内存。
    private sealed class HitDigest
    {
        private readonly byte[] sum = new byte[32];
        public long Rows { get; private set; }
        private long matches;
        public void Add(SearchHit hit)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hit)));
            int carry = 0;
            for (int i = 0; i < sum.Length; i++) { int value = sum[i] + hash[i] + carry; sum[i] = (byte)value; carry = value >> 8; }
            Rows++; matches += hit.Matches.Count;
        }
        public bool Equivalent(HitDigest other) => Rows == other.Rows && matches == other.matches && sum.AsSpan().SequenceEqual(other.sum);
    }
}
