using System;
using System.IO;
using AwesomeAssertions;
using MediaEncoding;
using Xunit;

namespace MediaEncoding.Tests;

public class RipOptionsTests
{
    [Fact]
    public void ParseArgs_WithNoArguments_SetsShowHelpTrue()
    {
        var args = Array.Empty<string>();

        var result = RipOptions.ParseArgs(args);

        result.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithHelpShortFlag_SetsShowHelpTrue()
    {
        var args = new[] { "-h" };

        var result = RipOptions.ParseArgs(args);

        result.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithHelpLongFlag_SetsShowHelpTrue()
    {
        var args = new[] { "--help" };

        var result = RipOptions.ParseArgs(args);

        result.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithHelpAmongOtherArgs_SetsShowHelpTrue()
    {
        var args = new[] { "--output", "/tmp", "--help", "--mode", "movie" };

        var result = RipOptions.ParseArgs(args);

        result.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithoutOutput_ThrowsArgumentException()
    {
        var args = new[] { "--mode", "movie" };

        Action act = () => RipOptions.ParseArgs(args);

        act.Should().Throw<ArgumentException>().WithMessage("--output is required");
    }

    [Fact]
    public void ParseArgs_WithEmptyOutput_ThrowsArgumentException()
    {
        var args = new[] { "--output", "", "--mode", "movie" };

        Action act = () => RipOptions.ParseArgs(args);

        act.Should().Throw<ArgumentException>().WithMessage("--output is required");
    }

    [Fact]
    public void ParseArgs_WithWhitespaceOutput_ThrowsArgumentException()
    {
        var args = new[] { "--output", "   ", "--mode", "movie" };

        Action act = () => RipOptions.ParseArgs(args);

        act.Should().Throw<ArgumentException>().WithMessage("--output is required");
    }

    [Fact]
    public void ParseArgs_WithValidOutput_SetsOutputProperty()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Output.Should().Be("/tmp/movies");
    }

    [Fact]
    public void ParseArgs_WithoutTemp_DefaultsToOutputDotMakemkv()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Temp.Should().Be(Path.Combine("/tmp/movies", ".makemkv"));
    }

    [Fact]
    public void ParseArgs_WithTemp_SetsCustomTempDirectory()
    {
        var args = new[] { "--output", "/tmp/movies", "--temp", "/mnt/large-disk/temp" };

        var result = RipOptions.ParseArgs(args);

        result.Temp.Should().Be("/mnt/large-disk/temp");
    }

    [Fact]
    public void ParseArgs_WithoutDisc_DefaultsToDisc0()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Disc.Should().Be("disc:0");
    }

    [Fact]
    public void ParseArgs_WithDisc_SetsCustomDisc()
    {
        var args = new[] { "--output", "/tmp/movies", "--disc", "disc:1" };

        var result = RipOptions.ParseArgs(args);

        result.Disc.Should().Be("disc:1");
    }

    [Fact]
    public void ParseArgs_WithDiscDevice_SetsDevicePath()
    {
        var args = new[] { "--output", "/tmp/movies", "--disc", "/dev/sr0" };

        var result = RipOptions.ParseArgs(args);

        result.Disc.Should().Be("/dev/sr0");
    }

    [Fact]
    public void ParseArgs_WithModeMovie_SetsTvFalse()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "movie" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithModeFilm_SetsTvFalse()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "film" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithModeTv_SetsTvTrue()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "tv" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithModeSeries_SetsTvTrue()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "series" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithModeUpperCase_IsCaseInsensitive()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "MOVIE" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithModeMixedCase_IsCaseInsensitive()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "TV" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithInvalidMode_ThrowsArgumentException()
    {
        var args = new[] { "--output", "/tmp/movies", "--mode", "invalid" };

        Action act = () => RipOptions.ParseArgs(args);

        act.Should().Throw<ArgumentException>().WithMessage("--mode must be 'movie' or 'tv'");
    }

    [Fact]
    public void ParseArgs_WithTvFlag_SetsTvTrue()
    {
        var args = new[] { "--output", "/tmp/movies", "--tv" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithoutTvFlag_DefaultsToFalse()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Tv.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithTitle_SetsTitleProperty()
    {
        var args = new[] { "--output", "/tmp/movies", "--title", "The Matrix" };

        var result = RipOptions.ParseArgs(args);

        result.Title.Should().Be("The Matrix");
    }

    [Fact]
    public void ParseArgs_WithoutTitle_LeavesNull()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Title.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_WithYear_ParsesYearCorrectly()
    {
        var args = new[] { "--output", "/tmp/movies", "--year", "1999" };

        var result = RipOptions.ParseArgs(args);

        result.Year.Should().Be(1999);
    }

    [Fact]
    public void ParseArgs_WithInvalidYear_LeavesNull()
    {
        var args = new[] { "--output", "/tmp/movies", "--year", "not-a-year" };

        var result = RipOptions.ParseArgs(args);

        result.Year.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_WithoutYear_LeavesNull()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Year.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_WithSeason_ParsesSeasonCorrectly()
    {
        var args = new[] { "--output", "/tmp/movies", "--season", "5" };

        var result = RipOptions.ParseArgs(args);

        result.Season.Should().Be(5);
    }

    [Fact]
    public void ParseArgs_WithInvalidSeason_KeepsDefault()
    {
        var args = new[] { "--output", "/tmp/movies", "--season", "not-a-number" };

        var result = RipOptions.ParseArgs(args);

        result.Season.Should().Be(1);
    }

    [Fact]
    public void ParseArgs_WithoutSeason_DefaultsTo1()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Season.Should().Be(1);
    }

    [Fact]
    public void ParseArgs_WithEpisodeStart_ParsesCorrectly()
    {
        var args = new[] { "--output", "/tmp/movies", "--episode-start", "3" };

        var result = RipOptions.ParseArgs(args);

        result.EpisodeStart.Should().Be(3);
    }

    [Fact]
    public void ParseArgs_WithInvalidEpisodeStart_KeepsDefault()
    {
        var args = new[] { "--output", "/tmp/movies", "--episode-start", "invalid" };

        var result = RipOptions.ParseArgs(args);

        result.EpisodeStart.Should().Be(1);
    }

    [Fact]
    public void ParseArgs_WithoutEpisodeStart_DefaultsTo1()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.EpisodeStart.Should().Be(1);
    }

    [Fact]
    public void ParseArgs_WithDebugFlag_SetsDebugTrue()
    {
        var args = new[] { "--output", "/tmp/movies", "--debug" };

        var result = RipOptions.ParseArgs(args);

        result.Debug.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WithoutDebugFlag_DefaultsToFalse()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.Debug.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithDiscType_SetsDiscTypeProperty()
    {
        var args = new[] { "--output", "/tmp/movies", "--disc-type", "uhd" };

        var result = RipOptions.ParseArgs(args);

        result.DiscType.Should().Be("uhd");
    }

    [Fact]
    public void ParseArgs_WithDiscTypeDvd_SetsCorrectly()
    {
        var args = new[] { "--output", "/tmp/movies", "--disc-type", "dvd" };

        var result = RipOptions.ParseArgs(args);

        result.DiscType.Should().Be("dvd");
    }

    [Fact]
    public void ParseArgs_WithDiscTypeBd_SetsCorrectly()
    {
        var args = new[] { "--output", "/tmp/movies", "--disc-type", "bd" };

        var result = RipOptions.ParseArgs(args);

        result.DiscType.Should().Be("bd");
    }

    [Fact]
    public void ParseArgs_WithoutDiscType_LeavesNull()
    {
        var args = new[] { "--output", "/tmp/movies" };

        var result = RipOptions.ParseArgs(args);

        result.DiscType.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_WithAllOptions_ParsesAllCorrectly()
    {
        var args = new[]
        {
            "--output", "/tmp/movies",
            "--disc", "disc:1",
            "--temp", "/tmp/custom",
            "--mode", "tv",
            "--title", "Breaking Bad",
            "--year", "2008",
            "--season", "3",
            "--episode-start", "5",
            "--disc-type", "bd",
            "--debug"
        };

        var result = RipOptions.ParseArgs(args);

        result.Output.Should().Be("/tmp/movies");
        result.Disc.Should().Be("disc:1");
        result.Temp.Should().Be("/tmp/custom");
        result.Tv.Should().BeTrue();
        result.Title.Should().Be("Breaking Bad");
        result.Year.Should().Be(2008);
        result.Season.Should().Be(3);
        result.EpisodeStart.Should().Be(5);
        result.DiscType.Should().Be("bd");
        result.Debug.Should().BeTrue();
        result.ShowHelp.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_WithMissingValueForOption_UsesDefault()
    {
        // When --disc is last without a value, it should keep default
        var args = new[] { "--output", "/tmp/movies", "--disc" };

        var result = RipOptions.ParseArgs(args);

        result.Disc.Should().Be("disc:0");
    }

    [Fact]
    public void ParseArgs_MovieScenario_ParsesCorrectly()
    {
        var args = new[]
        {
            "--output", "~/Movies",
            "--mode", "movie",
            "--title", "The Matrix",
            "--year", "1999"
        };

        var result = RipOptions.ParseArgs(args);

        result.Output.Should().Be("~/Movies");
        result.Tv.Should().BeFalse();
        result.Title.Should().Be("The Matrix");
        result.Year.Should().Be(1999);
        result.Temp.Should().Be(Path.Combine("~/Movies", ".makemkv"));
    }

    [Fact]
    public void ParseArgs_TvScenario_ParsesCorrectly()
    {
        var args = new[]
        {
            "--output", "~/TV",
            "--mode", "tv",
            "--title", "Breaking Bad",
            "--season", "1"
        };

        var result = RipOptions.ParseArgs(args);

        result.Output.Should().Be("~/TV");
        result.Tv.Should().BeTrue();
        result.Title.Should().Be("Breaking Bad");
        result.Season.Should().Be(1);
        result.EpisodeStart.Should().Be(1);
    }
}
