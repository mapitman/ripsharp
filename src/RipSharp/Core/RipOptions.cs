using System;
using System.Collections.Generic;

namespace RipSharp.Core;

public class RipOptions
{
    public string Disc { get; set; } = "disc:0";
    public string Output { get; set; } = string.Empty;
    public string? Temp { get; set; }
    public bool Tv { get; set; }
    public bool AutoDetect { get; set; } = true; // Auto-detect content type by default
    public string? Title { get; set; }
    public int? Year { get; set; }
    public int Season { get; set; } = 1;
    public int EpisodeStart { get; set; } = 1;
    public bool Debug { get; set; }
    public string? DiscType { get; set; } // dvd|bd|uhd
    public bool ShowHelp { get; set; }

    public static RipOptions ParseArgs(string[] args)
    {
        var opts = new RipOptions();

        // Check for help first
        if (args.Length == 0 || args.Any(a => a == "-h" || a == "--help"))
        {
            opts.ShowHelp = true;
            return opts;
        }

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--disc": opts.Disc = next() ?? opts.Disc; break;
                case "--output": opts.Output = next() ?? opts.Output; break;
                case "--temp": opts.Temp = next(); break;
                case "--tv": opts.Tv = true; opts.AutoDetect = false; break;
                case "--mode":
                    var mode = next();
                    if (mode == null)
                        throw new ArgumentException("--mode requires a value");
                    var modeLower = mode.ToLowerInvariant();
                    if (modeLower == "tv" || modeLower == "series")
                    {
                        opts.Tv = true;
                        opts.AutoDetect = false;
                    }
                    else if (modeLower == "movie" || modeLower == "film")
                    {
                        opts.Tv = false;
                        opts.AutoDetect = false;
                    }
                    else if (modeLower == "auto" || modeLower == "detect")
                    {
                        opts.AutoDetect = true;
                    }
                    else throw new ArgumentException("--mode must be 'movie', 'tv', or 'auto'");
                    break;
                case "--title": opts.Title = next(); break;
                case "--year": if (int.TryParse(next(), out var y)) opts.Year = y; break;
                case "--season": if (int.TryParse(next(), out var s)) opts.Season = s; break;
                case "--episode-start": if (int.TryParse(next(), out var e)) opts.EpisodeStart = e; break;
                case "--debug": opts.Debug = true; break;
                case "--disc-type": opts.DiscType = next(); break;
            }
        }
        if (string.IsNullOrWhiteSpace(opts.Output))
        {
            throw new ArgumentException("--output is required");
        }
        if (string.IsNullOrEmpty(opts.Temp))
        {
            opts.Temp = Path.Combine(opts.Output, ".makemkv");
        }
        return opts;
    }

    public static void DisplayHelp(IConsoleWriter writer)
    {
        writer.Plain("ripsharp - DVD/Blu-Ray/UHD disc ripping tool");
        writer.Plain("");
        writer.Plain("USAGE:");
        writer.Plain("    dotnet run --project src/RipSharp -- [OPTIONS]");
        writer.Plain("");
        writer.Plain("REQUIRED OPTIONS:");
        writer.Plain("    --output PATH           Output directory for ripped files");
        writer.Plain("");
        writer.Plain("OPTIONS:");
        writer.Plain("    --mode auto|movie|tv    Content type detection (default: auto)");
        writer.Plain("                            - auto: Automatically detect movie vs TV series");
        writer.Plain("                            - movie: Treat as single movie");
        writer.Plain("                            - tv: Treat as TV series");
        writer.Plain("    --disc PATH             Optical drive path (default: disc:0)");
        writer.Plain("    --temp PATH             Temporary ripping directory (default: {output}/.makemkv)");
        writer.Plain("    --title TEXT            Custom title for file naming");
        writer.Plain("    --year YYYY             Release year (movies only)");
        writer.Plain("    --season N              Season number (TV only, default: 1)");
        writer.Plain("    --episode-start N       Starting episode number (TV only, default: 1)");
        writer.Plain("    --disc-type TYPE        Override disc type: dvd|bd|uhd (auto-detect by default)");
        writer.Plain("    --debug                 Enable debug logging");
        writer.Plain("    -h, --help              Show this help message");
        writer.Plain("");
        writer.Plain("EXAMPLES:");
        writer.Plain("    # Rip with auto-detection (recommended)");
        writer.Plain("    dotnet run --project src/RipSharp -- --output ~/Movies --title \"The Matrix\" --year 1999");
        writer.Plain("");
        writer.Plain("    # Rip a movie (explicit)");
        writer.Plain("    dotnet run --project src/RipSharp -- --output ~/Movies --mode movie --title \"The Matrix\" --year 1999");
        writer.Plain("");
        writer.Plain("    # Rip a TV season (explicit)");
        writer.Plain("    dotnet run --project src/RipSharp -- --output ~/TV --mode tv --title \"Breaking Bad\" --season 1");
        writer.Plain("");
        writer.Plain("    # Use second disc drive");
        writer.Plain("    dotnet run --project src/RipSharp -- --output ~/Movies --disc disc:1");
        writer.Plain("");
        writer.Plain("ENVIRONMENT VARIABLES:");
        writer.Plain("    TMDB_API_KEY            TMDB API key for metadata lookup (recommended)");
        writer.Plain("    OMDB_API_KEY            OMDB API key for metadata lookup (optional)");
        writer.Plain("");
        writer.Plain("For more information, visit: https://github.com/mapitman/ripsharp");
    }
}
