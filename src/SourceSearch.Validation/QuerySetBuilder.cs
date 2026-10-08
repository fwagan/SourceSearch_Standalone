using SourceSearch.Core;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SourceSearch.Validation;
public static class QuerySetBuilder
{
    public static IReadOnlyList<QueryCase> Create(IndexSnapshot snapshot)
    {
        string identifier = "LongActualUserService";
        foreach (var file in snapshot.Report.Files.Where(f => f.Extension == ".cs").Take(500))
        {
            string text = File.ReadAllText(file.FullPath);
            var match = Regex.Match(text, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]{15,})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (match.Success) { identifier = match.Groups[1].Value; break; }
        }
        string[] texts = ["User", "Id", "IO", "a", "Service", "public", "class", "return", "Task", "async", "用户", "权限", "a.b", identifier, identifier.Substring(3, Math.Min(8, identifier.Length - 3)), "SourceSearchAbsent_7c021065b9954dd8a01fbd6b129fb24a"];
        var queries = new List<QueryCase>();
        for (int i = 0; i < texts.Length; i++)
            foreach (var mode in new[] { MatchMode.Contains, MatchMode.WholeWord })
                queries.Add(new($"q{i + 1:D2}-{mode}", new(texts[i], mode, ExtensionFilter.Parse(false, "", false, "")), i is 1 or 2 or 3 ? "short" : "general"));
        queries.Add(new("filter-include-cs", new("User", MatchMode.Contains, ExtensionFilter.Parse(true, "cs", false, "")), "filter"));
        queries.Add(new("filter-exclude-cs", new("User", MatchMode.Contains, ExtensionFilter.Parse(false, "", true, "cs")), "filter"));
        queries.Add(new("filter-js-json", new("User", MatchMode.Contains, ExtensionFilter.Parse(true, "js,json", false, "")), "filter"));
        queries.Add(new("filter-overlap", new("User", MatchMode.Contains, ExtensionFilter.Parse(true, "cs,js", true, "cs")), "filter"));
        return queries;
    }
    public static void Write(string path, IReadOnlyList<QueryCase> queries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(queries, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static IReadOnlyList<QueryCase> Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var request = item.GetProperty("Request");
            var filter = request.GetProperty("Filter");
            var include = filter.GetProperty("Include").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var exclude = filter.GetProperty("Exclude").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new QueryCase(item.GetProperty("Name").GetString()!, new(request.GetProperty("Text").GetString()!, (MatchMode)request.GetProperty("Mode").GetInt32(), new(include, exclude)), item.GetProperty("Group").GetString()!);
        }).ToArray();
    }
}
