using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Spectre.Console;

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

        var totalTitles = titleIds.Count;
        var rippedFilesMap = new Dictionary<int, string>();  // Map titleId -> ripped file path

        // Allow recovery: if the temp folder already has MKVs (e.g., previous run interrupted),
        // reuse them without re-ripping. We assign in discovery order to requested titles.
        var preExistingRips = new Queue<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderBy(File.GetCreationTime));

        for (int idx = 0; idx < titleIds.Count; idx++)
        {
            var titleId = titleIds[idx];
            var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
            var titleName = titleInfo?.Name;

            // Recovery path: use existing rip if present
            if (preExistingRips.Count > 0)
            {
                var reused = preExistingRips.Dequeue();
                Console.WriteLine($"↩️ Using existing ripped file for title {idx + 1} of {totalTitles} (Title ID: {titleId}) -> {Path.GetFileName(reused)}");
                rippedFilesMap[titleId] = reused;
                continue;
            }

            Console.WriteLine($"🎬 Ripping title {idx + 1} of {totalTitles} (Title ID: {titleId}){(string.IsNullOrWhiteSpace(titleName) ? "" : $" - {titleName}")}");
            
            // Track existing files before rip
            var existingFiles = new HashSet<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv"));
            
            var progressLogPath = Path.Combine(options.Temp!, $"progress_title_{titleId:D2}.log");
            if (File.Exists(progressLogPath)) File.Delete(progressLogPath);

            // makemkvcon mkv disc:0 {titleId} tempDir
            var args = $"-r --robot mkv {options.Disc} {titleId} \"{options.Temp}\"";

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[green]Title {titleId} ({idx + 1}/{totalTitles})[/]", maxValue: 100);
                    double lastPct = 0;
                    var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
                    var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
                    bool ripDone = false;

                    var pollTask = Task.Run(async () =>
                    {
                        while (!ripDone)
                        {
                            try
                            {
                                var mkv = Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderByDescending(File.GetCreationTime).FirstOrDefault();
                                if (mkv != null && expectedBytes > 0)
                                {
                                    var size = new FileInfo(mkv).Length;
                                    var pct = Math.Min(100.0, Math.Max(lastPct, (size * 100.0) / expectedBytes));
                                    task.Value = pct;
                                    lastPct = pct;
                                }
                            }
                            catch { }
                            await Task.Delay(1000);
                        }
                    });

                    var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{titleId:D2}.log");
                    void HandleMakemkvLine(string line)
                    {
                        try { File.AppendAllText(rawLogPath, line + "\n"); } catch { }
                        if (line.StartsWith("PRGV:"))
                        {
                            var m = Regex.Match(line, @"PRGV:\s*([0-9]+(?:\.[0-9]+)?)");
                            if (m.Success)
                            {
                                if (double.TryParse(m.Groups[1].Value, out var raw))
                                {
                                    double pct = raw;
                                    if (pct <= 1.0) pct *= 100.0; // fraction to percent
                                    while (pct > 100.0) pct /= 10.0; // scale down if 1000-based
                                    pct = Math.Max(0, Math.Min(100, pct));
                                    task.Value = pct;
                                    lastPct = pct;
                                    try { File.AppendAllText(progressLogPath, $"PRGV {pct:F1}\n"); } catch { }
                                }
                            }
                        }
                        else if (line.StartsWith("PRGC:"))
                        {
                            var caption = ExtractQuoted(line);
                            if (!string.IsNullOrEmpty(caption))
                            {
                                task.Description = $"[green]{caption} ({idx + 1}/{totalTitles})[/]";
                            }
                            try { File.AppendAllText(progressLogPath, $"PRGC {caption}\n"); } catch { }
                        }
                    }

                    var exit = await _runner.RunAsync("makemkvcon", args, onOutput: HandleMakemkvLine, onError: errLine =>
                    {
                        if (!(errLine.StartsWith("PRGV:") || errLine.StartsWith("PRGC:")))
                        {
                            Console.Error.WriteLine(errLine);
                        }
                        HandleMakemkvLine(errLine);
                    });
                    ripDone = true;
                    try { await pollTask; } catch { }

                    if (exit != 0)
                    {
                        task.Description = $"[red]Failed: Title {titleId}[/]";
                        task.StopTask();
                        Console.Error.WriteLine($"Failed to rip title {titleId}");
                        return;
                    }

                    if (lastPct < 100)
                    {
                        task.Value = 100;
                    }
                    task.StopTask();
                });

            // Determine ripped file path - find file that didn't exist before
            var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
            if (newFiles.Count > 0)
            {
                // Use the newest file that wasn't there before
                var ripped = newFiles.OrderByDescending(File.GetCreationTime).First();
                rippedFilesMap[titleId] = ripped;  // Store with titleId as key
            }
        }

        // Encode and rename all ripped files
        var finalFiles = new List<string>();
        foreach (var titleId in titleIds)
        {
            if (!rippedFilesMap.TryGetValue(titleId, out var src))
            {
                Console.Error.WriteLine($"No ripped file found for title {titleId}");
                continue;
            }
            
            var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
            var titleName = titleInfo?.Name;
            string outputName;
            string? versionSuffix = null;
            
            if (options.Tv)
            {
                var episodeIdx = titleIds.IndexOf(titleId);
                var episodeNum = (options.EpisodeStart - 1) + episodeIdx + 1;
                outputName = Path.Combine(options.Output, $"temp_s{options.Season:00}e{episodeNum:00}.mkv");
            }
            else
            {
                // Use disc title name for output if available, otherwise fallback, and append a per-title suffix to avoid overwrites
                var safeTitle = !string.IsNullOrWhiteSpace(titleName) ? SanitizeFileName(titleName!) : $"movie_{titleIds.IndexOf(titleId) + 1}";
                versionSuffix = $" - title{titleId:D2}";
                var safeVersionSuffix = SanitizeFileName(versionSuffix);
                outputName = Path.Combine(options.Output, $"{safeTitle}{safeVersionSuffix}.mkv");
            }
            if (File.Exists(outputName)) File.Delete(outputName);

            if (await _encoder.EncodeAsync(src, outputName, includeEnglishSubtitles: true))
            {
                var episodeIdx = options.Tv ? titleIds.IndexOf(titleId) : (int?)null;
                var episodeNum = episodeIdx.HasValue ? (options.EpisodeStart - 1) + episodeIdx.Value + 1 : (int?)null;
                var final = RenameFile(outputName, metadata!, episodeNum, options.Season, versionSuffix);
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
        var safeSuffix = string.IsNullOrWhiteSpace(versionSuffix) ? "" : SanitizeFileName(versionSuffix);
        string filename;
        if (mediaType == "tv" && episodeNum.HasValue)
        {
            filename = $"{safeTitle} - S{seasonNum:00}E{episodeNum.Value:00}.mkv";
        }
        else
        {
            var yearPart = year.HasValue ? $" ({year.Value})" : "";
            filename = $"{safeTitle}{yearPart}{safeSuffix}.mkv";
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
