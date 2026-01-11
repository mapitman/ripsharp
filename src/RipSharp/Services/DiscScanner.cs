using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RipSharp.Services;

public class DiscScanner : IDiscScanner
{
    private readonly IProcessRunner _runner;
    private readonly IConsoleWriter _notifier;
    private readonly IDiscTypeDetector _typeDetector;

    public DiscScanner(IProcessRunner runner, IConsoleWriter notifier, IDiscTypeDetector typeDetector)
    {
        _runner = runner;
        _notifier = notifier;
        _typeDetector = typeDetector;
    }

    public async Task<DiscInfo?> ScanDiscAsync(string discPath)
    {
        var titles = new List<TitleInfo>();
        var handler = new ScanOutputHandler(_notifier, titles);

        var exit = await _runner.RunAsync("makemkvcon", $"-r --robot info {discPath}",
            onOutput: handler.HandleLine);

        if (exit != 0) return null;

        if (handler.TitleAddedCount > 0)
        {
            _notifier.Success($"✓ Scan complete - found {handler.TitleAddedCount} title(s)");
        }

        return BuildDiscInfo(handler.DiscName, handler.DiscType, titles);
    }

    private DiscInfo BuildDiscInfo(string? discName, string? discType, List<TitleInfo> titles)
    {
        var (detectedContentType, confidence) = _typeDetector.DetectContentType(new DiscInfo { Titles = titles });

        return new DiscInfo
        {
            DiscName = discName ?? string.Empty,
            DiscType = NormalizeDiscType(discType),
            Titles = titles,
            DetectedContentType = detectedContentType,
            DetectionConfidence = confidence
        };
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
            // Include only tracks longer than 30 minutes (theatrical, extended, director's cut, etc.)
            // This captures main versions while excluding short extras
            foreach (var t in titles)
            {
                var minutes = t.DurationSeconds / 60;
                if (minutes >= 30)
                    ids.Add(t.Id);
            }
            ids.Sort();
            return ids;
        }
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
