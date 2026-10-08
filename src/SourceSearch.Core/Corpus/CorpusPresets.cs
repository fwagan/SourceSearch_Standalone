namespace SourceSearch.Core;
public static class CorpusPresets
{
    public static IReadOnlySet<string> CSharp { get; } = new HashSet<string>([".cs"], StringComparer.OrdinalIgnoreCase);
    public static IReadOnlySet<string> Mixed { get; } = new HashSet<string>([".cs", ".csproj", ".props", ".targets", ".sln", ".slnx", ".js", ".ts", ".tsx", ".jsx", ".vue", ".html", ".cshtml", ".css", ".scss", ".less", ".json", ".xml", ".resx", ".config", ".yaml", ".yml", ".md", ".txt", ".sql", ".ps1", ".psm1", ".sh", ".go", ".mod", ".sum", ".c", ".h", ".cpp", ".hpp", ".py"], StringComparer.OrdinalIgnoreCase);
}
