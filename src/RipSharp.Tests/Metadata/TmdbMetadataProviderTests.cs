using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using NSubstitute;

using RipSharp;

using Xunit;

namespace RipSharp.Tests.Metadata;

public class TmdbMetadataProviderTests
{
    [Fact]
    public async Task LookupAsync_ReturnsMetadata_WhenMovieFound()
    {
        var json = @"{""results"":[{""title"":""The Matrix"",""release_date"":""1999-03-31""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("the matrix", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("The Matrix");
        result.Year.Should().Be(1999);
        result.Type.Should().Be("movie");
    }

    [Fact]
    public async Task LookupAsync_ReturnsMetadata_WhenTvSeriesFound()
    {
        var json = @"{""results"":[{""name"":""Game of Thrones"",""first_air_date"":""2011-04-17""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("game of thrones", isTv: true, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Game of Thrones");
        result.Year.Should().Be(2011);
        result.Type.Should().Be("tv");
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNoResultsFound()
    {
        var json = @"{""results"":[]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("nonexistent movie", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenJsonMalformed()
    {
        var json = @"{invalid json}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenHttpRequestFails()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public void Name_ReturnsTMDB()
    {
        var httpClient = new HttpClient();
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        // Act & Assert
        provider.Name.Should().Be("TMDB");
    }

    [Fact]
    public async Task LookupAsync_HandlesMovieWithoutReleaseDate()
    {
        var json = @"{""results"":[{""title"":""Upcoming Movie""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("upcoming", isTv: false, year: 2025);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Upcoming Movie");
        result.Year.Should().Be(2025); // Falls back to provided year
    }

    [Fact]
    public async Task LookupAsync_HandlesTvWithoutAirDate()
    {
        var json = @"{""results"":[{""name"":""New Series""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("new series", isTv: true, year: 2026);

        result.Should().NotBeNull();
        result!.Title.Should().Be("New Series");
        result.Year.Should().Be(2026); // Falls back to provided year
    }

    [Fact]
    public async Task LookupAsync_HandlesShortDateString()
    {
        var json = @"{""results"":[{""title"":""Test Movie"",""release_date"":""202""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: 2020);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2020); // Falls back when date string too short
    }

    [Fact]
    public async Task LookupAsync_UsesFirstResult_WhenMultipleResults()
    {
        var json = @"{""results"":[{""title"":""First Movie"",""release_date"":""2020-01-01""},{""title"":""Second Movie"",""release_date"":""2021-01-01""}]}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new TmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("movie", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("First Movie");
        result.Year.Should().Be(2020);
    }

    private static HttpClient CreateHttpClient(string responseJson)
    {
        var handler = new FakeHttpMessageHandler(responseJson);
        return new HttpClient(handler);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _responseJson;
        private readonly HttpStatusCode _statusCode;

        public FakeHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
            _statusCode = HttpStatusCode.OK;
        }

        public FakeHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_statusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson ?? "")
            };
            return Task.FromResult(response);
        }
    }
}
