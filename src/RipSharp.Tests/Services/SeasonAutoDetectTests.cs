using System.Reflection;

namespace RipSharp.Tests.Services;

public class SeasonAutoDetectTests
{
    [Theory]
    [InlineData("/Volumes/data/TV/Veep/Season 2", 2)]
    [InlineData("/Volumes/data/TV/Veep/Season 1", 1)]
    [InlineData("/Volumes/data/TV/Veep/Season 10", 10)]
    [InlineData("/media/tv/The Office/Season 3", 3)]
    public void AutoDetectSeason_SeasonInDirName_DetectsSeason(string outputPath, int expectedSeason)
    {
        var options = new RipOptions { Output = outputPath };
        InvokeAutoDetect(options);
        options.Season.Should().Be(expectedSeason);
    }

    [Theory]
    [InlineData("/Volumes/data/TV/Veep/season 2", 2)]
    [InlineData("/Volumes/data/TV/Veep/SEASON 5", 5)]
    [InlineData("/Volumes/data/TV/Veep/Season2", 2)]
    public void AutoDetectSeason_CaseInsensitive(string outputPath, int expectedSeason)
    {
        var options = new RipOptions { Output = outputPath };
        InvokeAutoDetect(options);
        options.Season.Should().Be(expectedSeason);
    }

    [Theory]
    [InlineData("/Volumes/data/TV/Veep/S02", 2)]
    [InlineData("/Volumes/data/TV/Veep/S1", 1)]
    [InlineData("/Volumes/data/TV/Veep/s03", 3)]
    public void AutoDetectSeason_ShortForm_DetectsSeason(string outputPath, int expectedSeason)
    {
        var options = new RipOptions { Output = outputPath };
        InvokeAutoDetect(options);
        options.Season.Should().Be(expectedSeason);
    }

    [Theory]
    [InlineData("/Volumes/data/TV/Veep")]
    [InlineData("/Volumes/data/TV/Veep/Extras")]
    [InlineData("/tmp/movies")]
    public void AutoDetectSeason_NoSeasonInDirName_RemainsNull(string outputPath)
    {
        var options = new RipOptions { Output = outputPath };
        InvokeAutoDetect(options);
        options.Season.Should().BeNull();
    }

    [Theory]
    [InlineData("VEEP_S2_D1", 2)]
    [InlineData("BREAKING_BAD_SEASON_3_DISC_1", 3)]
    [InlineData("THE_OFFICE_S04_D2", 4)]
    [InlineData("SHOW_Season1", 1)]
    public void AutoDetectSeason_FromDiscName_DetectsSeason(string discName, int expectedSeason)
    {
        var options = new RipOptions { Output = "/tmp/output" };
        InvokeAutoDetect(options, discName);
        options.Season.Should().Be(expectedSeason);
    }

    [Fact]
    public void AutoDetectSeason_DirNameTakesPriorityOverDiscName()
    {
        var options = new RipOptions { Output = "/Volumes/data/TV/Veep/Season 3" };
        InvokeAutoDetect(options, "VEEP_S2_D1");
        options.Season.Should().Be(3);
    }

    [Fact]
    public void AutoDetectSeason_FallsBackToDiscNameWhenDirHasNoSeason()
    {
        var options = new RipOptions { Output = "/Volumes/data/TV/Veep" };
        InvokeAutoDetect(options, "VEEP_S2_D1");
        options.Season.Should().Be(2);
    }

    [Fact]
    public void AutoDetectSeason_NeitherSource_RemainsNull()
    {
        var options = new RipOptions { Output = "/Volumes/data/TV/Veep" };
        InvokeAutoDetect(options, "VEEP_DISC_1");
        options.Season.Should().BeNull();
    }

    private static void InvokeAutoDetect(RipOptions options, string? discName = null)
    {
        var scanner = Substitute.For<IDiscScanner>();
        var encoder = Substitute.For<IEncoderService>();
        var metadata = Substitute.For<IMetadataService>();
        var makeMkv = Substitute.For<IMakeMkvService>();
        var notifier = Substitute.For<IConsoleWriter>();
        var userPrompt = Substitute.For<IUserPrompt>();
        var episodeTitles = Substitute.For<ITvEpisodeTitleProvider>();
        var progressDisplay = Substitute.For<IProgressDisplay>();
        var theme = ThemeProvider.CreateDefault();

        var ripper = new DiscRipper(scanner, encoder, metadata, makeMkv, notifier, userPrompt, episodeTitles, progressDisplay, theme);

        var discInfo = new DiscInfo { DiscName = discName ?? "" };

        var method = typeof(DiscRipper).GetMethod("AutoDetectSeason", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("AutoDetectSeason should exist as a private method");
        method!.Invoke(ripper, new object[] { options, discInfo });
    }
}
