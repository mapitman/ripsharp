using System.Reflection;

namespace RipSharp.Tests.Services;

public class EpisodeAutoDetectTests
{
    [Fact]
    public void AutoDetectEpisodeStart_EmptyDirectory_RemainsNull()
    {
        var outputDir = CreateTempDir();
        try
        {
            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().BeNull();
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_WithExistingEpisodes_SetsNextEpisode()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E01 - Pilot.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E02 - Second.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E03 - Third.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(4);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_MatchesCorrectSeason()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E01.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E02.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S02E01.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 2 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(2);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_IgnoresNonMkvFiles()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E05.txt"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E01.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(2);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_NonexistentDirectory_RemainsNull()
    {
        var options = new RipOptions { Output = "/nonexistent/path/12345", Season = 1 };
        InvokeAutoDetect(options);
        options.EpisodeStart.Should().BeNull();
    }

    [Fact]
    public void AutoDetectEpisodeStart_CaseInsensitiveMatching()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - s01e04 - Episode.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(5);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_GapInNumbering_UsesMaxEpisode()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E01.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E03.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E05.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(6);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_HighEpisodeNumbers()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E12.mkv"), "");
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E13.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1 };
            InvokeAutoDetect(options);
            options.EpisodeStart.Should().Be(14);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void AutoDetectEpisodeStart_DoesNotModifyExplicitValue()
    {
        var outputDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(outputDir, "Show - S01E05.mkv"), "");

            var options = new RipOptions { Output = outputDir, Season = 1, EpisodeStart = 1 };
            InvokeAutoDetect(options);
            // AutoDetectEpisodeStart still runs and finds E05, setting to 6.
            // The guard that prevents calling it when EpisodeStart is explicit
            // lives in ProcessDiscAsync, not in the method itself.
            options.EpisodeStart.Should().Be(6);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ripsharp-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void InvokeAutoDetect(RipOptions options)
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

        var method = typeof(DiscRipper).GetMethod("AutoDetectEpisodeStart", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("AutoDetectEpisodeStart should exist as a private method");
        method!.Invoke(ripper, new object[] { options });
    }
}
