using System.Text;
using System.Buffers;
using System.Globalization;
namespace SourceSearch.Core;
public static class TextRules
{
    /// <summary>按标量转小写，保留原始 UTF-16 范围，避免代理对造成匹配偏移错位。</summary>
    public static NormalizedText Normalize(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = new StringBuilder(text.Length);
        var starts = new List<int>(text.Length);
        var ends = new List<int>(text.Length);
        for (int offset = 0; offset < text.Length;)
        {
            if ((offset & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (Rune.DecodeFromUtf16(text.AsSpan(offset), out var rune, out int consumed) != OperationStatus.Done)
                throw new ArgumentException("Text contains an invalid UTF-16 sequence.", nameof(text));
            string folded = Rune.ToLowerInvariant(rune).ToString();
            value.Append(folded);
            for (int i = 0; i < folded.Length; i++) { starts.Add(offset); ends.Add(offset + consumed); }
            offset += consumed;
        }
        return new(value.ToString(), starts.ToArray(), ends.ToArray());
    }

    public static bool IsWordRune(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
        UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or
        UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation;

    public static IEnumerable<string> EnumerateWords(string normalized)
    {
        int offset = 0, start = -1;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (IsWordRune(rune)) { if (start < 0) start = offset; }
            else if (start >= 0) { yield return normalized[start..offset]; start = -1; }
            offset += rune.Utf16SequenceLength;
        }
        if (start >= 0) yield return normalized[start..];
    }

    public static IEnumerable<string> EnumerateGrams(string normalized, int width)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        int start = 0;
        while (start < normalized.Length)
        {
            int end = start, count = 0;
            while (end < normalized.Length && count < width)
            {
                var rune = Rune.GetRuneAt(normalized, end);
                end += rune.Utf16SequenceLength;
                count++;
            }
            if (count == width) yield return normalized[start..end];
            start += Rune.GetRuneAt(normalized, start).Utf16SequenceLength;
        }
    }
}
