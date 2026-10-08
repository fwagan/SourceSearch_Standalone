namespace SourceSearch.Core.Tests;
public class TextRulesTests
{
    [Fact] public void GramsUseScalarsAndIncludePunctuation() =>
        Assert.Equal(new[] { "a😀", "😀." }, TextRules.EnumerateGrams("a😀.", 2));
    [Fact] public void KeepsIdentifierAsAWholeWord() =>
        Assert.Equal(new[] { "getuser_name2", "用户" }, TextRules.EnumerateWords("getuser_name2(用户)"));
    [Fact] public void NormalizationDoesNotRemoveAccents() => Assert.Equal("é", TextRules.Normalize("É").Value);
}
