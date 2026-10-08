using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SourceSearch.Core;
namespace SourceSearch.Desktop;
public sealed record ResultRow(string FilePath, string DisplayLine)
{
    public static ResultRow From(SearchHit hit) => new(hit.FilePath, $"{hit.LineNumber}: " +
        (hit.LineText.Length > 2000 ? hit.LineText[..2000] + " … [仅显示前 2000 字符]" : hit.LineText));
}
public sealed class SearchViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string rootPath = @"C:\Coding\Ftrans\ftrans.main", queryText = "", includeText = "", excludeText = "";
    private string statusText = "尚未建立索引。", timingText = "默认包含匹配；全字匹配使用完整单词边界。";
    private bool wholeWord, includeEnabled, excludeEnabled, canEdit = true, canSearch, canMore;
    public string RootPath { get => rootPath; set => Set(ref rootPath, value); }
    public string QueryText { get => queryText; set => Set(ref queryText, value); }
    public string IncludeText { get => includeText; set => Set(ref includeText, value); }
    public string ExcludeText { get => excludeText; set => Set(ref excludeText, value); }
    public string StatusText { get => statusText; set => Set(ref statusText, value); }
    public string TimingText { get => timingText; set => Set(ref timingText, value); }
    public bool WholeWord { get => wholeWord; set => Set(ref wholeWord, value); }
    public bool IncludeEnabled { get => includeEnabled; set => Set(ref includeEnabled, value); }
    public bool ExcludeEnabled { get => excludeEnabled; set => Set(ref excludeEnabled, value); }
    public bool CanEdit { get => canEdit; set => Set(ref canEdit, value); }
    public bool CanSearch { get => canSearch; set => Set(ref canSearch, value); }
    public bool CanMore { get => canMore; set => Set(ref canMore, value); }
    public ObservableCollection<ResultRow> Results { get; } = [];
    public SearchRequest CreateRequest() => new(QueryText, WholeWord ? MatchMode.WholeWord : MatchMode.Contains,
        ExtensionFilter.Parse(IncludeEnabled, IncludeText, ExcludeEnabled, ExcludeText));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
