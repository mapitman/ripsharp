namespace RipSharp.Tests.Models;

public class TitlePlanTests
{
    [Fact]
    public void TitlePlan_DurationSeconds_DefaultsToZero()
    {
        var plan = new TitlePlan(
            TitleId: 1, Index: 0, EpisodeNum: null, EpisodeTitle: null,
            TempOutputPath: "/tmp/out.mkv", FinalFileName: "Movie.mkv",
            VersionSuffix: null, DisplayName: "Movie");

        plan.DurationSeconds.Should().Be(0);
    }

    [Fact]
    public void TitlePlan_WithDurationSeconds_StoresValue()
    {
        var plan = new TitlePlan(
            TitleId: 1, Index: 0, EpisodeNum: 3, EpisodeTitle: "Pilot",
            TempOutputPath: "/tmp/out.mkv", FinalFileName: "Show - S01E03.mkv",
            VersionSuffix: null, DisplayName: "Show S01E03", DurationSeconds: 2700);

        plan.DurationSeconds.Should().Be(2700);
        plan.EpisodeNum.Should().Be(3);
        plan.EpisodeTitle.Should().Be("Pilot");
    }
}
