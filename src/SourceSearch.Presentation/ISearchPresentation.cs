using SourceSearch.Core;
namespace SourceSearch.Presentation;
public interface ISearchPresentation
{
    Task<TimeSpan> MeasureAsync(Func<Task> updateResults, CancellationToken ct);
}
public sealed record SearchExecution(SearchPage Page, TimeSpan UiElapsed);
