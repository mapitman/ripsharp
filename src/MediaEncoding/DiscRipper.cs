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
    private readonly IProgressNotifier _notifier;
    private readonly IMakeMkvService _makeMkv;

    public DiscRipper(IDiscScanner scanner, IEncoderService encoder, IMetadataService metadata, IMakeMkvService makeMkv, IProgressNotifier notifier)
    {
        _scanner = scanner;
        _encoder = encoder;
        _metadata = metadata;
        _makeMkv = makeMkv;
        _notifier = notifier;
    }

    public async Task<List<string>> ProcessDiscAsync(RipOptions options)
    {
        PrepareDirectories(options);
        var (discInfo, metadata) = await ScanDiscAndLookupMetadata(options);
        var titleIds = IdentifyTitlesToRip(discInfo, options);
        if (titleIds.Count == 0)
        {
            _notifier.Error("No suitable titles found on disc");
            return new List<string>();
        }
        _notifier.Accent($"Found {titleIds.Count} title(s) to rip: [{string.Join(", ", titleIds)}]");

        var rippedFilesMap = await RipTitlesAsync(discInfo, titleIds, options);

        if (metadata is null)
        {
            _notifier.Error("Metadata lookup failed; unable to encode and rename titles.");
            return new List<string>();
        }

        var finalFiles = await EncodeAndRenameAsync(discInfo, titleIds, rippedFilesMap, metadata, options);
        _notifier.Success($"Processing complete. Output files: {finalFiles.Count}");
        foreach (var f in finalFiles) _notifier.Plain(f);
        return finalFiles;
    }

    private static void PrepareDirectories(RipOptions options)
    {
        Directory.CreateDirectory(options.Output);
        Directory.CreateDirectory(options.Temp!);
    }

    private async Task<(DiscInfo discInfo, Metadata? metadata)> ScanDiscAndLookupMetadata(RipOptions options)
    {
        var discInfo = await _scanner.ScanDiscAsync(options.Disc);
        if (discInfo == null)
        {
            _notifier.Error("Failed to scan disc");
            return (new DiscInfo { Titles = new List<TitleInfo>() }, null);
        }
        var titleForMeta = options.Title ?? discInfo.DiscName;
        if (string.IsNullOrWhiteSpace(titleForMeta))
        {
            titleForMeta = options.Tv ? "TV Episode" : "Movie";
            _notifier.Warning($"No title specified and disc name unavailable. Using fallback: '{titleForMeta}'");
        }
        var metadata = await _metadata.LookupAsync(titleForMeta, options.Tv, options.Year);
        return (discInfo, metadata);
    }

    private List<int> IdentifyTitlesToRip(DiscInfo discInfo, RipOptions options)
    {
        var titleIds = _scanner.IdentifyMainContent(discInfo, options.Tv);
        return titleIds;
    }

    private async Task<Dictionary<int, string>> RipTitlesAsync(DiscInfo discInfo, List<int> titleIds, RipOptions options)
    {
        var rippedFilesMap = new Dictionary<int, string>();
        var totalTitles = titleIds.Count;
        var preExistingRips = new Queue<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderBy(File.GetCreationTime));
        for (int idx = 0; idx < titleIds.Count; idx++)
        {
            var titleId = titleIds[idx];
            var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
            var titleName = titleInfo?.Name;

            if (preExistingRips.Count > 0)
            {
                var reused = preExistingRips.Dequeue();
                _notifier.Muted($"Using existing ripped file for title {idx + 1} of {totalTitles} (Title ID: {titleId}) -> {Path.GetFileName(reused)}");
                rippedFilesMap[titleId] = reused;
                continue;
            }

            _notifier.Info($"Ripping title {idx + 1} of {totalTitles} (Title ID: {titleId}){(string.IsNullOrWhiteSpace(titleName) ? "" : $" - {titleName}")} [{DurationFormatter.Format(titleInfo?.DurationSeconds ?? 0)}]");
            var existingFiles = new HashSet<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv"));
            var progressLogPath = Path.Combine(options.Temp!, $"progress_title_{titleId:D2}.log");
            if (File.Exists(progressLogPath)) File.Delete(progressLogPath);

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ElapsedTimeColumn { Style = Color.Green },
                    new ProgressBarColumn(),
                    new PercentageColumn{ Style = Color.Yellow },
                    new RemainingTimeColumn { Style = Color.Blue },
                    new SpinnerColumn(),
                })
                .StartAsync(async ctx =>
                {
                    var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
                    var maxValue = expectedBytes > 0 ? expectedBytes : 100;
                    var task = ctx.AddTask($"[{ConsoleColors.Success}]Title {titleId} ({idx + 1}/{totalTitles})[/]", maxValue: maxValue);
                    bool ripDone = false;

                    var pollTask = Task.Run(async () =>
                    {
                        double lastSizeLocal = 0;
                        while (!ripDone)
                        {
                            try
                            {
                                var mkv = Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderByDescending(File.GetCreationTime).FirstOrDefault();
                                if (mkv != null && expectedBytes > 0)
                                {
                                    var size = new FileInfo(mkv).Length;
                                    lastSizeLocal = Math.Max(lastSizeLocal, size);
                                    task.Value = Math.Min(expectedBytes, lastSizeLocal);
                                }
                            }
                            catch { }
                            await Task.Delay(1000);
                        }
                    });

                    var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{titleId:D2}.log");
                    var handler = new MakeMkvOutputHandler(expectedBytes, idx, totalTitles, task, progressLogPath, rawLogPath);
                    var exit = await _makeMkv.RipTitleAsync(options.Disc, titleId, options.Temp!,
                        onOutput: handler.HandleLine,
                        onError: errLine =>
                        {
                            if (!(errLine.StartsWith("PRGV:") || errLine.StartsWith("PRGC:")))
                            {
                                _notifier.Error(errLine);
                            }
                            handler.HandleLine(errLine);
                        });
                    ripDone = true;
                    try { await pollTask; } catch { }

                    if (exit != 0)
                    {
                        task.Description = $"[{ConsoleColors.Error}]Failed: Title {titleId}[/]";
                        task.StopTask();
                        _notifier.Error($"Failed to rip title {titleId}");
                        return;
                    }
                    if (handler.LastBytesProcessed < maxValue)
                    {
                        task.Value = maxValue;
                    }
                    task.StopTask();
                });

            var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
            if (newFiles.Count > 0)
            {
                var ripped = newFiles.OrderByDescending(File.GetCreationTime).First();
                rippedFilesMap[titleId] = ripped;
            }
        }
        return rippedFilesMap;
    }

    private async Task<List<string>> EncodeAndRenameAsync(DiscInfo discInfo, List<int> titleIds, Dictionary<int, string> rippedFilesMap, Metadata metadata, RipOptions options)
    {
        var finalFiles = new List<string>();
        foreach (var titleId in titleIds)
        {
            if (!rippedFilesMap.TryGetValue(titleId, out var src))
            {
                _notifier.Error($"No ripped file found for title {titleId}");
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
                var safeTitle = !string.IsNullOrWhiteSpace(titleName) ? FileNaming.SanitizeFileName(titleName!) : $"movie_{titleIds.IndexOf(titleId) + 1}";
                versionSuffix = $" - title{titleId:D2}";
                var safeVersionSuffix = FileNaming.SanitizeFileName(versionSuffix);
                outputName = Path.Combine(options.Output, $"{safeTitle}{safeVersionSuffix}.mkv");
            }
            if (File.Exists(outputName)) File.Delete(outputName);

            if (await _encoder.EncodeAsync(src, outputName, includeEnglishSubtitles: true))
            {
                var episodeIdx = options.Tv ? titleIds.IndexOf(titleId) : (int?)null;
                var episodeNum = episodeIdx.HasValue ? (options.EpisodeStart - 1) + episodeIdx.Value + 1 : (int?)null;
                var final = FileNaming.RenameFile(outputName, metadata!, episodeNum, options.Season, versionSuffix);
                finalFiles.Add(final);
            }
        }
        return finalFiles;
    }
}
