using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SourceSearch.Core;
namespace SourceSearch.Presentation;
public sealed record ResultRow(string FilePath, string DisplayLine)
{
    public static ResultRow From(SearchHit hit) => new(hit.FilePath, $"{hit.LineNumber}: " +
        (hit.LineText.Length > 2000 ? hit.LineText[..2000] + " … [仅显示前 2000 字符]" : hit.LineText));
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
