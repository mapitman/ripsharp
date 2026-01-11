using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace RipSharp;

public class DiscRipper : IDiscRipper
{
    private readonly IDiscScanner _scanner;
    private readonly IEncoderService _encoder;
    private readonly IMetadataService _metadata;
    private readonly IProgressNotifier _notifier;
    private readonly IMakeMkvService _makeMkv;
    private readonly IUserPrompt _userPrompt;
    private readonly ITvEpisodeTitleProvider _episodeTitles;

    public DiscRipper(IDiscScanner scanner, IEncoderService encoder, IMetadataService metadata, IMakeMkvService makeMkv, IProgressNotifier notifier, IUserPrompt userPrompt, ITvEpisodeTitleProvider episodeTitles)
    {
        _scanner = scanner;
        _encoder = encoder;
        _metadata = metadata;
        _makeMkv = makeMkv;
        _notifier = notifier;
        _userPrompt = userPrompt;
        _episodeTitles = episodeTitles;
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

        // Use auto-detected content type if requested
        if (options.AutoDetect)
        {
            const double minConfidenceThreshold = 0.70;
            
            if (discInfo.DetectedContentType.HasValue && discInfo.DetectionConfidence >= minConfidenceThreshold)
            {
                var contentType = discInfo.DetectedContentType.Value ? "TV series" : "movie";
                var emoji = discInfo.DetectedContentType.Value ? "\ud83d\udcfa " : "\ud83c\udfac "; // 📺 for TV, 🎬 for movie
                var confidencePercent = (int)(discInfo.DetectionConfidence * 100);
                _notifier.Info($"{emoji}Detected as {contentType} (confidence: {confidencePercent}%)");
                options.Tv = discInfo.DetectedContentType.Value;
            }
            else
            {
                // Low confidence or uncertain - prompt user
                // Note: Modifying options.Tv in-place based on detection result or user's choice
                string? detectionHint = null;
                if (discInfo.DetectedContentType.HasValue)
                {
                    var contentType = discInfo.DetectedContentType.Value ? "TV series" : "movie";
                    var confidencePercent = (int)(discInfo.DetectionConfidence * 100);
                    detectionHint = $"detected as {contentType} with {confidencePercent}% confidence";
                }
                
                options.Tv = _userPrompt.PromptForContentType(detectionHint);
            }
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
                    var task = ctx.AddTask($"[{ConsoleColors.Success}]Title {idx + 1} ({idx + 1}/{totalTitles})[/]", maxValue: maxValue);
                    bool ripDone = false;

                    var pollTask = Task.Run(async () =>
                    {
                        double lastSizeLocal = 0;
                        string? currentMkv = null;
                        while (!ripDone)
                        {
                            try
                            {
                                // Identify the mkv file being written for this title: the first new mkv not in existingFiles
                                if (currentMkv == null)
                                {
                                    currentMkv = Directory
                                        .EnumerateFiles(options.Temp!, "*.mkv")
                                        .FirstOrDefault(f => !existingFiles.Contains(f));
                                }

                                if (currentMkv != null && expectedBytes > 0)
                                {
                                    var size = new FileInfo(currentMkv).Length;
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
                var ordinal = titleIds.IndexOf(titleId) + 1;
                var safeTitle = !string.IsNullOrWhiteSpace(titleName) ? FileNaming.SanitizeFileName(titleName!) : $"movie_{ordinal}";
                versionSuffix = $" - title{ordinal:D2}";
                var safeVersionSuffix = FileNaming.SanitizeFileName(versionSuffix);
                outputName = Path.Combine(options.Output, $"{safeTitle}{safeVersionSuffix}.mkv");
            }
            if (File.Exists(outputName)) File.Delete(outputName);

            if (await _encoder.EncodeAsync(src, outputName, includeEnglishSubtitles: true, ordinal: titleIds.IndexOf(titleId) + 1, total: titleIds.Count))
            {
                var episodeIdx = options.Tv ? titleIds.IndexOf(titleId) : (int?)null;
                var episodeNum = episodeIdx.HasValue ? (options.EpisodeStart - 1) + episodeIdx.Value + 1 : (int?)null;
                string? episodeTitle = null;
                if (options.Tv && episodeNum.HasValue)
                {
                    episodeTitle = await _episodeTitles.GetEpisodeTitleAsync(metadata!.Title, options.Season, episodeNum.Value, metadata.Year);
                }
                var final = FileNaming.RenameFile(outputName, metadata!, episodeNum, options.Season, versionSuffix, episodeTitle);
                finalFiles.Add(final);
            }
        }
        return finalFiles;
    }
}
