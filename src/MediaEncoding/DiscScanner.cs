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

        bool discDetectedPrinted = false;
        bool scanningStarted = false;
        int titleAddedCount = 0;
        var printedTitles = new HashSet<string>();
        var exit = await _runner.RunAsync("makemkvcon", $"-r --robot info {discPath}", onOutput: line =>
        {
            // Show relevant scanning progress and info
            if (line.StartsWith("MSG:") && line.Contains("insert disc"))
            {
                Spectre.Console.AnsiConsole.MarkupLine("[yellow]💿 Insert a disc into the drive...[/]");
            }
            else if (line.StartsWith("DRV:0,") && !line.Contains(",256,") && !discDetectedPrinted)
            {
                Spectre.Console.AnsiConsole.MarkupLine("[green]📀 Disc detected in drive...[/]");
                discDetectedPrinted = true;
            }
            else if (line.StartsWith("MSG:1005,"))
            {
                // MakeMKV started message
                var msg = ExtractQuoted(line);
                if (!string.IsNullOrWhiteSpace(msg))
                    Spectre.Console.AnsiConsole.MarkupLine($"[dim]▸ {Spectre.Console.Markup.Escape(msg)}[/]");
            }
            else if (line.StartsWith("MSG:1011,") || line.StartsWith("MSG:3007,"))
            {
                // LibreDrive mode or direct disc access
                var msg = ExtractQuoted(line);
                if (!string.IsNullOrWhiteSpace(msg))
                    Spectre.Console.AnsiConsole.MarkupLine($"[dim]▸ {Spectre.Console.Markup.Escape(msg)}[/]");
            }
            else if (line.StartsWith("MSG:5085,"))
            {
                // Content hash table loaded
                Spectre.Console.AnsiConsole.MarkupLine("[dim]▸ Loaded content hash table[/]");
            }
            else if (line.StartsWith("MSG:") && (line.Contains("Scanning") || line.Contains("scanning")) && !scanningStarted)
            {
                Spectre.Console.AnsiConsole.MarkupLine("[blue]🔍 Scanning disc structure...[/]");
                scanningStarted = true;
            }
            else if (line.StartsWith("MSG:3307,"))
            {
                // File was added as title - show which file
                titleAddedCount++;
                var fileMatch = System.Text.RegularExpressions.Regex.Match(line, @"""File ([^\s]+)");
                var titleMatch = System.Text.RegularExpressions.Regex.Match(line, @"title #(\d+)");
                if (fileMatch.Success && titleMatch.Success)
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[dim green]  ✓ Added title #{titleMatch.Groups[1].Value}: {fileMatch.Groups[1].Value}[/]");
                }
            }
            else if (line.StartsWith("MSG:3309,"))
            {
                // Duplicate title skipped - show at lower priority
                var match = System.Text.RegularExpressions.Regex.Match(line, @"""Title ([^\s]+) is equal");
                if (match.Success)
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[dim]  ~ Skipped duplicate: {match.Groups[1].Value}[/]");
                }
            }
            else if (line.StartsWith("MSG:3025,"))
            {
                // Short title skipped - extract file name and show occasionally
                var match = System.Text.RegularExpressions.Regex.Match(line, @"""Title #([^\s]+)");
                if (match.Success)
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[dim]  - Skipped short: {match.Groups[1].Value}[/]");
                }
            }
            else if (line.StartsWith("MSG:") && (line.Contains("error") || line.Contains("fail")))
            {
                Spectre.Console.AnsiConsole.MarkupLine($"[red]❌ {Spectre.Console.Markup.Escape(line)}[/]");
            }
            else if (line.StartsWith("CINFO:1,"))
            {
                var dtype = ExtractQuoted(line);
                if (!string.IsNullOrWhiteSpace(dtype))
                {
                    Spectre.Console.AnsiConsole.MarkupLine($"[cyan]💽 Disc type: [bold]{Spectre.Console.Markup.Escape(dtype)}[/][/]");
                }
            }
            else if (line.StartsWith("TINFO:"))
            {
                // TINFO format: TINFO:<titleId>,<fieldId>,<flags>,"<value>"
                // Field 2 is the title name
                var parts = line.Split(new[] { ',' }, 4);
                if (parts.Length >= 4 && parts[1] == "2")
                {
                    var tname = ExtractQuoted(line);
                    if (!string.IsNullOrWhiteSpace(tname) && printedTitles.Add(tname!))
                    {
                        Spectre.Console.AnsiConsole.MarkupLine($"[magenta]🎞️ Title found: [bold]{Spectre.Console.Markup.Escape(tname)}[/][/]");
                    }
                }
            }
            
            // Parse protocol lines for disc info (non-display logic)
            if (line.StartsWith("CINFO:1,"))
            {
                // CINFO:1 contains disc type (DVD, Blu-ray disc, etc.)
                discType = ExtractQuoted(line) ?? discType;
            }
            else if (line.StartsWith("CINFO:2,") && string.IsNullOrEmpty(discName))
            {
                // CINFO:2 contains disc name/title
                discName = ExtractQuoted(line);
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
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                title.Name = name;
                                if (discName == null)
                                    discName = name;
                            }
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

        if (titleAddedCount > 0)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[green]✓ Scan complete - found {titleAddedCount} title(s)[/]");
        }

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
