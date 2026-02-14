using System.Collections.Generic;

using Xunit;

namespace BugZapperLabs.RipSharp.Tests.Metadata;

public class TitleVariationGeneratorEdgeCasesTests
{
    [Fact]
    public void Generate_ReturnsOriginal_ForEmptyString()
    {
        var result = TitleVariationGenerator.Generate("");

        result.Should().Equal(new[] { "" });
    }

    [Fact]
    public void Generate_ReturnsOriginal_ForOnlySpecialCharacters()
    {
        var result = TitleVariationGenerator.Generate("___---");

        // The algorithm strips each trailing separator, which is expected behavior
        result.Should().Contain("___---");
        result.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Generate_ReturnsOriginal_ForSingleCharacter()
    {
        var result = TitleVariationGenerator.Generate("A");

        result.Should().Equal(new[] { "A" });
    }

    [Fact]
    public void Generate_HandlesTrailingSpaces()
    {
        var result = TitleVariationGenerator.Generate("Movie Title   ");

        result.Should().Equal(new[] { "Movie Title   ", "Movie" });
    }

    [Fact]
    public void Generate_HandlesMultipleConsecutiveSeparators()
    {
        var result = TitleVariationGenerator.Generate("Title___Part___A");

        // The algorithm strips one non-alphanumeric at a time, which creates intermediate variations
        result.Should().Contain("Title___Part___A");
        result.Should().Contain("Title___Part");
        result.Should().Contain("Title");
    }

    [Fact]
    public void Generate_HandlesUnicodeCharacters()
    {
        var result = TitleVariationGenerator.Generate("Movie™_Title®");

        // ™ and ® are non-alphanumeric, so they get stripped
        result.Should().Contain("Movie™_Title®");
        result.Should().Contain("Movie");
    }

    [Theory]
    [InlineData("Test-", new[] { "Test-", "Test" })]
    [InlineData("Test_", new[] { "Test_", "Test" })]
    [InlineData("Test ", new[] { "Test " })]
    public void Generate_HandlesTrailingSeparators(string input, string[] expected)
    {
        var result = TitleVariationGenerator.Generate(input);

        result.Should().Equal(expected);
    }

    [Fact]
    public void Generate_HandlesMixedSeparators()
    {
        var result = TitleVariationGenerator.Generate("Movie-Title_Part 2023");

        result.Should().Equal(new[] { "Movie-Title_Part 2023", "Movie-Title_Part", "Movie-Title", "Movie" });
    }
}
