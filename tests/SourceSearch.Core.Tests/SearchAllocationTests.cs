using System.Text;
using Xunit;
namespace SourceSearch.Core.Tests;
public sealed class SearchAllocationTests
{
    [Fact]
    public void Long_nonmatching_line_does_not_allocate_per_character_objects_or_offset_arrays()
    {
        string line = new string('A', 1_000_000) + "用户𐐀";
        LiteralMatcher.Find("warmup", "absent", MatchMode.Contains);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Empty(LiteralMatcher.Find(line, "absent", MatchMode.Contains));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < line.Length * 4L, $"Allocated {allocated} bytes for {line.Length} characters.");
    }
    [Fact]
    public void Invalid_source_utf16_is_rejected()
    {
        foreach (char invalid in new[] { (char)0xd800, (char)0xdc00 })
            Assert.Throws<ArgumentException>(() => LiteralMatcher.Find(new string(invalid, 1), "a", MatchMode.Contains));
    }

    [Fact]
    public void Search_folding_matches_scalar_folding_for_all_valid_unicode_scalars()
    {
        var input = new StringBuilder(); var expected = new StringBuilder();
        for (int value = 0; value <= 0x10ffff; value++)
        {
            if (!Rune.IsValid(value)) continue;
            var rune = new Rune(value); input.Append(rune.ToString()); expected.Append(Rune.ToLowerInvariant(rune).ToString());
        }
        Assert.Equal(expected.ToString(), SearchText.Create(input.ToString(), default).Value);
    }
    [Fact]
    public async Task Decoded_line_cache_has_a_byte_limit_and_does_not_change_results()
    {
        using var corpus = new TestCorpus();
        corpus.Write("a.cs", string.Join('\n', Enumerable.Repeat(new string('x', 1000) + " User", 20)));
        using var built = (await corpus.BuildAsync()).Snapshot;
        using var limited = IndexSnapshot.Open(built.DirectoryPath, 10_000);
        var request = new SearchRequest("User", MatchMode.Contains, ExtensionFilter.Parse(false, "", false, ""));
        var page = await new SearchService().SearchAsync(limited, request, null, 100, default);
        Assert.Equal(20, page.Hits.Count);
        Assert.InRange(limited.DecodedCacheBytes, 1, 10_000);
        using var uncached = IndexSnapshot.Open(built.DirectoryPath, 0);
        var other = await new SearchService().SearchAsync(uncached, request, null, 100, default);
        Assert.Equal(page.Hits.Select(h => (h.LineNumber, h.LineText)), other.Hits.Select(h => (h.LineNumber, h.LineText)));
        Assert.Equal(0, uncached.DecodedCacheBytes);
    }
}
