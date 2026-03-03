using System.Collections;
using System.Reflection;

namespace RipSharp.Tests.Services;

public class DiscRipperTitleSuffixTests
{
    [Fact]
    public async Task BuildTitlePlansAsync_SingleMovieTitle_DoesNotAppendSuffix()
    {
        var encoder = Substitute.For<IEncoderService>();
        var ripper = CreateRipper(encoder);
        var discInfo = new DiscInfo
        {
            Titles = new List<TitleInfo>
            {
                new() { Id = 0, Name = "Control" }
            }
        };
        var titleIds = new List<int> { 0 };
        var metadata = new ContentMetadata { Title = "Control", Year = 2007, Type = "movie" };
        var options = new RipOptions { Output = "/tmp", Season = 1, EpisodeStart = 1 };

        var plans = await InvokeBuildTitlePlansAsync(ripper, discInfo, titleIds, metadata, options);
        plans.Should().HaveCount(1);

        var plan = plans.Single();
        var finalFileName = GetStringProperty(plan, "FinalFileName");
        var versionSuffix = GetStringProperty(plan, "VersionSuffix");

        finalFileName.Should().Be("Control (2007).mkv");
        versionSuffix.Should().BeNull();
    }

    [Fact]
    public async Task EncodeAndRenameAsync_SingleMovieTitle_DoesNotAppendSuffix()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"ripsharp-tests-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(outputRoot);
        var sourceFile = Path.Combine(outputRoot, "source.mkv");
        File.WriteAllText(sourceFile, "data");

        try
        {
            var encoder = Substitute.For<IEncoderService>();
            encoder.EncodeAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<IProgressTask?>(),
                    Arg.Any<string?>())
                .Returns(callInfo =>
                {
                    var outputPath = callInfo.ArgAt<string>(1);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    File.WriteAllText(outputPath, "data");
                    return Task.FromResult(new ProcessResult(true, 0, Array.Empty<string>()));
                });

            var ripper = CreateRipper(encoder);
            var rippedFilesMap = new Dictionary<int, string> { { 0, sourceFile } };
            var metadata = new ContentMetadata { Title = "Control", Year = 2007, Type = "movie" };
            var options = new RipOptions { Output = outputRoot, Season = 1, EpisodeStart = 1 };
            var titlePlans = new List<TitlePlan>
            {
                new(TitleId: 0, Index: 0, EpisodeNum: null, EpisodeTitle: null,
                    TempOutputPath: Path.Combine(outputRoot, "Control.mkv"),
                    FinalFileName: "Control (2007).mkv", VersionSuffix: null, DisplayName: "Control")
            };

            var (successes, failures) = await InvokeEncodeAndRenameAsync(ripper, rippedFilesMap, titlePlans, metadata, options);

            failures.Should().BeEmpty();
            successes.Should().HaveCount(1);
            var finalPath = successes[0].FinalPath;
            finalPath.Should().Be(Path.Combine(outputRoot, "Control (2007).mkv"));
            finalPath.Should().NotContain("title01");
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static DiscRipper CreateRipper(IEncoderService encoder)
    {
        var scanner = Substitute.For<IDiscScanner>();
        var metadataService = Substitute.For<IMetadataService>();
        var makeMkv = Substitute.For<IMakeMkvService>();
        var notifier = Substitute.For<IConsoleWriter>();
        var userPrompt = Substitute.For<IUserPrompt>();
        var episodeTitles = Substitute.For<ITvEpisodeTitleProvider>();
        var progressDisplay = Substitute.For<IProgressDisplay>();
        var theme = ThemeProvider.CreateDefault();

        return new DiscRipper(scanner, encoder, metadataService, makeMkv, notifier, userPrompt, episodeTitles, progressDisplay, theme);
    }

    private static async Task<List<object>> InvokeBuildTitlePlansAsync(
        DiscRipper ripper,
        DiscInfo discInfo,
        List<int> titleIds,
        ContentMetadata metadata,
        RipOptions options)
    {
        var method = typeof(DiscRipper).GetMethod("BuildTitlePlansAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(ripper, new object[] { discInfo, titleIds, metadata, options })!;
        await task.ConfigureAwait(false);

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return ((IEnumerable)result).Cast<object>().ToList();
    }

    private static async Task<(List<TitleOutcome> Successes, List<TitleOutcome> Failures)> InvokeEncodeAndRenameAsync(
        DiscRipper ripper,
        Dictionary<int, string> rippedFilesMap,
        IReadOnlyList<TitlePlan> titlePlans,
        ContentMetadata metadata,
        RipOptions options)
    {
        var method = typeof(DiscRipper).GetMethod("EncodeAndRenameAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(ripper, new object[] { rippedFilesMap, titlePlans, metadata, options })!;
        await task.ConfigureAwait(false);

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var successes = (List<TitleOutcome>)result.GetType().GetField("Item1")!.GetValue(result)!;
        var failures = (List<TitleOutcome>)result.GetType().GetField("Item2")!.GetValue(result)!;
        return (successes, failures);
    }

    private static string? GetStringProperty(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as string;
    }
}
