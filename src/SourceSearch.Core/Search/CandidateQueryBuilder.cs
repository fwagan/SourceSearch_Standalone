using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Search;
namespace SourceSearch.Core;
internal static class CandidateQueryBuilder
{
    internal static Query Build(IndexSearcher searcher, SearchRequest request, CancellationToken ct)
    {
        string normalized = TextRules.Normalize(request.Text, ct).Value;
        if (normalized.Length == 0) return new BooleanQuery();
        Query candidate;
        if (request.Mode == MatchMode.WholeWord && normalized.EnumerateRunes().All(TextRules.IsWordRune) && Encoding.UTF8.GetByteCount(normalized) <= IndexWriter.MAX_TERM_LENGTH)
            candidate = new TermQuery(new Term("words", normalized));
        else
        {
            int width = Math.Min(3, normalized.EnumerateRunes().Count());
            string field = "gram" + width;
            var grams = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string gram in TextRules.EnumerateGrams(normalized, width))
            {
                ct.ThrowIfCancellationRequested();
                if (grams.ContainsKey(gram)) continue;
                int count = searcher.IndexReader.DocFreq(new Term(field, gram));
                if (count == 0) return new BooleanQuery();
                grams.Add(gram, count);
            }
            var intersection = new BooleanQuery();
            foreach (var gram in grams.OrderBy(g => g.Value).ThenBy(g => g.Key, StringComparer.Ordinal).Take(32))
                intersection.Add(new TermQuery(new Term(field, gram.Key)), Occur.MUST);
            candidate = intersection;
        }
        var filtered = new BooleanQuery { { candidate, Occur.MUST } };
        if (request.Filter.Include.Count > 0) filtered.Add(ExtensionUnion(request.Filter.Include), Occur.MUST);
        if (request.Filter.Exclude.Count > 0) filtered.Add(ExtensionUnion(request.Filter.Exclude), Occur.MUST_NOT);
        return new ConstantScoreQuery(filtered);
    }

    // 分层 OR 将每个节点限制在32个子句，不修改引擎全局限制，也不额外引入查询包。
    private static Query ExtensionUnion(IEnumerable<string> extensions)
    {
        List<Query> nodes = extensions.Select(e => e.ToLowerInvariant()).Distinct(StringComparer.Ordinal)
            .Select(e => (Query)new TermQuery(new Term("extension", e))).ToList();
        while (nodes.Count > 1)
        {
            var next = new List<Query>();
            foreach (var group in nodes.Chunk(32))
            {
                var query = new BooleanQuery();
                foreach (var term in group) query.Add(term, Occur.SHOULD);
                next.Add(query);
            }
            nodes = next;
        }
        return nodes.Count == 0 ? new BooleanQuery() : nodes[0];
    }
}
