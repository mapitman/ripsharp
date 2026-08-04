using System.Text.RegularExpressions;
using System.Threading.Channels;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace BugZapperLabs.RipSharp.Services;

// Job records for channel communication
public record RipJob(
    int TitleId,
    int Index,
    string RippedFilePath,
    TitleInfo TitleInfo);

public record EncodeJobResult(
    int TitleId,
    bool Success,
    string? FinalPath = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? ErrorLines = null,
    string? Command = null,
    string? EncodeLogPath = null,
    string? RippedFilePath = null);

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
    private readonly IThemeProvider _theme;

    public DiscRipper(IDiscScanner scanner, IEncoderService encoder, IMetadataService metadata, IMakeMkvService makeMkv, IConsoleWriter notifier, IUserPrompt userPrompt, ITvEpisodeTitleProvider episodeTitles, IProgressDisplay progressDisplay, IThemeProvider theme)
    {
        _scanner = scanner;
        _encoder = encoder;
        _metadata = metadata;
        _makeMkv = makeMkv;
        _notifier = notifier;
        _userPrompt = userPrompt;
        _episodeTitles = episodeTitles;
        _progressDisplay = progressDisplay;
        _theme = theme;
    }

    public async Task<DiscProcessingResult> ProcessDiscAsync(RipOptions options, CancellationToken cancellationToken = default)
    {
        PrepareDirectories(options);
        var (discInfo, metadata) = await ScanDiscAndLookupMetadata(options);
        var titleIds = IdentifyTitlesToRip(discInfo, options);

        if (titleIds.Count == 0)
        {
            _notifier.Error("No suitable titles found on disc");
            return new DiscProcessingResult(new List<string>(), new List<TitleOutcome>(), 0);
        }

        if (metadata is null)
        {
            _notifier.Error("ContentMetadata? lookup failed; unable to encode and rename titles.");
            return new DiscProcessingResult(new List<string>(), new List<TitleOutcome>(), 0);
        }

        _notifier.Accent($"Found {titleIds.Count} title(s) to rip: [{string.Join(", ", titleIds)}]");

        if (options.Tv && !options.Season.HasValue)
        {
            AutoDetectSeason(options, discInfo);
        }
        options.Season ??= 1;

        if (options.Tv && !options.EpisodeStart.HasValue)
        {
            AutoDetectEpisodeStart(options);
        }
        options.EpisodeStart ??= 1;

        var titlePlans = await BuildTitlePlansAsync(discInfo, titleIds, metadata, options);

        if (options.Preview)
        {
            var selectedTitleIds = new HashSet<int>(titlePlans.Select(p => p.TitleId));

            while (true)
            {
                var result = _userPrompt.ConfirmRipPlan(titlePlans, metadata, options.Tv, discInfo.DiscName, options.Season, options.EpisodeStart, selectedTitleIds);

                if (result.Action == PreviewAction.Abort)
                {
                    _notifier.Warning("Rip plan declined by user");
                    return new DiscProcessingResult(new List<string>(), new List<TitleOutcome>(), titleIds.Count);
                }

                if (result.Action == PreviewAction.Proceed)
                    break;

                if (result.Action == PreviewAction.EditTitle && result.NewTitle != null)
                {
                    metadata.Title = result.NewTitle;
                    titlePlans = await BuildTitlePlansAsync(discInfo, titleIds, metadata, options);
                    continue;
                }

                if (result.Action == PreviewAction.EditEpisodeStart && result.NewEpisodeStart.HasValue)
                {
                    options.EpisodeStart = result.NewEpisodeStart.Value;
                    titlePlans = await BuildTitlePlansAsync(discInfo, titleIds, metadata, options);
                    continue;
                }

                if (result.Action == PreviewAction.EditFilenames && result.RenamedFiles is { Count: > 0 })
                {
                    titlePlans = titlePlans.Select(p =>
                        result.RenamedFiles.TryGetValue(p.TitleId, out var newName)
                            ? p with { FinalFileName = newName }
                            : p).ToList();
                    continue;
                }

                if (result.Action == PreviewAction.SelectTitles && result.SelectedTitleIds != null)
                {
                    selectedTitleIds = result.SelectedTitleIds;
                    continue;
                }

                // Edit action with no actual change (e.g. user kept default) — re-show preview
            }

            // Apply title selection — filter both the plan list and the ID list
            if (selectedTitleIds.Count < titleIds.Count)
            {
                titlePlans = titlePlans.Where(p => selectedTitleIds.Contains(p.TitleId)).ToList();
                titleIds = titleIds.Where(id => selectedTitleIds.Contains(id)).ToList();
            }
        }

        var (successes, failures) = options.EnableParallelProcessing
            ? await ProcessDiscParallelAsync(discInfo, titleIds, metadata, options, titlePlans, cancellationToken)
            : await ProcessDiscSequentialAsync(discInfo, titleIds, metadata, options, titlePlans, cancellationToken);

        // Interactive failure review with retry support
        while (failures.Count > 0 && options.ErrorMode == ErrorMode.Prompt)
        {
            var retryRequested = _userPrompt.ReviewFailures(failures);
            if (retryRequested.Count == 0)
                break;

            var retryOutcomes = await RetryFailedTitlesAsync(retryRequested, metadata, options);
            var succeededIds = new HashSet<int>(retryOutcomes.Where(o => o.Success).Select(o => o.Plan.TitleId));

            foreach (var outcome in retryOutcomes)
            {
                // Remove old failure entry for this title
                failures.RemoveAll(f => f.Plan.TitleId == outcome.Plan.TitleId);

                if (outcome.Success)
                    successes.Add(outcome);
                else
                    failures.Add(outcome);
            }
        }

        // Organize logs by status before cleanup
        OrganizeLogsByStatus(successes, failures, options);

        // Cleanup temp directory
        if (successes.Count > 0 || failures.Count == 0)
        {
            CleanupTempDirectory(options);
        }
        else if (!string.IsNullOrWhiteSpace(options.Temp) && Directory.Exists(options.Temp) && Directory.EnumerateFiles(options.Temp, "*.mkv").Any())
        {
            _notifier.Error($"No files were successfully encoded; temporary files have been left in: {options.Temp}");
        }

        // Final summary
        var successFiles = successes.Where(o => o.FinalPath != null).Select(o => o.FinalPath!).ToList();
        if (failures.Count > 0)
        {
            _notifier.Error($"{failures.Count} title(s) failed:");
            foreach (var f in failures)
                _notifier.Error($"   {f.Plan.DisplayName} ({f.FailedPhase}): {TruncateError(f.ErrorMessage)}");
        }

        if (successes.Count > 0)
        {
            RenderCompletionSummary(successes, titleIds.Count);
        }

        return new DiscProcessingResult(successFiles, failures, titleIds.Count);
    }

    private static string TruncateError(string? message, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        // Take just the first line
        var firstLine = message.Split('\n', 2)[0].Trim();
        return firstLine.Length <= maxLength ? firstLine : firstLine[..(maxLength - 3)] + "...";
    }

    private void RenderCompletionSummary(List<TitleOutcome> successes, int totalCount)
    {
        var sorted = successes
            .Where(o => o.FinalPath != null)
            .OrderBy(o => o.Plan.FinalFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sorted.Count == 0)
            return;

        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(_theme.AccentColor);
        table.Title($"[bold][{_theme.Colors.Success}]Complete[/] — {sorted.Count}/{totalCount} title{(totalCount != 1 ? "s" : "")} encoded[/]");

        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("Duration").RightAligned());
        table.AddColumn("Filename");

        for (var i = 0; i < sorted.Count; i++)
        {
            var outcome = sorted[i];
            var duration = DurationFormatter.Format(outcome.Plan.DurationSeconds);
            var fileName = Path.GetFileName(outcome.FinalPath!);

            table.AddRow(
                new Markup($"[{_theme.Colors.Muted}]{i + 1}[/]"),
                new Markup($"[{_theme.Colors.Muted}]{Markup.Escape(duration)}[/]"),
                new Markup(Markup.Escape(fileName))
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private async Task<(List<TitleOutcome> Successes, List<TitleOutcome> Failures)> ProcessDiscSequentialAsync(DiscInfo discInfo, List<int> titleIds, ContentMetadata metadata, RipOptions options, IReadOnlyList<TitlePlan> titlePlans, CancellationToken cancellationToken)
    {
        var (rippedFilesMap, ripFailures) = await RipTitlesAsync(discInfo, titleIds, titlePlans, options);
        var (encodeSuccesses, encodeFailures) = await EncodeAndRenameAsync(rippedFilesMap, titlePlans, metadata, options);
        return (encodeSuccesses, ripFailures.Concat(encodeFailures).ToList());
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
                episodeNum = (options.EpisodeStart!.Value - 1) + idx + 1;
                // Fetch episode title early so we can display/name immediately
                episodeTitle = await _episodeTitles.GetEpisodeTitleAsync(metadata.Title, options.Season!.Value, episodeNum.Value, metadata.Year);

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
                var includeSuffix = titleIds.Count > 1;
                versionSuffix = includeSuffix ? $" - title{ordinal:D2}" : null;
                var safeVersionSuffix = string.IsNullOrWhiteSpace(versionSuffix) ? "" : FileNaming.SanitizeFileName(versionSuffix);
                var yearPart = metadata.Year.HasValue ? $" ({metadata.Year.Value})" : "";
                var safeTitle = !string.IsNullOrWhiteSpace(titleName) ? FileNaming.SanitizeFileName(titleName!) : safeSeriesTitle;
                finalFileName = $"{safeTitle}{yearPart}{safeVersionSuffix}.mkv";
                tempOutputPath = Path.Combine(options.Output, $"{safeTitle}{safeVersionSuffix}.mkv");
                displayName = !string.IsNullOrWhiteSpace(titleName)
                    ? titleName!
                    : metadata.Title;
            }

            plans.Add(new TitlePlan(titleId, idx, episodeNum, episodeTitle, tempOutputPath, finalFileName, versionSuffix, displayName, titleInfo?.DurationSeconds ?? 0));
        }

        return plans;
    }

    private async Task<(List<TitleOutcome> Successes, List<TitleOutcome> Failures)> ProcessDiscParallelAsync(DiscInfo discInfo, List<int> titleIds, ContentMetadata metadata, RipOptions options, IReadOnlyList<TitlePlan> titlePlans, CancellationToken cancellationToken)
    {
        var ripChannel = Channel.CreateUnbounded<RipJob>();
        var resultChannel = Channel.CreateUnbounded<EncodeJobResult>();
        var ripFailures = new System.Collections.Concurrent.ConcurrentBag<TitleOutcome>();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var encodeResults = new List<EncodeJobResult>();
        Interlocked.Exchange(ref _encodeProcessedCount, 0);

        try
        {
            await _progressDisplay.ExecuteAsync(async ctx =>
            {
                var ripProgress = ctx.AddTask("Ripping", maxValue: RipProgressScale);
                var encodeProgressTasks = new IProgressTask[options.Concurrency];
                for (int w = 0; w < options.Concurrency; w++)
                {
                    var label = options.Concurrency > 1 ? $"Encoding [{w + 1}]" : "Encoding";
                    encodeProgressTasks[w] = ctx.AddTask(label, maxValue: RipProgressScale);
                    encodeProgressTasks[w].AddMessage("Waiting for rip to complete...");
                }
                var overallProgress = ctx.AddTask("Overall", maxValue: titleIds.Count * 2);
                overallProgress.StartTracking();
                var overallTracker = new OverallProgressTracker(overallProgress);

                var ripTask = Task.Run(() => RipProducerAsync(ripChannel, ripFailures, discInfo, titleIds, titlePlans, options, ripProgress, overallTracker, cts.Token));

                var encodeWorkers = new Task[options.Concurrency];
                for (int w = 0; w < options.Concurrency; w++)
                {
                    var workerProgress = encodeProgressTasks[w];
                    encodeWorkers[w] = Task.Run(() => EncodeWorkerAsync(ripChannel, resultChannel, titlePlans, metadata, options, workerProgress, overallTracker, cts.Token));
                }

                var collectTask = CollectResultsAsync(resultChannel, cts.Token);

                await ripTask;
                await Task.WhenAll(encodeWorkers);
                resultChannel.Writer.Complete();

                ripProgress.StopTask();
                foreach (var ep in encodeProgressTasks) ep.StopTask();
                overallProgress.StopTask();

                encodeResults = await collectTask;
            });

            return BuildOutcomes(titlePlans, encodeResults, ripFailures);
        }
        catch (OperationCanceledException)
        {
            throw;
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
                var emoji = discInfo.DetectedContentType.Value ? $"{_theme.Emojis.Tv} " : $"{_theme.Emojis.Movie} ";
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

    private static readonly Regex SeasonPattern = new(@"[Ss](?:eason[\s_]*)?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void AutoDetectSeason(RipOptions options, DiscInfo discInfo)
    {
        var dirName = Path.GetFileName(Path.GetFullPath(options.Output));
        if (!string.IsNullOrWhiteSpace(dirName))
        {
            var match = SeasonPattern.Match(dirName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var season) && season > 0)
            {
                options.Season = season;
                _notifier.Info($"Detected season {season} from output directory");
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(discInfo.DiscName))
        {
            var match = SeasonPattern.Match(discInfo.DiscName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var season) && season > 0)
            {
                options.Season = season;
                _notifier.Info($"Detected season {season} from disc name '{discInfo.DiscName}'");
            }
        }
    }

    private void AutoDetectEpisodeStart(RipOptions options)
    {
        if (!Directory.Exists(options.Output))
            return;

        var pattern = new Regex($@"S{options.Season:00}E(\d{{2,}})", RegexOptions.IgnoreCase);
        int maxEpisode = 0;

        foreach (var file in Directory.EnumerateFiles(options.Output, "*.mkv"))
        {
            var match = pattern.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var ep) && ep > maxEpisode)
            {
                maxEpisode = ep;
            }
        }

        if (maxEpisode > 0)
        {
            options.EpisodeStart = maxEpisode + 1;
            _notifier.Info($"Detected existing episodes up to E{maxEpisode:00}, starting at E{options.EpisodeStart:00}");
        }
    }

    private async Task<(Dictionary<int, string> RippedFiles, List<TitleOutcome> RipFailures)> RipTitlesAsync(DiscInfo discInfo, List<int> titleIds, IReadOnlyList<TitlePlan> titlePlans, RipOptions options)
    {
        var rippedFilesMap = new Dictionary<int, string>();
        var ripFailures = new List<TitleOutcome>();
        var totalTitles = titleIds.Count;
        var preExistingRips = new Queue<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderBy(File.GetCreationTime));
        for (int idx = 0; idx < titleIds.Count; idx++)
        {
            var titleId = titleIds[idx];
            var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);
            var titleName = titleInfo?.Name;
            var plan = titlePlans[idx];

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

            ProcessResult? ripFailureResult = null;
            string? rawLogPathCapture = null;

            await _progressDisplay.ExecuteAsync(async ctx =>
            {
                var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
                var maxValue = expectedBytes > 0 ? expectedBytes : 100;
                var task = ctx.AddTask($"[{_theme.Colors.Success}]Title {idx + 1} ({idx + 1}/{totalTitles})[/]", maxValue);
                bool ripDone = false;

                var pollTask = Task.Run(async () =>
                {
                    double lastSizeLocal = 0;
                    string? currentMkv = null;
                    while (!ripDone)
                    {
                        try
                        {
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
                rawLogPathCapture = rawLogPath;
                var handler = new MakeMkvOutputHandler(expectedBytes, idx, totalTitles, task, progressLogPath, rawLogPath, _notifier, _theme);
                var ripResult = await _makeMkv.RipTitleAsync(options.Disc, titleId, options.Temp!,
                    onOutput: handler.HandleLine,
                    onError: errLine =>
                    {
                        _notifier.Error(errLine);
                        handler.HandleLine(errLine);
                    });
                ripDone = true;
                try { await pollTask; } catch { }

                if (!ripResult.Success)
                {
                    task.Description = $"[{_theme.Colors.Error}]Failed: Title {titleId}[/]";
                    task.StopTask();
                    ripFailureResult = ripResult;
                    return;
                }
                if (handler.LastBytesProcessed < maxValue)
                {
                    task.Value = maxValue;
                }
                task.StopTask();
            });

            if (ripFailureResult != null)
            {
                ripFailures.Add(new TitleOutcome(
                    plan, Success: false, FailedPhase: ProcessingPhase.Rip,
                    ErrorMessage: ripFailureResult.ErrorSummary,
                    ErrorLines: ripFailureResult.ErrorLines,
                    Command: ripFailureResult.Command,
                    RipLogPath: rawLogPathCapture));
                continue;
            }

            var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
            if (newFiles.Count > 0)
            {
                var ripped = newFiles.OrderByDescending(File.GetCreationTime).First();
                rippedFilesMap[titleId] = ripped;
            }
        }
        return (rippedFilesMap, ripFailures);
    }

    private async Task RipProducerAsync(Channel<RipJob> ripChannel, System.Collections.Concurrent.ConcurrentBag<TitleOutcome> ripFailures, DiscInfo discInfo, List<int> titleIds, IReadOnlyList<TitlePlan> plans, RipOptions options, IProgressTask ripProgress, OverallProgressTracker overallTracker, CancellationToken cancellationToken)
    {
        try
        {
            var totalTitles = titleIds.Count;
            var ripStartTime = DateTime.UtcNow;
            var rippedCount = 0;
            var preExistingRips = new Queue<string>(Directory.Exists(options.Temp!) ? Directory.EnumerateFiles(options.Temp!, "*.mkv").OrderBy(File.GetCreationTime) : Enumerable.Empty<string>());

            for (int idx = 0; idx < titleIds.Count; idx++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var titleId = titleIds[idx];
                var titleInfo = discInfo.Titles.FirstOrDefault(t => t.Id == titleId);

                if (titleInfo == null)
                {
                    var plan = plans[idx];
                    ripFailures.Add(new TitleOutcome(
                        plan, Success: false, FailedPhase: ProcessingPhase.Rip,
                        ErrorMessage: $"Title {titleId} not found in disc info"));
                    ripProgress.AddMessage($"Title {titleId} not found in disc info, skipping");
                    overallTracker.MarkRipComplete();
                    overallTracker.MarkEncodeComplete();
                    continue;
                }

                var planForTitle = plans[idx];

                // Check for pre-existing rips
                if (preExistingRips.Count > 0)
                {
                    var reused = preExistingRips.Dequeue();
                    var msg = $"Using existing ripped file for title {idx + 1} of {totalTitles}: {planForTitle.DisplayName} (Title ID: {titleId}) -> {Path.GetFileName(reused)}";
                    ripProgress.AddMessage(msg);

                    await ripChannel.Writer.WriteAsync(new RipJob(titleId, idx, reused, titleInfo), cancellationToken);
                    ripProgress.Description = $"{planForTitle.DisplayName} [100%]";
                    ripProgress.Value += RipProgressScale;
                    rippedCount++;
                    overallTracker.MarkRipComplete();
                    continue;
                }

                // Perform actual rip with live progress contribution
                var (rippedPath, ripResult, rawLogPath) = await PerformSingleRipAsync(titleId, idx, titleInfo, planForTitle, totalTitles, options, ripProgress);

                if (!string.IsNullOrEmpty(rippedPath))
                {
                    await ripChannel.Writer.WriteAsync(new RipJob(titleId, idx, rippedPath, titleInfo), cancellationToken);
                    rippedCount++;
                    overallTracker.MarkRipComplete();
                }
                else
                {
                    ripFailures.Add(new TitleOutcome(
                        planForTitle, Success: false, FailedPhase: ProcessingPhase.Rip,
                        ErrorMessage: ripResult?.ErrorSummary ?? "Rip failed with no details",
                        ErrorLines: ripResult?.ErrorLines,
                        Command: ripResult?.Command,
                        RipLogPath: rawLogPath));
                    ripProgress.AddMessage($"Failed to rip title {titleId}, skipping");
                    overallTracker.MarkRipComplete();
                    overallTracker.MarkEncodeComplete();
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var elapsed = DateTime.UtcNow - ripStartTime;
                var summary = BuildRipCompletionMessage(rippedCount, totalTitles, elapsed);
                ripProgress.ClearMessages();
                ripProgress.AddMessage(summary);
                ripProgress.StopTask();
            }
        }
        finally
        {
            ripChannel.Writer.Complete();
        }
    }

    private async Task<(string? RippedPath, ProcessResult? FailureResult, string RawLogPath)> PerformSingleRipAsync(int titleId, int idx, TitleInfo? titleInfo, TitlePlan plan, int totalTitles, RipOptions options, IProgressTask ripProgress)
    {
        // Reset ripProgress for this track (show 0-100% per track)
        ripProgress.Value = 0;
        ripProgress.ClearMessages(); // Clear messages from previous track

        var msg = $"Ripping title {idx + 1} of {totalTitles}: {plan.DisplayName} (Title ID: {titleId}) [{DurationFormatter.Format(titleInfo?.DurationSeconds ?? 0)}]";
        ripProgress.AddMessage(msg);

        var existingFiles = new HashSet<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv"));
        var progressLogPath = Path.Combine(options.Temp!, $"progress_title_{titleId:D2}.log");
        if (File.Exists(progressLogPath)) File.Delete(progressLogPath);

        string? rippedPath = null;
        ProcessResult? failureResult = null;

        var expectedBytes = titleInfo?.ReportedSizeBytes ?? 0;
        var durationSeconds = titleInfo?.DurationSeconds ?? 0;
        var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{titleId:D2}.log");
        var handler = new MakeMkvOutputHandler(expectedBytes, idx, totalTitles, null, progressLogPath, rawLogPath, _notifier, _theme);

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

                    if (currentMkv == null)
                    {
                        currentMkv = Directory
                            .EnumerateFiles(options.Temp!, "*.mkv")
                            .FirstOrDefault(f => !existingFiles.Contains(f));
                    }

                    var fraction = Math.Clamp(handler.LastProgressFraction, 0, 1);

                    if (fraction == 0 && expectedBytes > 0 && handler.LastBytesProcessed > 0)
                    {
                        fraction = Math.Clamp(handler.LastBytesProcessed / Math.Max(1.0, expectedBytes), 0, 1);
                    }

                    if (fraction == 0 && durationSeconds > 0)
                    {
                        var elapsedSecs = DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds;
                        var denom = Math.Max(10.0, durationSeconds * 1.2);
                        fraction = Math.Clamp(elapsedSecs / denom, 0, 1);
                    }

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

                    if (fraction == 0 && expectedBytes == 0 && currentMkv != null)
                    {
                        try
                        {
                            var size = new FileInfo(currentMkv).Length;
                            if (size > observedMaxBytes) observedMaxBytes = size;
                            var candidate = observedMaxBytes > 0 ? size / observedMaxBytes : 0;
                            displayedFraction = Math.Max(displayedFraction, Math.Clamp(candidate * 0.8, 0, 0.99));
                            fraction = displayedFraction;
                        }
                        catch { }
                    }

                    if (fraction == 0 && DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds > 3)
                    {
                        var elapsedSecs = DateTime.UtcNow.Subtract(ripStartTime).TotalSeconds;
                        var minimalFraction = Math.Min(0.1, elapsedSecs / 60.0);
                        displayedFraction = Math.Max(displayedFraction, minimalFraction);
                        fraction = displayedFraction;
                    }

                    var fractionalProgress = (long)Math.Round(fraction * RipProgressScale);
                    ripProgress.Value = fractionalProgress;

                }
                catch (Exception ex)
                {
                    _notifier.Error($"Rip progress polling error: {ex.Message}");
                }
                await Task.Delay(500);
            }
        });

        var ripResult = await _makeMkv.RipTitleAsync(options.Disc, titleId, options.Temp!,
            onOutput: handler.HandleLine,
            onError: errLine =>
            {
                ripProgress.AddMessage($"ERROR: {errLine}");
                handler.HandleLine(errLine);
            });
        ripDone = true;
        try
        {
            await pollTask;
        }
        catch (Exception ex)
        {
            ripProgress.AddMessage($"ERROR: monitoring rip progress: {ex.Message}");
        }

        if (ripResult.Success)
        {
            var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
            if (newFiles.Count > 0)
            {
                rippedPath = newFiles.OrderByDescending(File.GetCreationTime).First();
            }
        }
        else
        {
            ripProgress.ReportFailure(ripResult);
            failureResult = ripResult;
        }

        ripProgress.Value = RipProgressScale;
        return (rippedPath, failureResult, rawLogPath);
    }

    private int _encodeProcessedCount;

    private async Task EncodeWorkerAsync(Channel<RipJob> ripChannel, Channel<EncodeJobResult> resultChannel, IReadOnlyList<TitlePlan> titlePlans, ContentMetadata metadata, RipOptions options, IProgressTask encodeProgress, OverallProgressTracker overallTracker, CancellationToken cancellationToken)
    {
        var totalTitles = titlePlans.Count;
        var planLookup = titlePlans.ToDictionary(p => p.TitleId);

        await foreach (var ripJob in ripChannel.Reader.ReadAllAsync(cancellationToken))
        {
            var processedCount = Interlocked.Increment(ref _encodeProcessedCount);

            if (!planLookup.TryGetValue(ripJob.TitleId, out var plan))
            {
                await resultChannel.Writer.WriteAsync(new EncodeJobResult(ripJob.TitleId, false, ErrorMessage: $"Missing plan for title {ripJob.TitleId}"), cancellationToken);
                overallTracker.MarkEncodeComplete();
                continue;
            }

            var outputPath = plan.TempOutputPath;
            var versionSuffix = plan.VersionSuffix;
            var episodeNum = plan.EpisodeNum;

            encodeProgress.ClearMessages();
            var encMsg = $"Encoding ({processedCount}/{totalTitles}): {plan.FinalFileName}";
            encodeProgress.AddMessage(encMsg);

            if (File.Exists(outputPath)) File.Delete(outputPath);
            encodeProgress.Value = 0;

            var outcome = await _encoder.EncodeAsync(
                ripJob.RippedFilePath,
                outputPath,
                includeEnglishSubtitles: true,
                ordinal: processedCount,
                total: totalTitles,
                progressTask: encodeProgress,
                logDirectory: options.Temp);

            if (outcome.Success)
            {
                var finalPath = FileNaming.RenameFile(
                    outputPath, metadata, episodeNum,
                    options.Season!.Value, versionSuffix, plan.EpisodeTitle);

                await resultChannel.Writer.WriteAsync(new EncodeJobResult(
                    ripJob.TitleId,
                    true,
                    finalPath,
                    EncodeLogPath: outcome.LogPath,
                    RippedFilePath: ripJob.RippedFilePath), cancellationToken);
                encodeProgress.Value = RipProgressScale;
            }
            else
            {
                encodeProgress.ReportFailure(outcome);
                await resultChannel.Writer.WriteAsync(new EncodeJobResult(
                    ripJob.TitleId,
                    false,
                    ErrorMessage: outcome.ErrorSummary,
                    ErrorLines: outcome.ErrorLines,
                    Command: outcome.Command,
                    EncodeLogPath: outcome.LogPath,
                    RippedFilePath: ripJob.RippedFilePath), cancellationToken);
            }
            overallTracker.MarkEncodeComplete();
        }
    }

    private sealed class OverallProgressTracker
    {
        private readonly IProgressTask _overallTask;
        private readonly Lock _lock = new();
        private int _completedRips;
        private int _completedEncodes;

        public OverallProgressTracker(IProgressTask overallTask)
        {
            _overallTask = overallTask;
        }

        public void MarkRipComplete()
        {
            lock (_lock)
            {
                _completedRips++;
                UpdateValue();
            }
        }

        public void MarkEncodeComplete()
        {
            lock (_lock)
            {
                _completedEncodes++;
                UpdateValue();
            }
        }

        private void UpdateValue()
        {
            _overallTask.Value = _completedRips + _completedEncodes;
        }
    }

    private static string BuildRipCompletionMessage(int rippedCount, int totalTitles, TimeSpan elapsed)
    {
        var durationText = DurationFormatter.Format((int)Math.Max(0, elapsed.TotalSeconds));
        return $"Ripping complete: {rippedCount}/{totalTitles} tracks in {durationText}.";
    }

    private async Task<List<EncodeJobResult>> CollectResultsAsync(Channel<EncodeJobResult> resultChannel, CancellationToken cancellationToken)
    {
        var results = new List<EncodeJobResult>();

        await foreach (var result in resultChannel.Reader.ReadAllAsync(cancellationToken))
        {
            results.Add(result);
        }

        return results;
    }

    private static (List<TitleOutcome> Successes, List<TitleOutcome> Failures) BuildOutcomes(
        IReadOnlyList<TitlePlan> plans,
        List<EncodeJobResult> encodeResults,
        System.Collections.Concurrent.ConcurrentBag<TitleOutcome> ripFailures)
    {
        var planLookup = plans.ToDictionary(p => p.TitleId);
        var successes = new List<TitleOutcome>();
        var failures = new List<TitleOutcome>(ripFailures);

        foreach (var er in encodeResults)
        {
            if (!planLookup.TryGetValue(er.TitleId, out var plan)) continue;
            if (er.Success)
            {
                successes.Add(new TitleOutcome(
                    plan, Success: true,
                    FinalPath: er.FinalPath,
                    RippedFilePath: er.RippedFilePath,
                    EncodeLogPath: er.EncodeLogPath));
            }
            else
            {
                failures.Add(new TitleOutcome(
                    plan, Success: false, FailedPhase: ProcessingPhase.Encode,
                    ErrorMessage: er.ErrorMessage,
                    ErrorLines: er.ErrorLines,
                    Command: er.Command,
                    EncodeLogPath: er.EncodeLogPath,
                    RippedFilePath: er.RippedFilePath));
            }
        }

        return (successes, failures);
    }

    private async Task<(List<TitleOutcome> Successes, List<TitleOutcome> Failures)> EncodeAndRenameAsync(Dictionary<int, string> rippedFilesMap, IReadOnlyList<TitlePlan> titlePlans, ContentMetadata metadata, RipOptions options)
    {
        var successes = new List<TitleOutcome>();
        var failures = new List<TitleOutcome>();
        for (var idx = 0; idx < titlePlans.Count; idx++)
        {
            var plan = titlePlans[idx];
            if (!rippedFilesMap.TryGetValue(plan.TitleId, out var src))
            {
                // Title was never ripped (failure already recorded in rip phase)
                continue;
            }

            if (File.Exists(plan.TempOutputPath)) File.Delete(plan.TempOutputPath);

            var outcome = await _encoder.EncodeAsync(src, plan.TempOutputPath, includeEnglishSubtitles: true, ordinal: idx + 1, total: titlePlans.Count, logDirectory: options.Temp);
            if (outcome.Success)
            {
                var finalPath = FileNaming.RenameFile(
                    plan.TempOutputPath, metadata, plan.EpisodeNum,
                    options.Season!.Value, plan.VersionSuffix, plan.EpisodeTitle);
                successes.Add(new TitleOutcome(
                    plan, Success: true,
                    FinalPath: finalPath,
                    RippedFilePath: src,
                    EncodeLogPath: outcome.LogPath));
            }
            else
            {
                failures.Add(new TitleOutcome(
                    plan, Success: false, FailedPhase: ProcessingPhase.Encode,
                    ErrorMessage: outcome.ErrorSummary,
                    ErrorLines: outcome.ErrorLines,
                    Command: outcome.Command,
                    EncodeLogPath: outcome.LogPath,
                    RippedFilePath: src));
            }
        }
        return (successes, failures);
    }

    private void OrganizeLogsByStatus(List<TitleOutcome> successes, List<TitleOutcome> failures, RipOptions options)
    {
        if (failures.Count == 0) return; // No failures = no logs to preserve

        var logsBase = Path.Combine(options.Output, "logs");
        var successLogsDir = Path.Combine(logsBase, "success");
        var failedLogsDir = Path.Combine(logsBase, "failed");

        void CopyLog(string? logPath, string destDir)
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath)) return;
            try
            {
                Directory.CreateDirectory(destDir);
                File.Copy(logPath, Path.Combine(destDir, Path.GetFileName(logPath)), overwrite: true);
            }
            catch (Exception ex)
            {
                _notifier.Warning($"Could not preserve log '{logPath}': {ex.Message}");
            }
        }

        foreach (var s in successes)
        {
            CopyLog(s.RipLogPath, successLogsDir);
            CopyLog(s.EncodeLogPath, successLogsDir);
        }

        foreach (var f in failures)
        {
            CopyLog(f.RipLogPath, failedLogsDir);
            CopyLog(f.EncodeLogPath, failedLogsDir);
        }

        // Also copy makemkv/progress logs from temp that are associated with outcomes by title ID
        if (!string.IsNullOrWhiteSpace(options.Temp) && Directory.Exists(options.Temp))
        {
            var failedTitleIds = new HashSet<int>(failures.Select(f => f.Plan.TitleId));
            var successTitleIds = new HashSet<int>(successes.Select(s => s.Plan.TitleId));

            foreach (var logFile in Directory.GetFiles(options.Temp, "*.log"))
            {
                var fileName = Path.GetFileName(logFile);
                var match = Regex.Match(fileName, @"title_(\d+)");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id)) continue;

                var destDir = failedTitleIds.Contains(id) ? failedLogsDir : successLogsDir;
                CopyLog(logFile, destDir);
            }
        }
    }

    private async Task<List<TitleOutcome>> RetryFailedTitlesAsync(
        List<TitleOutcome> toRetry,
        ContentMetadata metadata,
        RipOptions options)
    {
        var results = new List<TitleOutcome>();

        foreach (var failure in toRetry)
        {
            var plan = failure.Plan;
            _notifier.Info($"Retrying: {plan.DisplayName}");

            TitleOutcome? outcome = null;
            await _progressDisplay.ExecuteAsync(async ctx =>
            {
                var encodeTask = ctx.AddTask($"Encoding: {plan.FinalFileName}", maxValue: RipProgressScale);

                if (failure.FailedPhase == ProcessingPhase.Rip)
                {
                    encodeTask.AddMessage("Waiting for rip...");
                    var rippedPath = await RipSingleTitleForRetryAsync(plan, options);
                    if (rippedPath == null)
                    {
                        outcome = failure with { ErrorMessage = "Retry rip failed" };
                        encodeTask.AddMessage("Rip failed");
                        encodeTask.FailTask();
                        return;
                    }
                    encodeTask.ClearMessages();
                    encodeTask.StartTracking();
                    outcome = await EncodeSingleTitleAsync(rippedPath, plan, metadata, options, encodeTask);
                }
                else
                {
                    if (string.IsNullOrEmpty(failure.RippedFilePath) || !File.Exists(failure.RippedFilePath))
                    {
                        outcome = failure with { ErrorMessage = "Ripped file no longer available for re-encode" };
                        encodeTask.AddMessage("Ripped file not available");
                        encodeTask.FailTask();
                        return;
                    }
                    encodeTask.StartTracking();
                    outcome = await EncodeSingleTitleAsync(failure.RippedFilePath, plan, metadata, options, encodeTask);
                }

                if (outcome!.Success)
                    encodeTask.StopTask();
            });

            results.Add(outcome!);
        }
        return results;
    }

    private async Task<string?> RipSingleTitleForRetryAsync(TitlePlan plan, RipOptions options)
    {
        var existingFiles = new HashSet<string>(Directory.EnumerateFiles(options.Temp!, "*.mkv"));
        var rawLogPath = Path.Combine(options.Temp!, $"makemkv_title_{plan.TitleId:D2}.log");
        var progressLogPath = Path.Combine(options.Temp!, $"progress_title_{plan.TitleId:D2}.log");
        if (File.Exists(progressLogPath)) File.Delete(progressLogPath);

        var handler = new MakeMkvOutputHandler(0, plan.Index, 1, null, progressLogPath, rawLogPath, _notifier, _theme);
        var ripResult = await _makeMkv.RipTitleAsync(options.Disc, plan.TitleId, options.Temp!,
            onOutput: handler.HandleLine,
            onError: errLine => handler.HandleLine(errLine));

        if (!ripResult.Success)
        {
            _notifier.Error($"Retry rip failed for {plan.DisplayName}: {ripResult.ErrorSummary}");
            return null;
        }

        var newFiles = Directory.EnumerateFiles(options.Temp!, "*.mkv").Where(f => !existingFiles.Contains(f)).ToList();
        return newFiles.Count > 0 ? newFiles.OrderByDescending(File.GetCreationTime).First() : null;
    }

    private async Task<TitleOutcome> EncodeSingleTitleAsync(string rippedFilePath, TitlePlan plan, ContentMetadata metadata, RipOptions options, IProgressTask? progressTask = null)
    {
        if (File.Exists(plan.TempOutputPath)) File.Delete(plan.TempOutputPath);

        var outcome = await _encoder.EncodeAsync(
            rippedFilePath, plan.TempOutputPath,
            includeEnglishSubtitles: true,
            ordinal: plan.Index + 1,
            total: 1,
            progressTask: progressTask,
            logDirectory: options.Temp);

        if (outcome.Success)
        {
            var finalPath = FileNaming.RenameFile(
                plan.TempOutputPath, metadata, plan.EpisodeNum,
                options.Season!.Value, plan.VersionSuffix, plan.EpisodeTitle);
            return new TitleOutcome(plan, Success: true, FinalPath: finalPath,
                RippedFilePath: rippedFilePath, EncodeLogPath: outcome.LogPath);
        }

        progressTask?.ReportFailure(outcome);
        return new TitleOutcome(plan, Success: false, FailedPhase: ProcessingPhase.Encode,
            ErrorMessage: outcome.ErrorSummary,
            ErrorLines: outcome.ErrorLines,
            Command: outcome.Command,
            EncodeLogPath: outcome.LogPath,
            RippedFilePath: rippedFilePath);
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
