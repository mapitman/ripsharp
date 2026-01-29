using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Spectre.Console;
using RipSharp.Abstractions;


namespace RipSharp.Services;

// Job records for channel communication
public record RipJob(
    int TitleId,
    int Index,
    string RippedFilePath,
    TitleInfo TitleInfo);

public record EncodeResult(
    int TitleId,
    bool Success,
    string? FinalPath = null,
    string? ErrorMessage = null);

public class DiscRipper : IDiscRipper
{
    private const int RipProgressScale = 100;
    private readonly IDiscScanner _scanner;
    private readonly IEncoderService _encoder;
    private readonly IMetadataService _metadata;
    private readonly IConsoleWriter _notifier;
    private readonly IMakeMkvService _makeMkv;
    private readonly IUserPrompt _userPrompt;
    private readonly ITvEpisodeTitleProvider _episodeTitles;
    private readonly IProgressDisplay _progressDisplay;

    private record TitlePlan(
        int TitleId,
        int Index,
        int? EpisodeNum,
        string? EpisodeTitle,
        string TempOutputPath,
        string FinalFileName,
        string? VersionSuffix,
        string DisplayName);

    public DiscRipper(IDiscScanner scanner, IEncoderService encoder, IMetadataService metadata, IMakeMkvService makeMkv, IConsoleWriter notifier, IUserPrompt userPrompt, ITvEpisodeTitleProvider episodeTitles, IProgressDisplay progressDisplay)
    {
        _scanner = scanner;
        _encoder = encoder;
        _metadata = metadata;
        _makeMkv = makeMkv;
        _notifier = notifier;
        _userPrompt = userPrompt;
        _episodeTitles = episodeTitles;
        _progressDisplay = progressDisplay;
    }

    public async Task<List<string>> ProcessDiscAsync(RipOptions options, CancellationToken cancellationToken = default)
    {
        PrepareDirectories(options);
        var (discInfo, metadata) = await ScanDiscAndLookupMetadata(options);
        var titleIds = IdentifyTitlesToRip(discInfo, options);
        
        if (titleIds.Count == 0)
        {
            _notifier.Error("No suitable titles found on disc");
            return new List<string>();
        }

        if (metadata is null)
        {
            _notifier.Error("ContentMetadata? lookup failed; unable to encode and rename titles.");
            return new List<string>();
        }

        _notifier.Accent($"Found {titleIds.Count} title(s) to rip: [{string.Join(", ", titleIds)}]");

        // Use parallel processing by default
        var finalFiles = options.EnableParallelProcessing
            ? await ProcessDiscParallelAsync(discInfo, titleIds, metadata, options, cancellationToken)
            : await ProcessDiscSequentialAsync(discInfo, titleIds, metadata, options, cancellationToken);

        if (finalFiles.Count > 0)
        {
            CleanupTempDirectory(options);
        }
        else if (Directory.EnumerateFiles(options.Temp!, "*.mkv").Any())
        {
            _notifier.Error($"No files were successfully encoded; temporary files have been left in: {options.Temp}");
        }

        _notifier.Success($"Processing complete. Output files: {finalFiles.Count}");
        foreach (var f in finalFiles) _notifier.Plain(f);
        return finalFiles;
    }

    private async Task<List<string>> ProcessDiscSequentialAsync(DiscInfo discInfo, List<int> titleIds, ContentMetadata metadata, RipOptions options, CancellationToken cancellationToken)
    {
        var rippedFilesMap = await RipTitlesAsync(discInfo, titleIds, options);
        return await EncodeAndRenameAsync(discInfo, titleIds, rippedFilesMap, metadata, options);
    }

