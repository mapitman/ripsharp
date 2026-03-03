namespace RipSharp.Tests.MakeMkv;

public class MakeMkvServiceTests
{
    [Fact]
    public async Task RipTitleAsync_FiltersProgressLines_FromCallbackAndErrorSummary()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var onError = callInfo.ArgAt<Action<string>?>(3);
                onError?.Invoke("PRGV:100,200,300");
                onError?.Invoke("PRGC:1,2,3");
                onError?.Invoke("real error line");
                return Task.FromResult(2);
            });

        var service = new MakeMkvService(runner);
        var callbackLines = new List<string>();

        var result = await service.RipTitleAsync("disc:0", 7, "/tmp/rips", onError: callbackLines.Add);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(2);
        result.ErrorLines.Should().BeEquivalentTo(["real error line"]);
        callbackLines.Should().BeEquivalentTo(["real error line"]);
    }

    [Fact]
    public async Task RipTitleAsync_PassesExpectedCommandAndArguments_ToRunner()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var service = new MakeMkvService(runner);

        var result = await service.RipTitleAsync("disc:1", 3, "/tmp/output");

        await runner.Received(1).RunAsync(
            "makemkvcon",
            "-r --robot mkv disc:1 3 \"/tmp/output\"",
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<CancellationToken>());

        result.Success.Should().BeTrue();
        result.Command.Should().Be("makemkvcon -r --robot mkv disc:1 3 \"/tmp/output\"");
    }
}
