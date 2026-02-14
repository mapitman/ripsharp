using System.Collections.Generic;

using Xunit;

namespace BugZapperLabs.RipSharp.Tests.Metadata;

public class TitleVariationGeneratorTests
{
    [Theory]
    [InlineData("SIMPSONS_WS", new[] { "SIMPSONS_WS", "SIMPSONS" })]
    [InlineData("Movie_Title_2023", new[] { "Movie_Title_2023", "Movie_Title", "Movie" })]
    [InlineData("Title-Part-A", new[] { "Title-Part-A", "Title-Part", "Title" })]
    [InlineData("Simple", new[] { "Simple" })]
    [InlineData("Title  With  Spaces", new[] { "Title  With  Spaces", "Title  With", "Title" })]
    public void Generate_CreatesCorrectVariations(string input, string[] expected)
    {
        var result = TitleVariationGenerator.Generate(input);

        result.Should().Equal(expected);
    }
}
