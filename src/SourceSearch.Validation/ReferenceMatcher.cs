using SourceSearch.Core;
using System.Text;
using System.Globalization;
namespace SourceSearch.Validation;
public static class ReferenceMatcher
{
    public static IReadOnlyList<TextMatch> Find(string line, string query, MatchMode mode) => new PreparedLine(line).Find(PrepareQuery(query), mode, default);
    internal static int[] PrepareQuery(string query) => query.EnumerateRunes().Select(r => Rune.ToLowerInvariant(r).Value).ToArray();
    internal sealed class PreparedLine
    {
        private readonly int[] folded;
        private readonly int[] starts;
        private readonly int[] ends;
        private readonly bool[] word;
        public PreparedLine(string text)
        {
            Rune[] runes = text.EnumerateRunes().ToArray();
            folded = new int[runes.Length]; starts = new int[runes.Length]; ends = new int[runes.Length]; word = new bool[runes.Length];
            int offset = 0;
            for (int i = 0; i < runes.Length; i++)
            {
                folded[i] = Rune.ToLowerInvariant(runes[i]).Value;
                starts[i] = offset; offset += runes[i].Utf16SequenceLength; ends[i] = offset;
                // 独立定义边界，不调用被校验实现的词字符或规范化函数。
                word[i] = Rune.GetUnicodeCategory(runes[i]) switch
                {
                    UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
                    UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
                    UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or
                    UnicodeCategory.ConnectorPunctuation => true,
                    _ => false
                };
            }
        }
        public IReadOnlyList<TextMatch> Find(int[] query, MatchMode mode, CancellationToken ct)
        {
            if (query.Length == 0) return Array.Empty<TextMatch>();
            var result = new List<TextMatch>();
            for (int at = 0; at <= folded.Length - query.Length; at++)
            {
                if ((at & 4095) == 0) ct.ThrowIfCancellationRequested();
                if (folded[at] != query[0]) continue;
                bool matches = true;
                for (int j = 1; j < query.Length; j++) if (folded[at + j] != query[j]) { matches = false; break; }
                if (!matches) continue;
                int after = at + query.Length;
                if (mode == MatchMode.WholeWord && ((at > 0 && word[at - 1]) || (after < word.Length && word[after]))) continue;
                result.Add(new(starts[at], ends[after - 1] - starts[at]));
            }
            return result;
        }
    }
}
