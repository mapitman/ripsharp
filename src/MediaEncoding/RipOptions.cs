using System;
using System.Collections.Generic;

namespace MediaEncoding;

public class RipOptions
{
    public string Disc { get; set; } = "disc:0";
    public string Output { get; set; } = string.Empty;
    public string? Temp { get; set; }
    public bool Tv { get; set; }
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
                case "--tv": opts.Tv = true; break;
                case "--mode":
                    var mode = next()?.ToLowerInvariant();
                    if (mode == "tv" || mode == "series") opts.Tv = true;
                    else if (mode == "movie" || mode == "film") opts.Tv = false;
                    else throw new ArgumentException("--mode must be 'movie' or 'tv'");
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

    public static void DisplayHelp()
    {
        Console.WriteLine("media-encoding - DVD/Blu-Ray/UHD disc ripping tool");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    dotnet run --project src/MediaEncoding -- [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine("REQUIRED OPTIONS:");
        Console.WriteLine("    --output PATH           Output directory for ripped files");
        Console.WriteLine("    --mode movie|tv         Content type (movie for films, tv for series)");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --disc PATH             Optical drive path (default: disc:0)");
        Console.WriteLine("    --temp PATH             Temporary ripping directory (default: {output}/.makemkv)");
        Console.WriteLine("    --title TEXT            Custom title for file naming");
        Console.WriteLine("    --year YYYY             Release year (movies only)");
        Console.WriteLine("    --season N              Season number (TV only, default: 1)");
        Console.WriteLine("    --episode-start N       Starting episode number (TV only, default: 1)");
        Console.WriteLine("    --disc-type TYPE        Override disc type: dvd|bd|uhd (auto-detect by default)");
        Console.WriteLine("    --debug                 Enable debug logging");
        Console.WriteLine("    -h, --help              Show this help message");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("    # Rip a movie");
        Console.WriteLine("    dotnet run --project src/MediaEncoding -- --output ~/Movies --mode movie --title \"The Matrix\" --year 1999");
        Console.WriteLine();
        Console.WriteLine("    # Rip a TV season");
        Console.WriteLine("    dotnet run --project src/MediaEncoding -- --output ~/TV --mode tv --title \"Breaking Bad\" --season 1");
        Console.WriteLine();
        Console.WriteLine("    # Use second disc drive");
        Console.WriteLine("    dotnet run --project src/MediaEncoding -- --output ~/Movies --mode movie --disc disc:1");
        Console.WriteLine();
        Console.WriteLine("ENVIRONMENT VARIABLES:");
        Console.WriteLine("    TMDB_API_KEY            TMDB API key for metadata lookup (recommended)");
        Console.WriteLine("    OMDB_API_KEY            OMDB API key for metadata lookup (optional)");
        Console.WriteLine();
        Console.WriteLine("For more information, visit: https://github.com/mapitman/media-encoding");
    }
}
