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

public class OmdbMetadataProviderTests
{
    [Fact]
    public async Task LookupAsync_ReturnsMetadata_WhenMovieFound()
    {
        var json = @"{""Response"":""True"",""Title"":""Inception"",""Year"":""2010"",""Type"":""movie""}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("inception", isTv: false, year: null);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.Type.Should().Be("movie");
    }

    [Fact]
    public async Task LookupAsync_ReturnsMetadata_WhenTvSeriesFound()
    {
        var json = @"{""Response"":""True"",""Title"":""Breaking Bad"",""Year"":""2008-2013"",""Type"":""series""}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("breaking bad", isTv: true, year: 2008);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Breaking Bad");
        result.Year.Should().Be(2008);
        result.Type.Should().Be("tv");
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenResponseIsFalse()
    {
        var json = @"{""Response"":""False"",""Error"":""Movie not found!""}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("nonexistent movie", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenJsonMalformed()
    {
        var json = @"{invalid json}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenHttpRequestFails()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: null);

        result.Should().BeNull();
    }

    [Fact]
    public void Name_ReturnsOMDB()
    {
        var httpClient = new HttpClient();
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        // Act & Assert
        provider.Name.Should().Be("OMDB");
    }

    [Fact]
    public async Task LookupAsync_IncludesYear_WhenProvided()
    {
        var json = @"{""Response"":""True"",""Title"":""Dune"",""Year"":""2021"",""Type"":""movie""}";
        var handler = new FakeHttpMessageHandler(json);
        var httpClient = new HttpClient(handler);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        await provider.LookupAsync("dune", isTv: false, year: 2021);

        handler.RequestUri.Should().NotBeNull();
        var url = handler.RequestUri!.ToString();
        url.Should().Contain("&y=2021");
    }

    [Fact]
    public async Task LookupAsync_HandlesYearParsingFailure()
    {
        var json = @"{""Response"":""True"",""Title"":""Test"",""Year"":""N/A"",""Type"":""movie""}";
        var httpClient = CreateHttpClient(json);
        var notifier = Substitute.For<IConsoleWriter>();
        var provider = new OmdbMetadataProvider(httpClient, "test-key", notifier);

        var result = await provider.LookupAsync("test", isTv: false, year: 2020);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2020); // Falls back to provided year
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
        public Uri? RequestUri { get; private set; }

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
            RequestUri = request.RequestUri;

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