    private async Task<List<TitlePlan>> BuildTitlePlansAsync(DiscInfo discInfo, List<int> titleIds, ContentMetadata metadata, RipOptions options)
    {
        var plans = new List<TitlePlan>(titleIds.Count);
        var safeSeriesTitle = FileNaming.SanitizeFileName(metadata.Title);

        for (var idx = 0; idx < titleIds.Count; idx++)
        {
            var titleId = titleIds[idx];
            var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
            var titleName = titleInfo?.Name;

            int? episodeNum = null;
            string? episodeTitle = null;
            string? versionSuffix = null;
            string tempOutputPath;
            string finalFileName;
            string displayName;

            if (options.Tv)
            {
                episodeNum = (options.EpisodeStart - 1) + idx + 1;
                // Fetch episode title early so we can display/name immediately
                episodeTitle = await _episodeTitles.GetEpisodeTitleAsync(metadata.Title, options.Season, episodeNum.Value, metadata.Year);

                var safeEpisodeTitle = string.IsNullOrWhiteSpace(episodeTitle) ? "" : $" - {FileNaming.SanitizeFileName(episodeTitle)}";
                finalFileName = $"{safeSeriesTitle} - S{options.Season:00}E{episodeNum:00}{safeEpisodeTitle}.mkv";
                tempOutputPath = Path.Combine(options.Output, $"temp_s{options.Season:00}e{episodeNum:00}.mkv");
                displayName = string.IsNullOrWhiteSpace(episodeTitle)
                    ? $"{metadata.Title} S{options.Season:00}E{episodeNum:00}"
                    : $"{metadata.Title} S{options.Season:00}E{episodeNum:00} - {episodeTitle}";
            }
            else
            {
                var ordinal = idx + 1;
                versionSuffix = $" - title{ordinal:D2}";
                var safeVersionSuffix = FileNaming.SanitizeFileName(versionSuffix);
                var yearPart = metadata.Year.HasValue ? $" ({metadata.Year.Value})" : "";
                var safeTitle = !string.IsNullOrWhiteSpace(titleName) ? FileNaming.SanitizeFileName(titleName!) : safeSeriesTitle;
                finalFileName = $"{safeTitle}{yearPart}{safeVersionSuffix}.mkv";
                tempOutputPath = Path.Combine(options.Output, $"{safeTitle}{safeVersionSuffix}.mkv");
                displayName = !string.IsNullOrWhiteSpace(titleName)
                    ? titleName!
                    : metadata.Title;
            }

            plans.Add(new TitlePlan(titleId, idx, episodeNum, episodeTitle, tempOutputPath, finalFileName, versionSuffix, displayName));
        }

        return plans;
    }

    private async Task<List<string>> ProcessDiscParallelAsync(DiscInfo discInfo, List<int> titleIds, ContentMetadata metadata, RipOptions options, CancellationToken cancellationToken)
    {
        var ripChannel = Channel.CreateUnbounded<RipJob>();
        var resultChannel = Channel.CreateUnbounded<EncodeResult>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finalFiles = new List<string>();
        var titlePlans = await BuildTitlePlansAsync(discInfo, titleIds, metadata, options);

        try
        {
            await _progressDisplay.ExecuteAsync(async ctx =>
            {
                var ripProgress = ctx.AddTask("Ripping", maxValue: RipProgressScale); // Per-track progress (0-100)
                var encodeProgress = ctx.AddTask("Encoding", maxValue: RipProgressScale); // Per-encode progress (0-100)
                encodeProgress.AddMessage("Waiting for rip to complete...");
                var overallProgress = ctx.AddTask("Overall", maxValue: titleIds.Count * RipProgressScale * 2);

                // Start both ripping and encoding tasks in parallel
                var ripTask = Task.Run(() => RipProducerAsync(ripChannel, discInfo, titleIds, titlePlans, options, ripProgress, overallProgress, cts.Token));
                var encodeTask = Task.Run(() => EncodeConsumerAsync(ripChannel, resultChannel, titlePlans, metadata, options, encodeProgress, overallProgress, cts.Token));
                var collectTask = CollectResultsAsync(resultChannel, titleIds.Count, cts.Token);

                // Wait for both to complete
                await Task.WhenAll(ripTask, encodeTask);

                // Stop progress bars
                ripProgress.StopTask();
                encodeProgress.StopTask();
                overallProgress.StopTask();
                
                finalFiles = await collectTask;
            });

            return finalFiles;
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw to let Program.cs handle the message
        }
        finally
        {
            cts.Dispose();
        }
    }

    private static void PrepareDirectories(RipOptions options)
    {
        Directory.CreateDirectory(options.Output);
        Directory.CreateDirectory(options.Temp!);
    }

    private async Task<(DiscInfo discInfo, ContentMetadata? metadata)> ScanDiscAndLookupMetadata(RipOptions options)
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

