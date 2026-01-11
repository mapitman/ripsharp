using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AwesomeAssertions;

using NSubstitute;

using RipSharp;

using Xunit;

namespace RipSharp.Tests.Metadata;

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
        Environment.SetEnvironmentVariable("OMDB_API_KEY", null);
        Environment.SetEnvironmentVariable("TMDB_API_KEY", null);
        var notifier = Substitute.For<IConsoleWriter>();
        var providers = new List<IMetadataProvider>();
        var svc = new MetadataService(providers, notifier);

        var md = await svc.LookupAsync("SIMPSONS_WS", isTv: false, year: null);

        md.Should().NotBeNull();
        md!.Title.Should().Be("SIMPSONS_WS");
        md.Type.Should().Be("movie");
        notifier.Received(1).Warning(Arg.Any<string>());
    }

    [Fact]
    public async Task LookupAsync_ReturnsFromFirstProvider_WhenMatch()
    {
        var notifier = Substitute.For<IConsoleWriter>();
        var provider1 = Substitute.For<IMetadataProvider>();
        provider1.Name.Returns("Provider1");
        provider1.LookupAsync("test", false, null).Returns(new ContentMetadata { Title = "Test Movie", Year = 2020, Type = "movie" });

        var provider2 = Substitute.For<IMetadataProvider>();
        provider2.Name.Returns("Provider2");

        var providers = new List<IMetadataProvider> { provider1, provider2 };
        var svc = new MetadataService(providers, notifier);

        var result = await svc.LookupAsync("test", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Movie");
        await provider1.Received(1).LookupAsync("test", false, null);
        await provider2.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>());
        notifier.Received(1).Success(Arg.Is<string>(s => s.Contains("Provider1")));
    }

    [Fact]
    public async Task LookupAsync_TriesSecondProvider_WhenFirstReturnsNull()
    {
        var notifier = Substitute.For<IConsoleWriter>();
        var provider1 = Substitute.For<IMetadataProvider>();
        provider1.Name.Returns("Provider1");
        provider1.LookupAsync("test", false, null).Returns((ContentMetadata?)null);

        var provider2 = Substitute.For<IMetadataProvider>();
        provider2.Name.Returns("Provider2");
        provider2.LookupAsync("test", false, null).Returns(new ContentMetadata { Title = "Test Movie", Year = 2021, Type = "movie" });

        var providers = new List<IMetadataProvider> { provider1, provider2 };
        var svc = new MetadataService(providers, notifier);

        var result = await svc.LookupAsync("test", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Movie");
        await provider1.Received(1).LookupAsync("test", false, null);
        await provider2.Received(1).LookupAsync("test", false, null);
        notifier.Received(1).Success(Arg.Is<string>(s => s.Contains("Provider2")));
    }

    [Fact]
    public async Task LookupAsync_UsesTitleVariations_WhenOriginalFails()
    {
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = Substitute.For<IMetadataProvider>();
        provider.Name.Returns("TestProvider");
        provider.LookupAsync("MOVIE_TITLE_2023", Arg.Any<bool>(), Arg.Any<int?>()).Returns((ContentMetadata?)null);
        provider.LookupAsync("MOVIE_TITLE", Arg.Any<bool>(), Arg.Any<int?>()).Returns(new ContentMetadata { Title = "Movie Title", Year = 2023, Type = "movie" });

        var providers = new List<IMetadataProvider> { provider };
        var svc = new MetadataService(providers, notifier);

        var result = await svc.LookupAsync("MOVIE_TITLE_2023", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Movie Title");
        await provider.Received(1).LookupAsync("MOVIE_TITLE_2023", false, null);
        await provider.Received(1).LookupAsync("MOVIE_TITLE", false, null);
        notifier.Received(1).Success(Arg.Is<string>(s => s.Contains("simplified title") && s.Contains("MOVIE_TITLE")));
    }

    [Fact]
    public async Task LookupAsync_ShowsDifferentMessage_ForSimplifiedTitle()
    {
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = Substitute.For<IMetadataProvider>();
        provider.Name.Returns("TestProvider");
        provider.LookupAsync("SIMPSONS_WS", Arg.Any<bool>(), Arg.Any<int?>()).Returns((ContentMetadata?)null);
        provider.LookupAsync("SIMPSONS", Arg.Any<bool>(), Arg.Any<int?>()).Returns(new ContentMetadata { Title = "The Simpsons", Year = 1989, Type = "tv" });

        var providers = new List<IMetadataProvider> { provider };
        var svc = new MetadataService(providers, notifier);

        await svc.LookupAsync("SIMPSONS_WS", isTv: true, year: null);

        notifier.Received(1).Success(Arg.Is<string>(s =>
            s.Contains("simplified title 'SIMPSONS'") &&
            s.Contains("The Simpsons") &&
            s.Contains("(1989)")));
    }

    [Fact]
    public async Task LookupAsync_ShowsNormalMessage_ForOriginalTitle()
    {
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = Substitute.For<IMetadataProvider>();
        provider.Name.Returns("TestProvider");
        provider.LookupAsync("Test Movie", Arg.Any<bool>(), Arg.Any<int?>()).Returns(new ContentMetadata { Title = "Test Movie", Year = 2020, Type = "movie" });

        var providers = new List<IMetadataProvider> { provider };
        var svc = new MetadataService(providers, notifier);

        await svc.LookupAsync("Test Movie", isTv: false, year: null);

        notifier.Received(1).Success(Arg.Is<string>(s =>
            !s.Contains("simplified") &&
            s.Contains("Test Movie") &&
            s.Contains("(2020)")));
    }
}
