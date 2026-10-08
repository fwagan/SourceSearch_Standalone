namespace SourceSearch.Core;
public enum MatchMode { Contains, WholeWord }
public readonly record struct TextMatch(int Start, int Length);
public sealed record SearchRequest(string Text, MatchMode Mode, ExtensionFilter Filter);
public sealed record SearchHit(string FilePath, int LineNumber, string LineText, IReadOnlyList<TextMatch> Matches);
public sealed record SearchCursor(Guid SnapshotId, string RequestKey, int AfterDocId);
public sealed record SearchPage(IReadOnlyList<SearchHit> Hits, SearchCursor? Next, bool Exhausted, long CandidatesVisited, TimeSpan Elapsed);
