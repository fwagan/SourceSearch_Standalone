namespace SourceSearch.Presentation.Tests;
public class ResultRowTests
{
    [Fact]
    public void Highlight_uses_original_offsets_and_merges_overlaps()
    {
        var row = ResultRow.From(new("a.cs", 2, "😀UserUser", [new(2,4), new(5,3)]));
        Assert.Equal("😀UserUser", row.Text); Assert.Equal(new TextMatch(2,6), Assert.Single(row.Matches));
    }
    [Fact]
    public void Truncation_preserves_surrogate_pairs_and_hides_offscreen_matches()
    {
        var row = ResultRow.From(new("a.cs", 2, new string('a',1999)+"😀User", [new(2001,4)]));
        Assert.DoesNotContain('\uD83D', row.Text); Assert.Empty(row.Matches); Assert.Equal(1999, row.Text.Length);
    }
    [Fact]
    public void Crossing_match_is_clipped_to_visible_text()
    {
        var row = ResultRow.From(new("a.cs", 3, new string('a',2010), [new(1998,10)]));
        Assert.Equal(new TextMatch(1998,2), Assert.Single(row.Matches));
    }
}
