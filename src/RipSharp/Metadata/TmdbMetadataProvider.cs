using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RipSharp.Metadata;

public class TmdbMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IConsoleWriter _notifier;

    public string Name => "TMDB";

    public TmdbMetadataProvider(HttpClient http, string apiKey, IConsoleWriter notifier)
    {
        _http = http;
        _apiKey = apiKey;
        _notifier = notifier;
    }

    public async Task<ContentMetadata?> LookupAsync(string title, bool isTv, int? year)
    {
        try
        {
            return await (isTv ? LookupTvAsync(title, year) : LookupMovieAsync(title, year));
        }
        catch { }
        return null;
    }

    private async Task<ContentMetadata?> LookupTvAsync(string title, int? year)
    {
        var url = $"https://api.themoviedb.org/3/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&first_air_date_year={year.Value}" : "");
        var json = await _http.GetStringAsync(url);
        var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.TryGetProperty("results", out var r) ? r : default;
        if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            var first = results[0];
            var name = first.TryGetProperty("name", out var nm) ? nm.GetString() : title;
            var airYear = first.TryGetProperty("first_air_date", out var fad) && fad.GetString() is string fadStr && fadStr.Length >= 4 ? int.Parse(fadStr.Substring(0, 4)) : year;
            var md = new ContentMetadata { Title = name ?? title, Year = airYear, Type = "tv" };
            return md;
        }
        return null;
    }

    private async Task<ContentMetadata?> LookupMovieAsync(string title, int? year)
    {
        var url = $"https://api.themoviedb.org/3/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&year={year.Value}" : "");
        var json = await _http.GetStringAsync(url);
        var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.TryGetProperty("results", out var r) ? r : default;
        if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            var first = results[0];
            var name = first.TryGetProperty("title", out var nm) ? nm.GetString() : title;
            var relYear = first.TryGetProperty("release_date", out var rd) && rd.GetString() is string rdStr && rdStr.Length >= 4 ? int.Parse(rdStr.Substring(0, 4)) : year;
            var md = new ContentMetadata { Title = name ?? title, Year = relYear, Type = "movie" };
            return md;
        }
        return null;
    }
}
