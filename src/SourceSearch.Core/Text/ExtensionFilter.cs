namespace SourceSearch.Core;
public sealed record ExtensionFilter(IReadOnlySet<string> Include, IReadOnlySet<string> Exclude)
{
    public static ExtensionFilter Parse(bool includeEnabled, string includeText, bool excludeEnabled, string excludeText) =>
        new(ParseSet(includeEnabled, includeText), ParseSet(excludeEnabled, excludeText));

    public bool Allows(string extension)
    {
        string value = extension.ToLowerInvariant();
        return !Exclude.Contains(value) && (Include.Count == 0 || Include.Contains(value));
    }

    private static IReadOnlySet<string> ParseSet(bool enabled, string text)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!enabled) return values;
        foreach (string part in text.Split(','))
        {
            string value = part.Trim().ToLowerInvariant();
            if (value.Length == 0) continue;
            if (value.IndexOfAny(['*', '?', '/', '\\', ':', '[', ']']) >= 0 || value.Any(char.IsControl))
                throw new ArgumentException("File types must be literal extensions.", nameof(text));
            value = value.StartsWith('.') ? value : "." + value;
            if (value.Length == 1 || value.AsSpan(1).Contains('.')) throw new ArgumentException("Use the final file extension.", nameof(text));
            values.Add(value);
        }
        return values;
    }
}
