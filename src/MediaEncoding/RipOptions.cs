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

    public static RipOptions ParseArgs(string[] args)
    {
        var opts = new RipOptions();
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
}
