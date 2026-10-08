using System.Text;
namespace SourceSearch.Core;
public static class LiteralMatcher
{
    public static IReadOnlyList<TextMatch> Find(string line, string query, MatchMode mode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(line);
        if (query.Contains('\r') || query.Contains('\n')) throw new ArgumentException("Cross-line queries are not supported.", nameof(query));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (query.Length == 0) return Array.Empty<TextMatch>();
        string needle = TextRules.Normalize(query, cancellationToken).Value;
        return FindNormalized(line, SearchText.Create(line, cancellationToken), needle, mode, cancellationToken);
    }
    internal static IReadOnlyList<TextMatch> FindNormalized(string line, SearchText normalized, string needle, MatchMode mode, CancellationToken cancellationToken)
    {
        var matches = new List<TextMatch>();
        int offset = 0;
        while (offset <= normalized.Value.Length - needle.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int found = normalized.Value.IndexOf(needle, offset, StringComparison.Ordinal);
            if (found < 0) break;
            int start = normalized.Mapping?.OriginalStarts[found] ?? found;
            int end = normalized.Mapping?.OriginalEnds[found + needle.Length - 1] ?? found + needle.Length;
            if (mode == MatchMode.Contains || HasWordBoundaries(line, start, end))
                matches.Add(new(start, end - start));
            offset = found + Rune.GetRuneAt(normalized.Value, found).Utf16SequenceLength;
        }
        return matches;
    }

    private static bool HasWordBoundaries(string text, int start, int end)
    {
        if (start > 0)
        {
            Rune.DecodeLastFromUtf16(text.AsSpan(0, start), out var before, out _);
            if (TextRules.IsWordRune(before)) return false;
        }
        return end == text.Length || !TextRules.IsWordRune(Rune.GetRuneAt(text, end));
    }
}
