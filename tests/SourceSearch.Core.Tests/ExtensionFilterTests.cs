namespace SourceSearch.Core.Tests;
public class ExtensionFilterTests
{
    [Fact] public void ExclusionWins()
    {
        var filter = ExtensionFilter.Parse(true, "cs, .JSON,cs, ", true, ".cs");
        Assert.False(filter.Allows(".CS"));
        Assert.True(filter.Allows(".json"));
        Assert.False(filter.Allows(".go"));
    }
    [Fact] public void DisabledOrEmptyFiltersDoNotRestrict()
    {
        Assert.True(ExtensionFilter.Parse(false, "cs", false, "json").Allows(".go"));
        Assert.True(ExtensionFilter.Parse(true, " , ", true, "").Allows(""));
    }
    [Fact] public void WildcardIsNotAnExtension() => Assert.Throws<ArgumentException>(() => ExtensionFilter.Parse(true, "*.cs", false, ""));
}
