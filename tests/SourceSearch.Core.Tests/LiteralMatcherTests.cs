namespace SourceSearch.Core.Tests;

public class LiteralMatcherTests
{
    [Theory]
    [InlineData("User", 1, 1)]
    [InlineData("GetUserInfo", 1, 0)]
    [InlineData("User_Name", 1, 0)]
    [InlineData("User2", 1, 0)]
    [InlineData("List<User>", 1, 1)]
    [InlineData("// USER information", 1, 1)]
    [InlineData("User\u0301", 1, 0)]
    public void RespectsIdentifierBoundaries(string line, int contains, int whole)
    {
        Assert.Equal(contains, LiteralMatcher.Find(line, "User", MatchMode.Contains).Count);
        Assert.Equal(whole, LiteralMatcher.Find(line, "User", MatchMode.WholeWord).Count);
    }

    [Fact] public void MapsSupplementaryCharactersToOriginalOffsets() =>
        Assert.Equal(new TextMatch(2, 3), Assert.Single(LiteralMatcher.Find("😀𐐀User", "𐐨u", MatchMode.Contains)));

    [Fact] public void PreservesOverlappingMatches() => Assert.Equal(
        new[] { new TextMatch(0, 2), new TextMatch(1, 2) }, LiteralMatcher.Find("aaa", "aa", MatchMode.Contains));

    [Theory]
    [InlineData("a.b axb", "a.b", 1)]
    [InlineData("用户服务 用户", "用户", 2)]
    [InlineData("  ", " ", 2)]
    [InlineData("x", "", 0)]
    [InlineData("é", "e", 0)]
    public void SearchesLiteralText(string line, string query, int count) =>
        Assert.Equal(count, LiteralMatcher.Find(line, query, MatchMode.Contains).Count);

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void RejectsCrossLineQuery(string query) => Assert.Throws<ArgumentException>(() => LiteralMatcher.Find("a b", query, MatchMode.Contains));

    [Fact] public void RejectsUnpairedSurrogate() => Assert.Throws<ArgumentException>(() => LiteralMatcher.Find("abc", "\uD800", MatchMode.Contains));
    [Fact] public void ObservesCancellation() => Assert.Throws<OperationCanceledException>(() => LiteralMatcher.Find("aaa", "a", MatchMode.Contains, new CancellationToken(true)));
}
