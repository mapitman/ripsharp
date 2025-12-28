using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MediaEncoding;

public class DiscScanner : IDiscScanner
{
    private readonly IProcessRunner _runner;
    public DiscScanner(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<DiscInfo?> ScanDiscAsync(string discPath)
    {
        var info = new DiscInfo();
        var titles = new List<TitleInfo>();
        string? discName = null;
        string? discType = null;

        var exit = await _runner.RunAsync("makemkvcon", $"-r --robot info {discPath}", onOutput: line =>
        {
            // Example robot lines: TINFO:0,...,"Some Title"
            if (line.StartsWith("CINFO:"))
            {
                // Extract disc type raw from CINFO:0 lines if present
                if (line.Contains("type"))
                {
                    discType = ExtractQuoted(line) ?? discType;
                }
            }
            else if (line.StartsWith("DRV:"))
            {
                // ignore
            }
            else if (line.StartsWith("TINFO:"))
            {
                // Parse TINFO lines: TINFO:<titleId>,<attributeId>,<flags>,"<value>"
                // Field 2 = title name, Field 9 = duration, Field 10 = size string, Field 11 = size bytes
                var match = Regex.Match(line, @"TINFO:(\d+),(\d+),");
                if (match.Success)
                {
                    var id = int.Parse(match.Groups[1].Value);
                    var fieldId = int.Parse(match.Groups[2].Value);
                    
                    if (!titles.Exists(t => t.Id == id)) titles.Add(new TitleInfo { Id = id });
                    var title = titles.Find(x => x.Id == id)!;

                    switch (fieldId)
                    {
                        case 2: // Title name
                            var name = ExtractQuoted(line);
                            if (!string.IsNullOrWhiteSpace(name) && discName == null)
                                discName = name;
                            break;
                        case 9: // Duration (HH:MM:SS format)
                            var durStr = ExtractQuoted(line);
                            title.DurationSeconds = ParseDurationToSeconds(durStr);
                            break;
                        case 11: // Size in bytes
                            var sizeStr = ExtractQuoted(line);
                            if (long.TryParse(sizeStr, out var bytes))
                                title.ReportedSizeBytes = bytes;
                            break;
                    }
                }
            }
        });

        if (exit != 0) return null;

        info.DiscName = discName ?? string.Empty;
        info.DiscType = NormalizeDiscType(discType);
        info.Titles = titles;
        return info;
    }

    public List<int> IdentifyMainContent(DiscInfo info, bool isTv)
    {
        var ids = new List<int>();
        var titles = info.Titles;
        if (titles.Count == 0) return ids;
        if (isTv)
        {
            foreach (var t in titles)
            {
                if (t.DurationSeconds >= 20 * 60 && t.DurationSeconds <= 60 * 60)
                    ids.Add(t.Id);
            }
            ids.Sort();
            return ids;
        }
        else
        {
            // All movie-length titles >= 45 min (captures theatrical, extended, director's cut, etc.)
            foreach (var t in titles)
            {
                if (t.DurationSeconds >= 45 * 60)
                    ids.Add(t.Id);
            }
            ids.Sort();
            return ids;
        }
    }

    private static string? ExtractQuoted(string line)
    {
        var m = Regex.Match(line, "\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int ParseDurationToSeconds(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var sec))
            return h * 3600 + m * 60 + sec;
        return 0;
    }

    private static bool TryParseBytes(string? s, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrEmpty(s)) return false;
        // Very rough parser, expects number followed by unit
        var m = Regex.Match(s, @"([0-9]+) (MB|GB)");
        if (!m.Success) return false;
        var val = long.Parse(m.Groups[1].Value);
        var unit = m.Groups[2].Value;
        bytes = unit == "GB" ? val * 1024L * 1024L * 1024L : val * 1024L * 1024L;
        return true;
    }

    private static string NormalizeDiscType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "dvd" => "dvd",
        "bd" or "blu-ray" => "bd",
        "uhd" or "4k" => "uhd",
        _ => string.Empty
    };
}
