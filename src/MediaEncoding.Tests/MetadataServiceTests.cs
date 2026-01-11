using System.Threading.Tasks;
using AwesomeAssertions;
using MediaEncoding;
using NSubstitute;
using Xunit;

namespace MediaEncoding.Tests;

public class MetadataServiceTests : IDisposable
{
    private readonly string? _originalOmdbKey;
    private readonly string? _originalTmdbKey;

    public MetadataServiceTests()
    {
        _originalOmdbKey = Environment.GetEnvironmentVariable("OMDB_API_KEY");
        _originalTmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("OMDB_API_KEY", _originalOmdbKey);
        Environment.SetEnvironmentVariable("TMDB_API_KEY", _originalTmdbKey);
    }

    [Fact]
    public async Task LookupAsync_Fallbacks_WhenNoApiKeys()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OMDB_API_KEY", null);
        Environment.SetEnvironmentVariable("TMDB_API_KEY", null);
        var notifier = Substitute.For<IProgressNotifier>();
        var svc = new MetadataService(notifier);

        // Act
        var md = await svc.LookupAsync("SIMPSONS_WS", isTv: false, year: null);

        // Assert
        md.Should().NotBeNull();
        md!.Title.Should().Be("SIMPSONS_WS");
        md.Type.Should().Be("movie");
        notifier.Received(1).Warning(Arg.Any<string>());
    }
}
