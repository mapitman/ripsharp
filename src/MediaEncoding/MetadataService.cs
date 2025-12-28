using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MediaEncoding;

public class MetadataService : IMetadataService
{
    private readonly HttpClient _http = new();

    public async Task<Metadata?> LookupAsync(string title, bool isTv, int? year)
    {
        // OMDB first, then TMDB
        var omdbKey = Environment.GetEnvironmentVariable("OMDB_API_KEY");
        var tmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");

        if (!string.IsNullOrWhiteSpace(omdbKey))
        {
            var url = $"https://www.omdbapi.com/?apikey={omdbKey}&type={(isTv ? "series" : "movie")}&t={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&y={year.Value}" : "");
            try
            {
                var json = await _http.GetStringAsync(url);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Response", out var resp) && resp.GetString() == "True")
                {
                    var result = new Metadata
                    {
                        Title = doc.RootElement.TryGetProperty("Title", out var t) ? t.GetString() ?? title : title,
                        Year = doc.RootElement.TryGetProperty("Year", out var yEl) && int.TryParse(yEl.GetString(), out var y) ? y : year,
                        Type = isTv ? "tv" : "movie",
                    };
                    Console.WriteLine($"✓ OMDB {(isTv ? "TV" : "movie")} lookup found: '{result.Title}'" + (result.Year.HasValue ? $" ({result.Year.Value})" : ""));
                    return result;
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(tmdbKey))
        {
            try
            {
                if (isTv)
                {
                    var url = $"https://api.themoviedb.org/3/search/tv?api_key={tmdbKey}&query={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&first_air_date_year={year.Value}" : "");
                    var json = await _http.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    var results = doc.RootElement.TryGetProperty("results", out var r) ? r : default;
                    if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        var name = first.TryGetProperty("name", out var nm) ? nm.GetString() : title;
                        var airYear = first.TryGetProperty("first_air_date", out var fad) && fad.GetString() is string fadStr && fadStr.Length >= 4 ? int.Parse(fadStr.Substring(0, 4)) : year;
                        var md = new Metadata { Title = name ?? title, Year = airYear, Type = "tv" };
                        Console.WriteLine($"✓ TMDB TV lookup found: '{md.Title}'" + (md.Year.HasValue ? $" ({md.Year.Value})" : ""));
                        return md;
                    }
                }
                else
                {
                    var url = $"https://api.themoviedb.org/3/search/movie?api_key={tmdbKey}&query={Uri.EscapeDataString(title)}" + (year.HasValue ? $"&year={year.Value}" : "");
                    var json = await _http.GetStringAsync(url);
                    var doc = JsonDocument.Parse(json);
                    var results = doc.RootElement.TryGetProperty("results", out var r) ? r : default;
                    if (results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                    {
                        var first = results[0];
                        var name = first.TryGetProperty("title", out var nm) ? nm.GetString() : title;
                        var relYear = first.TryGetProperty("release_date", out var rd) && rd.GetString() is string rdStr && rdStr.Length >= 4 ? int.Parse(rdStr.Substring(0, 4)) : year;
                        var md = new Metadata { Title = name ?? title, Year = relYear, Type = "movie" };
                        Console.WriteLine($"✓ TMDB movie lookup found: '{md.Title}'" + (md.Year.HasValue ? $" ({md.Year.Value})" : ""));
                        return md;
                    }
                }
            }
            catch { }
        }

        Console.WriteLine($"⚠️ No metadata found from OMDB or TMDB for '{title}'. Using disc title as fallback.");
        return new Metadata { Title = title, Year = year, Type = isTv ? "tv" : "movie" };
    }
}
