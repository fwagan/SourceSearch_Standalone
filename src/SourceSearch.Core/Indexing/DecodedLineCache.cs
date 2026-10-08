using Lucene.Net.Search;
namespace SourceSearch.Core;
internal sealed record DecodedLine(int FileId, int LineNumber, string Text, SearchText Normalized)
{
    public long Size => 256L + 2L * (Text.Length + Normalized.Value.Length) +
        (Normalized.Mapping is { } map ? 4L * (map.OriginalStarts.Length + map.OriginalEnds.Length) : 0);
}
internal sealed class DecodedLineCache(long limit)
{
    private readonly object sync = new();
    private readonly Dictionary<int, LinkedListNode<(int Id, DecodedLine Line)>> entries = [];
    private readonly LinkedList<(int Id, DecodedLine Line)> recency = new();
    private long bytes;
    public long Bytes { get { lock (sync) return bytes; } }
    public DecodedLine Get(IndexSearcher searcher, int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (entries.TryGetValue(id, out var existing))
            { recency.Remove(existing); recency.AddFirst(existing); return existing.Value.Line; }
        }
        var document = searcher.Doc(id);
        string text = document.Get("text");
        var line = new DecodedLine(document.GetField("file_id").GetInt32Value()!.Value,
            document.GetField("line_number").GetInt32Value()!.Value, text, SearchText.Create(text, ct));
        if (line.Size > limit) return line;
        lock (sync)
        {
            if (entries.TryGetValue(id, out var existing)) return existing.Value.Line;
            while (bytes + line.Size > limit && recency.Last is { } last)
            { bytes -= last.Value.Line.Size; entries.Remove(last.Value.Id); recency.RemoveLast(); }
            entries.Add(id, recency.AddFirst((id, line))); bytes += line.Size;
        }
        return line;
    }
    public void Clear() { lock (sync) { entries.Clear(); recency.Clear(); bytes = 0; } }
}
