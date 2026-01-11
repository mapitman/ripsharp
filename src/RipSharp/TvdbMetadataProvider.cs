using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RipSharp;

/// <summary>
/// TVDB metadata provider using raw REST calls. Currently scaffolding the flow; real
/// episode/series title lookups will be added in follow-up work for issue #37.
/// </summary>
public class TvdbMetadataProvider : IMetadataProvider, ITvEpisodeTitleProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IProgressNotifier _notifier;

    private string? _token;
    private DateTime _tokenExpiryUtc = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, (int? seriesId, string? seriesName, int? year)> _seriesCache = new();

    public string Name => "TVDB";

    public TvdbMetadataProvider(HttpClient http, string apiKey, IProgressNotifier notifier)
    {
        _http = http;
        _apiKey = apiKey;
        _notifier = notifier;
    }

    public async Task<Metadata?> LookupAsync(string title, bool isTv, int? year)
    {
        // TVDB focus is TV content; for movies keep existing providers as primary.
        if (!isTv)
            return null;

        var series = await SearchSeriesAsync(title, year);
        if (series.seriesId == null)
            return null;

        return new Metadata
        {
            Title = series.seriesName ?? title,
            Year = series.year ?? year,
            Type = "tv"
        };
    }

    public async Task<string?> GetEpisodeTitleAsync(string seriesTitle, int season, int episode, int? year)
    {
        var cacheKey = BuildCacheKey(seriesTitle, year);
        var series = _seriesCache.ContainsKey(cacheKey)
            ? _seriesCache[cacheKey]
            : await SearchSeriesAsync(seriesTitle, year);

        if (series.seriesId == null)
            return null;

        return await FetchEpisodeTitleAsync(series.seriesId.Value, season, episode);
    }

    private string BuildCacheKey(string title, int? year) => $"{title.Trim().ToLowerInvariant()}|{year?.ToString() ?? ""}";

    private async Task<(int? seriesId, string? seriesName, int? year)> SearchSeriesAsync(string title, int? year)
    {
        var cacheKey = BuildCacheKey(title, year);
        if (_seriesCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var token = await GetTokenAsync();
        if (token == null)
            return (null, null, null);

        var url = $"https://api4.thetvdb.com/v4/search?query={Uri.EscapeDataString(title)}&type=series";
        if (year.HasValue) url += $"&year={year.Value}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return (null, null, null);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return (null, null, null);

        var first = data[0];
        var id = first.TryGetProperty("tvdb_id", out var idEl) && TryGetInt32Flexible(idEl, out var sid) ? sid :
             first.TryGetProperty("id", out var idAlt) && TryGetInt32Flexible(idAlt, out var sidAlt) ? sidAlt : (int?)null;
        var name = first.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : title;
        int? foundYear = null;
        if (first.TryGetProperty("first_air_time", out var fa) && fa.GetString() is string faStr && faStr.Length >= 4 && int.TryParse(faStr.Substring(0, 4), out var fy))
            foundYear = fy;

        var result = (id, name, foundYear);
        if (id != null)
            _seriesCache[cacheKey] = result;
        return result;
    }

    private static bool TryGetInt32Flexible(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
            return true;

        value = default;
        return false;
    }

    private async Task<string?> FetchEpisodeTitleAsync(int seriesId, int season, int episode)
    {
        var token = await GetTokenAsync();
        if (token == null)
            return null;

        var url = $"https://api4.thetvdb.com/v4/series/{seriesId}/episodes/default?page=0&season={season}&episodeNumber={episode}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;

        // data may be an object with "episodes" array or array directly; handle both
        JsonElement episodesElement = data;
        if (data.TryGetProperty("episodes", out var epsProp))
            episodesElement = epsProp;

        if (episodesElement.ValueKind == JsonValueKind.Array && episodesElement.GetArrayLength() > 0)
        {
            var first = episodesElement[0];
            if (first.TryGetProperty("name", out var nameEl) && nameEl.GetString() is string epName && !string.IsNullOrWhiteSpace(epName))
                return epName;
            if (first.TryGetProperty("episodeName", out var epAlt) && epAlt.GetString() is string epName2 && !string.IsNullOrWhiteSpace(epName2))
                return epName2;
        }

        return null;
    }

    private async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_token) && DateTime.UtcNow < _tokenExpiryUtc)
            return _token;

        var payload = JsonSerializer.Serialize(new { apikey = _apiKey });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("https://api4.thetvdb.com/v4/login", content);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;
        if (data.TryGetProperty("token", out var tokenEl) && tokenEl.GetString() is string token)
        {
            _token = token;
            _tokenExpiryUtc = DateTime.UtcNow.AddMinutes(55); // TVDB tokens typically last 1 hour
            return _token;
        }

        return null;
    }
}
