using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RipSharp.Metadata;

public class OmdbMetadataProvider : IMetadataProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IConsoleWriter _notifier;

    public string Name => "OMDB";

    public OmdbMetadataProvider(HttpClient http, string apiKey, IConsoleWriter notifier)
    {
        _http = http;
        _apiKey = apiKey;
        _notifier = notifier;
    }

    public async Task<ContentMetadata?> LookupAsync(string title, bool isTv, int? year)
    {
        try
        {
            var url = $"https://www.omdbapi.com/?apikey={_apiKey}&type={(isTv ? "series" : "movie")}&t={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&y={year.Value}" : "");
            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Response", out var resp) && resp.GetString() == "True")
            {
                var result = new ContentMetadata
                {
                    Title = doc.RootElement.TryGetProperty("Title", out var t) ? t.GetString() ?? title : title,
                    Year = doc.RootElement.TryGetProperty("Year", out var yEl) && int.TryParse(yEl.GetString(), out var y) ? y : year,
                    Type = isTv ? "tv" : "movie",
                };
                return result;
            }
        }
        catch { }
        return null;
    }
}
