using System.IO;
using AwesomeAssertions;
using MediaEncoding;
using Xunit;

namespace MediaEncoding.Tests;

public class FileNamingTests
{
    [Fact]
    public void SanitizeFileName_RemovesAllInvalidChars()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var input = "Start" + new string(invalid) + "End";

        var result = FileNaming.SanitizeFileName(input);

        result.Should().Be("StartEnd");
        foreach (var ch in invalid)
        {
            result.Should().NotContain(ch.ToString());
        }
    }

    [Theory]
    [InlineData("  Leading and trailing  ", "Leading and trailing")]
    public void SanitizeFileName_TrimsWhitespace(string input, string expected)
    {
        var result = FileNaming.SanitizeFileName(input);
        result.Should().Be(expected);
    }
}
