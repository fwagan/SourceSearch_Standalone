using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SourceSearch.Core;
namespace SourceSearch.Presentation;
public sealed record ResultRow(string FilePath, int LineNumber, string Text, IReadOnlyList<TextMatch> Matches, bool IsTruncated)
{
    public string DisplayLine => $"{LineNumber}: " + Text + (IsTruncated ? " … [仅显示前 2000 字符]" : "");
    public static ResultRow From(SearchHit hit)
    {
        int length = Math.Min(2000, hit.LineText.Length);
        if (length < hit.LineText.Length && length > 0 && char.IsHighSurrogate(hit.LineText[length - 1]) && char.IsLowSurrogate(hit.LineText[length])) length--;
        var matches = new List<TextMatch>();
        foreach (var match in hit.Matches.OrderBy(m => m.Start))
        {
            if (match.Start >= length) break;
            int start = Math.Max(0, match.Start), end = (int)Math.Min(length, (long)match.Start + match.Length);
            if (end <= start) continue;
            if (matches.Count > 0 && start <= matches[^1].Start + matches[^1].Length)
            { var previous = matches[^1]; matches[^1] = new(previous.Start, Math.Max(end, previous.Start + previous.Length) - previous.Start); }
            else matches.Add(new(start, end - start));
        }
        return new(hit.FilePath, hit.LineNumber, hit.LineText[..length], matches, length < hit.LineText.Length);
    }
}
public sealed class ResultCollection : ObservableCollection<ResultRow>
{
    public void Publish(IEnumerable<ResultRow> rows, bool append)
    {
        CheckReentrancy();
        if (!append) Items.Clear();
        foreach (var row in rows) Items.Add(row);
        OnPropertyChanged(new(nameof(Count)));
        OnPropertyChanged(new("Item[]"));
        OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
    }
}
