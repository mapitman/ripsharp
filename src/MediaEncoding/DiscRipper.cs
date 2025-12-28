using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaEncoding;

public class DiscRipper : IDiscRipper
{
    private readonly IDiscScanner _scanner;
    private readonly IEncoderService _encoder;
    private readonly IMetadataService _metadata;
    private readonly IProcessRunner _runner;

    public DiscRipper(IDiscScanner scanner, IEncoderService encoder, IMetadataService metadata, IProcessRunner runner)
    {
        _scanner = scanner;
        _encoder = encoder;
        _metadata = metadata;
        _runner = runner;
    }

    public async Task<List<string>> ProcessDiscAsync(RipOptions options)
    {
        Directory.CreateDirectory(options.Output);
        Directory.CreateDirectory(options.Temp!);

        var discInfo = await _scanner.ScanDiscAsync(options.Disc);
        if (discInfo == null)
        {
            Console.Error.WriteLine("Failed to scan disc");
            return new List<string>();
        }

        var titleForMeta = options.Title ?? discInfo.DiscName;
        // If no title provided and disc name is empty, use a fallback
        if (string.IsNullOrWhiteSpace(titleForMeta))
        {
            titleForMeta = options.Tv ? "TV Episode" : "Movie";
            Console.WriteLine($"⚠️ No title specified and disc name unavailable. Using fallback: '{titleForMeta}'");
        }
        var metadata = await _metadata.LookupAsync(titleForMeta, options.Tv, options.Year);
        var titleIds = _scanner.IdentifyMainContent(discInfo, options.Tv);
        if (titleIds.Count == 0)
        {
            Console.Error.WriteLine("No suitable titles found on disc");
            return new List<string>();
        }
        Console.WriteLine($"📋 Found {titleIds.Count} title(s) to rip: [{string.Join(", ", titleIds)}]");

        var rippedFiles = new List<string>();
        var totalTitles = titleIds.Count;

        for (int idx = 0; idx < titleIds.Count; idx++)
        {
            var titleId = titleIds[idx];
            Console.WriteLine($"🎬 Ripping title {idx + 1} of {totalTitles} (Title ID: {titleId})");
            var outFile = Path.Combine(options.Temp!, $"title_t{titleId:D2}.mkv");
            if (File.Exists(outFile)) File.Delete(outFile);

            // makemkvcon mkv disc:0 {titleId} tempDir --progress
            var args = $"-r --robot mkv {options.Disc} {titleId} \"{options.Temp}\"";
            var exit = await _runner.RunAsync("makemkvcon", args, onOutput: line =>
            {
                if (line.StartsWith("PRGV:"))
                {
                    var pctStr = line.Substring("PRGV:".Length);
                    if (int.TryParse(pctStr, out var pct))
                    {
                        Console.WriteLine($"Ripping {idx + 1}/{totalTitles} (ID {titleId}): {pct}%");
                    }
                }
                else if (line.StartsWith("PRGC:"))
                {
                    var caption = ExtractQuoted(line);
                    if (!string.IsNullOrEmpty(caption))
                    {
                        Console.WriteLine($"{caption} ({idx + 1}/{totalTitles})");
                    }
                }
            }, onError: e => { /* suppress verbose chatter */ });

            if (exit != 0)
            {
                Console.Error.WriteLine($"Failed to rip title {titleId}");
                continue;
            }

            // Determine ripped file path (MakeMKV names vary; fallback to temp listing)
            var ripped = Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderByDescending(File.GetCreationTime).FirstOrDefault();
            if (ripped != null)
            {
                rippedFiles.Add(ripped);
            }
        }

        // Encode and rename
        var finalFiles = new List<string>();
        for (int i = 0; i < rippedFiles.Count; i++)
        {
            var src = rippedFiles[i];
            string outputName;
            string? versionSuffix = null;
            
            if (options.Tv)
            {
                var episodeNum = (options.EpisodeStart - 1) + i + 1;
                outputName = Path.Combine(options.Output, $"temp_s{options.Season:00}e{episodeNum:00}.mkv");
            }
            else
            {
                // For movies with multiple versions, add a distinguishing suffix
                if (rippedFiles.Count > 1)
                {
                    var titleId = titleIds[i];
                    var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
                    var durationMin = titleInfo?.DurationSeconds / 60 ?? 0;
                    versionSuffix = $" [{durationMin}min]";
                }
                outputName = Path.Combine(options.Output, $"temp_movie_{i}.mkv");
            }
            if (File.Exists(outputName)) File.Delete(outputName);

            if (await _encoder.EncodeAsync(src, outputName, includeEnglishSubtitles: true))
            {
                var final = RenameFile(outputName, metadata!, options.Tv ? ((options.EpisodeStart - 1) + i + 1) : null, options.Season, versionSuffix);
                finalFiles.Add(final);
            }
        }

        Console.WriteLine($"Processing complete. Output files: {finalFiles.Count}");
        foreach (var f in finalFiles) Console.WriteLine(f);
        return finalFiles;
    }

    private static string RenameFile(string filePath, Metadata metadata, int? episodeNum, int seasonNum, string? versionSuffix = null)
    {
        var title = metadata.Title.Trim();
        var year = metadata.Year;
        var mediaType = metadata.Type;
        var safeTitle = SanitizeFileName(title);
        string filename;
        if (mediaType == "tv" && episodeNum.HasValue)
        {
            filename = $"{safeTitle} - S{seasonNum:00}E{episodeNum.Value:00}.mkv";
        }
        else
        {
            var yearPart = year.HasValue ? $" ({year.Value})" : "";
            var suffix = versionSuffix ?? "";
            filename = $"{safeTitle}{yearPart}{suffix}.mkv";
        }
        var newPath = Path.Combine(Path.GetDirectoryName(filePath)!, filename);
        if (File.Exists(newPath)) File.Delete(newPath);
        File.Move(filePath, newPath);
        return newPath;
    }

    private static string SanitizeFileName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            s = s.Replace(ch.ToString(), "");
        return s.Trim();
    }

    private static string? ExtractQuoted(string line)
    {
        var idx = line.IndexOf('"');
        if (idx < 0) return null;
        var idx2 = line.IndexOf('"', idx + 1);
        if (idx2 < 0) return null;
        return line.Substring(idx + 1, idx2 - idx - 1);
    }
}
