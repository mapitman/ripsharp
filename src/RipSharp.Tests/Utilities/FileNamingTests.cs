using System.IO;

using AwesomeAssertions;

using Xunit;

namespace BugZapperLabs.RipSharp.Tests.Utilities;

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

    [Fact]
    public void RenameFile_IncludesSpaceBeforeSuffix()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test");
        var metadata = new ContentMetadata { Title = "The Simpsons Movie", Year = 2007, Type = "movie" };
        var versionSuffix = " - title00";
        string? result = null;

        try
        {
            result = FileNaming.RenameFile(tempFile, metadata, null, 1, versionSuffix, null);

            var filename = Path.GetFileName(result);
            filename.Should().Be("The Simpsons Movie (2007) - title00.mkv");
        }
        finally
        {
            // Cleanup
            if (result != null && File.Exists(result)) File.Delete(result);
        }
    }

    [Fact]
    public void RenameFile_TvWithEpisodeTitle_AppendsEpisodeName()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test");
        var metadata = new ContentMetadata { Title = "Example Show", Year = 2020, Type = "tv" };
        string? result = null;

        try
        {
            result = FileNaming.RenameFile(tempFile, metadata, 2, 1, null, "Pilot Part 2");

            var filename = Path.GetFileName(result);
            filename.Should().Be("Example Show - S01E02 - Pilot Part 2.mkv");
        }
        finally
        {
            if (result != null && File.Exists(result)) File.Delete(result);
        }
    }
}
