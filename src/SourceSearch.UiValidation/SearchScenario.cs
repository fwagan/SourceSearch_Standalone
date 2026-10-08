using SourceSearch.Desktop;
using SourceSearch.Core;
using SourceSearch.Presentation;
using SourceSearch.Validation;
namespace SourceSearch.UiValidation;
internal static class SearchScenario
{
    internal static Task<SearchExecution> ExecuteAsync(MainWindow window, QueryCase query, CancellationToken ct)
    {
        var model = window.Model;
        model.QueryText = query.Request.Text; model.WholeWord = query.Request.Mode == MatchMode.WholeWord;
        model.IncludeText = string.Join(',', query.Request.Filter.Include); model.IncludeEnabled = query.Request.Filter.Include.Count > 0;
        model.ExcludeText = string.Join(',', query.Request.Filter.Exclude); model.ExcludeEnabled = query.Request.Filter.Exclude.Count > 0;
        return model.SearchAsync(ct: ct);
    }
}