            await _progressDisplay.ExecuteAsync(async ctx =>
            {
                var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
                var maxValue = expectedBytes > 0 ? expectedBytes : 100;
                var task = ctx.AddTask($"[{ConsoleColors.Success}]Title {idx + 1} ({idx + 1}/{totalTitles})[/]", maxValue);
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
                                task.Value = (long)Math.Min(expectedBytes, lastSizeLocal);
                            }
                        }
                        catch { }
                        await Task.Delay(1000);
                    }
                });

                var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{titleId:D2}.log");
                var handler = new MakeMkvOutputHandler(expectedBytes, idx, totalTitles, task, progressLogPath, rawLogPath, _notifier);
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

    private async Task RipProducerAsync(Channel<RipJob> ripChannel, DiscInfo discInfo, List<int> titleIds, IReadOnlyList<TitlePlan> plans, RipOptions options, IProgressTask ripProgress, IProgressTask overallProgress, CancellationToken cancellationToken)
    {
        try
        {
            var totalTitles = titleIds.Count;
            var preExistingRips = new Queue<string>(Directory.Exists(options.Temp!) ? Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderBy(File.GetCreationTime) : Enumerable.Empty<string>());

            for (int idx = 0; idx < titleIds.Count; idx++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var titleId = titleIds[idx];
                var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
                
                if (titleInfo == null)
                {
                    var msg = $"Title {titleId} not found in disc info, skipping";
                    ripProgress.AddMessage(msg);
                    overallProgress.Value += RipProgressScale;
                    continue;
                }

                var plan = plans[idx];

                // Check for pre-existing rips
                if (preExistingRips.Count > 0)
                {
                    var reused = preExistingRips.Dequeue();
                    var msg = $"Using existing ripped file for title {idx + 1} of {totalTitles}: {plan.DisplayName} (Title ID: {titleId}) -> {Path.GetFileName(reused)}";
                    ripProgress.AddMessage(msg);

                    await ripChannel.Writer.WriteAsync(new RipJob(titleId, idx, reused, titleInfo), cancellationToken);
                    ripProgress.Description = $"{plan.DisplayName} [100%]";
                    ripProgress.Value += RipProgressScale;
                    overallProgress.Value += RipProgressScale;
                    continue;
                }

                // Perform actual rip with live progress contribution
                var rippedPath = await PerformSingleRipAsync(titleId, idx, titleInfo, plan, totalTitles, options, ripProgress, overallProgress);

                if (!string.IsNullOrEmpty(rippedPath))
                {
                    await ripChannel.Writer.WriteAsync(new RipJob(titleId, idx, rippedPath, titleInfo), cancellationToken);
                }
                else
                {
                    var msg = $"Failed to rip title {titleId}, skipping";
                    ripProgress.AddMessage(msg);
                }
            }
        }
        finally
        {
            ripChannel.Writer.Complete();
        }
    }

    private async Task<string?> PerformSingleRipAsync(int titleId, int idx, TitleInfo? titleInfo, TitlePlan plan, int totalTitles, RipOptions options, IProgressTask ripProgress, IProgressTask overallProgress)
    {
        var baseProgressOverall = idx * RipProgressScale;
        
        // Reset ripProgress for this track (show 0-100% per track)
        ripProgress.Value = 0;
        ripProgress.ClearMessages(); // Clear messages from previous track
        
        var msg = $"Ripping title {idx + 1} of {totalTitles}: {plan.DisplayName} (Title ID: {titleId}) [{DurationFormatter.Format(titleInfo?.DurationSeconds ?? 0)}]";
        ripProgress.AddMessage(msg);

        var existingFiles = new HashSet<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv"));
        var progressLogPath = Path.Combine(options.Temp!, $"progress_title_{titleId:D2}.log");
        if (File.Exists(progressLogPath)) File.Delete(progressLogPath);

        string? rippedPath = null;

        var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
        var durationSeconds = titleInfo?.DurationSeconds ?? 0;
        var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{titleId:D2}.log");
        var handler = new MakeMkvOutputHandler(expectedBytes, idx, totalTitles, null, progressLogPath, rawLogPath, _notifier);

        bool ripDone = false;
        double observedMaxBytes = 1; // prevent divide-by-zero
        double displayedFraction = 0;
        string? currentMkv = null;
        var ripStartTime = DateTime.UtcNow;

        var pollTask = Task.Run(async () =>
        {
            int pollCount = 0;
            while (!ripDone)
            {
                try
                {
                    pollCount++;
                    
                    // Identify MKV being written if we haven't yet
                    if (currentMkv == null)
                    {
                        currentMkv = Directory
                            .EnumerateFiles(options.Temp!, "*.mkv")
                            .FirstOrDefault(f => !existingFiles.Contains(f));
                    }

                    // Prefer fraction parsed from PRGV when available
                    var fraction = Math.Clamp(handler.LastProgressFraction, 0, 1);

                    // If we know expected bytes, normalize by bytes processed
                    if (fraction == 0 && expectedBytes > 0 && handler.LastBytesProcessed > 0)
                    {
                        fraction = Math.Clamp(handler.LastBytesProcessed / Math.Max(1.0, expectedBytes), 0, 1);
                    }

                    // If MakeMKV never emits PRGV/bytes, fall back to elapsed time vs. title duration (best-effort)
                    if (fraction == 0 && durationSeconds > 0)
                    {
                        var elapsedSecs = DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds;
                        // Assume ~1x read speed with a little slack
                        var denom = Math.Max(10.0, durationSeconds * 1.2);
                        fraction = Math.Clamp(elapsedSecs / denom, 0, 1);
                    }

                    // If size is unknown and we have raw byte progress, derive a monotonic fraction from growth
                    if (fraction == 0 && expectedBytes == 0 && handler.LastBytesProcessed > 0)
                    {
                        if (handler.LastBytesProcessed > observedMaxBytes)
                        {
                            observedMaxBytes = handler.LastBytesProcessed;
                        }

                        var candidate = observedMaxBytes > 0 ? handler.LastBytesProcessed / observedMaxBytes : 0;
                        displayedFraction = Math.Max(displayedFraction, Math.Clamp(candidate, 0, 1));
                        fraction = displayedFraction;
                    }

                    // As a last resort, use file size growth (monotonic) when we have no size estimate
                    if (fraction == 0 && expectedBytes == 0 && currentMkv != null)
                    {
                        try
                        {
                            var size = new FileInfo(currentMkv).Length;
                            if (size > observedMaxBytes) observedMaxBytes = size;
                            // Use relative growth (monotonic), capped so the bar moves but never hits 100% from this alone
                            var candidate = observedMaxBytes > 0 ? size / observedMaxBytes : 0;
                            displayedFraction = Math.Max(displayedFraction, Math.Clamp(candidate * 0.8, 0, 0.99));
                            fraction = displayedFraction;
                        }
                        catch { }
                    }

                    // If rip has been running for a while but no progress yet, show minimal progress to indicate activity
                    if (fraction == 0 && DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds > 3)
                    {
                        var elapsedSecs = DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds;
                        var minimalFraction = Math.Min(0.1, elapsedSecs / 60.0); // nudge up to 10% over a minute
                        displayedFraction = Math.Max(displayedFraction, minimalFraction);
                        fraction = displayedFraction;
                    }

                    var fractionalProgress = (long)Math.Round(fraction * RipProgressScale);
                    ripProgress.Value = fractionalProgress; // Show only current track progress (0-100%)
                    var newOverall = baseProgressOverall + fractionalProgress;
                    overallProgress.Value = Math.Max(overallProgress.Value, newOverall);
                    
                }
                catch (Exception ex)
                {
                    _notifier.Error($"Rip progress polling error: {ex.Message}");
                }
                await Task.Delay(500);
            }
        });

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
        try
        {
            await pollTask;
        }
        catch (Exception ex)
        {
            _notifier.Error($"Error while monitoring rip progress: {ex}");
        }
        if (exit == 0)
        {
            var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
            if (newFiles.Count > 0)
            {
                rippedPath = newFiles.OrderByDescending(File.GetCreationTime).First();
            }
        }

        // Snap progress to the completed title
        ripProgress.Value = RipProgressScale; // Show 100% for current track
        overallProgress.Value = baseProgressOverall + RipProgressScale;
        return rippedPath;
    }

    private async Task EncodeConsumerAsync(Channel<RipJob> ripChannel, Channel<EncodeResult> resultChannel, IReadOnlyList<TitlePlan> titlePlans, ContentMetadata metadata, RipOptions options, IProgressTask encodeProgress, IProgressTask overallProgress, CancellationToken cancellationToken)
    {
        try
        {
            var totalTitles = titlePlans.Count;
            int processedCount = 0;
            var planLookup = titlePlans.ToDictionary(p => p.TitleId);

            await foreach (var ripJob in ripChannel.Reader.ReadAllAsync(cancellationToken))
            {
                processedCount++;

                if (!planLookup.TryGetValue(ripJob.TitleId, out var plan))
                {
                    await resultChannel.Writer.WriteAsync(new EncodeResult(ripJob.TitleId, false, ErrorMessage: $"Missing plan for title {ripJob.TitleId}"), cancellationToken);
                    overallProgress.Value += RipProgressScale;
                    continue;
                }

                var outputPath = plan.TempOutputPath;
                var versionSuffix = plan.VersionSuffix;
                var episodeNum = plan.EpisodeNum;

                if (File.Exists(outputPath)) File.Delete(outputPath);

                // Reset encodeProgress for this job (show 0-100% per encode)
                encodeProgress.Value = 0;
                encodeProgress.ClearMessages(); // Clear messages from previous encoding

                var encMsg = $"Encoding ({processedCount}/{totalTitles}): {plan.FinalFileName}";
                encodeProgress.AddMessage(encMsg);

                // Perform encoding
                var success = await _encoder.EncodeAsync(
                    ripJob.RippedFilePath,
                    outputPath,
                    includeEnglishSubtitles: true,
                    ordinal: processedCount,
                    total: totalTitles,
                    progressTask: encodeProgress);

                if (success)
                {
                    // Rename to final output
                    var finalPath = FileNaming.RenameFile(
                        outputPath, metadata, episodeNum,
                        options.Season, versionSuffix, plan.EpisodeTitle);

                    await resultChannel.Writer.WriteAsync(new EncodeResult(
                        ripJob.TitleId,
                        true,
                        finalPath), cancellationToken);
                    encodeProgress.Value = RipProgressScale; // Show 100% for current encode
                    overallProgress.Value += RipProgressScale;
                }
                else
                {
                    await resultChannel.Writer.WriteAsync(new EncodeResult(
                        ripJob.TitleId,
                        false,
                        ErrorMessage: $"Failed to encode title {ripJob.TitleId}"), cancellationToken);
                    encodeProgress.Value = RipProgressScale; // Show 100% even on failure
                    overallProgress.Value += RipProgressScale;
                }
            }

            // Ensure the encoding bar completes if it was created
            if (encodeProgress != null)
            {
                encodeProgress.Value = encodeProgress.MaxValue;
                overallProgress.Value = overallProgress.MaxValue;
            }
        }
        finally
        {
            resultChannel.Writer.Complete();
        }
    }

    private async Task<List<string>> CollectResultsAsync(Channel<EncodeResult> resultChannel, int expectedCount, CancellationToken cancellationToken)
    {
        var finalFiles = new List<string>();
        var errors = new List<string>();

        await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken))
        {
            if (result.Success && result.FinalPath != null)
            {
                finalFiles.Add(result.FinalPath);
            }
            else
            {
                errors.Add(result.ErrorMessage ?? "Unknown error");
                _notifier.Error(result.ErrorMessage ?? "Unknown error");
            }
        }

        if (errors.Count > 0)
        {
            _notifier.Warning($"{errors.Count} title(s) failed to encode");
        }

        return finalFiles;
    }

    private async Task<List<string>> EncodeAndRenameAsync(DiscInfo discInfo, List<int> titleIds, Dictionary<int, string> rippedFilesMap, ContentMetadata? metadata, RipOptions options)
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

    private void CleanupTempDirectory(RipOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Temp)) return;

        // Only auto-cleanup directories that were auto-generated
        if (!options.TempWasAutoGenerated)
        {
            _notifier.Muted($"Preserving user-specified temp directory: {options.Temp}");
            return;
        }

        var tempPath = Path.GetFullPath(options.Temp);
        var outputPath = Path.GetFullPath(options.Output);

        if (tempPath == outputPath)
        {
            _notifier.Warning("Skipping temp cleanup because temp directory matches the output directory.");
            return;
        }

        if (!Directory.Exists(tempPath)) return;

        try
        {
            Directory.Delete(tempPath, recursive: true);
            _notifier.Muted($"Cleaned up temporary rip files at {tempPath}");
        }
        catch (Exception ex)
        {
            _notifier.Warning($"Failed to clean up temp directory '{tempPath}': {ex.Message}");
        }
    }
}
