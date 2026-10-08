using SourceSearch.Core;
namespace SourceSearch.Validation;
public sealed record QueryCase(string Name, SearchRequest Request, string Group);
public sealed record QueryTiming(string Name, string Layer, int SampleCount, double P50Ms, double P95Ms, double MaxMs, int FirstPageRows, long CandidatesVisited, IReadOnlyList<double> Samples, double FirstInSequenceMs = 0);
public sealed record VerificationResult(string Name, bool Passed, long ExpectedLines, long ActualLines, string? Difference);
public sealed record BenchmarkReport(string Corpus, Guid SnapshotId, string EnvironmentFile, IReadOnlyList<QueryTiming> Timings, IReadOnlyList<VerificationResult> Correctness, bool Passed);
