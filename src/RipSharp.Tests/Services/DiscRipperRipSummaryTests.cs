using System;
using System.Reflection;

using AwesomeAssertions;

using RipSharp.Services;

using Xunit;

namespace RipSharp.Tests.Services;

public class DiscRipperRipSummaryTests
{
    [Fact]
    public void BuildRipCompletionMessage_IncludesCountsAndDuration()
    {
        var result = InvokeBuildRipCompletionMessage(3, 5, TimeSpan.FromSeconds(65));

        result.Should().Be("Ripping complete: 3/5 tracks in 1m 5s.");
    }

    private static string InvokeBuildRipCompletionMessage(int rippedCount, int totalTitles, TimeSpan elapsed)
    {
        var method = typeof(DiscRipper)
            .GetMethod("BuildRipCompletionMessage", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        return (string)method!.Invoke(null, new object[] { rippedCount, totalTitles, elapsed })!;
    }
}
