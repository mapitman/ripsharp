namespace RipSharp.Tests.Models;

public class ProcessResultTests
{
    [Fact]
    public void ErrorSummary_WithErrorLines_IncludesExitCodeAndLastTenLines()
    {
        var lines = Enumerable.Range(1, 12).Select(i => $"Error {i}").ToList();
        var result = new ProcessResult(false, 2, lines);

        result.ErrorSummary.Should().Contain("exited with code 2");
        result.ErrorSummary.Should().Contain("Error 3");
        result.ErrorSummary.Should().Contain("Error 12");
        result.ErrorSummary.Should().NotContain("Error 2" + Environment.NewLine);
    }

    [Fact]
    public void ErrorSummary_WithoutErrorLines_UsesDefaultProcessName()
    {
        var result = new ProcessResult(false, 42, Array.Empty<string>());

        result.ErrorSummary.Should().Contain("Process exited with code 42");
        result.ErrorSummary.Should().Contain("No error details captured");
    }

    [Fact]
    public void ErrorSummary_WithoutErrorLines_UsesFirstWordFromCommand()
    {
        var result = new ProcessResult(false, 5, Array.Empty<string>(), "ffmpeg -i input.mkv output.mp4");

        result.ErrorSummary.Should().Contain("ffmpeg exited with code 5");
    }

    [Fact]
    public void ErrorSummary_WithoutErrorLines_UsesQuotedFirstWordFromCommand()
    {
        var result = new ProcessResult(false, 7, Array.Empty<string>(), "\"/usr/local/bin/ffmpeg tool\" -i input.mkv output.mp4");

        result.ErrorSummary.Should().Contain("/usr/local/bin/ffmpeg tool exited with code 7");
    }

    [Fact]
    public void ErrorSummary_WithoutErrorLines_IgnoresLeadingWhitespace()
    {
        var result = new ProcessResult(false, 9, Array.Empty<string>(), "   ffprobe -v error file.mkv");

        result.ErrorSummary.Should().Contain("ffprobe exited with code 9");
    }

    [Fact]
    public void ErrorSummary_IncludesLogPath_WhenPresent()
    {
        var result = new ProcessResult(false, 1, Array.Empty<string>(), "ffmpeg", "/tmp/ffmpeg.log");

        result.ErrorSummary.Should().Contain("log: /tmp/ffmpeg.log");
    }
}
