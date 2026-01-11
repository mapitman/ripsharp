using AwesomeAssertions;
using MediaEncoding;
using Xunit;

namespace MediaEncoding.Tests;

public class DurationFormatterTests
{
    [Fact]
    public void Format_ReturnsUnknownDuration_WhenSecondsIsZero()
    {
        var result = DurationFormatter.Format(0);
        result.Should().Be("Unknown duration");
    }

    [Fact]
    public void Format_ReturnsUnknownDuration_WhenSecondsIsNegative()
    {
        var result = DurationFormatter.Format(-100);
        result.Should().Be("Unknown duration");
    }

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m 0s")]
    [InlineData(90, "1m 30s")]
    [InlineData(3599, "59m 59s")]
    [InlineData(3600, "1h 0m 0s")]
    [InlineData(3661, "1h 1m 1s")]
    [InlineData(7322, "2h 2m 2s")]
    public void Format_ReturnsCorrectlyFormattedDuration(int seconds, string expected)
    {
        var result = DurationFormatter.Format(seconds);
        result.Should().Be(expected);
    }
}
