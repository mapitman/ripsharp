using System.IO.Compression;

using BugZapperLabs.RipSharp.Models;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace BugZapperLabs.RipSharp.Utilities;

/// <summary>
/// Prompts users for input using Spectre.Console for rich terminal interactions.
/// </summary>
public class ConsoleUserPrompt : IUserPrompt
{
    private readonly IConsoleWriter _notifier;
    private readonly IThemeProvider _theme;

    public ConsoleUserPrompt(IConsoleWriter notifier, IThemeProvider theme)
    {
        _notifier = notifier;
        _theme = theme;
    }

    public bool PromptForContentType(string? detectionHint = null)
    {
        var message = "Unable to confidently detect disc type";
        if (!string.IsNullOrEmpty(detectionHint))
        {
            message += $" ({detectionHint})";
        }

        _notifier.Warning(message);

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What type of content is this?")
                .AddChoices(new[] { "Movie", "TV Series" })
        );

        var isTv = selection == "TV Series";
        var mode = isTv ? "TV series" : "movie";
        _notifier.Info($"Using {mode} mode");

        return isTv;
    }

    public PreviewResult ConfirmRipPlan(IReadOnlyList<TitlePlan> plans, ContentMetadata metadata, bool isTv, string? discName = null, int? season = null, int? episodeStart = null, IReadOnlySet<int>? selectedTitleIds = null)
    {
        RenderPreview(plans, metadata, isTv, discName, season, episodeStart, selectedTitleIds);

        var choices = new List<string> { "Proceed", "Edit title" };
        if (isTv)
            choices.Add("Edit episode start");
        choices.Add("Edit output filenames");
        choices.Add("Select titles to include");
        choices.Add("Abort");

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(choices));

        return selection switch
        {
            "Proceed" => PreviewResult.Proceed(),
            "Abort" => PreviewResult.Abort(),
            "Edit title" => PromptEditTitle(metadata),
            "Edit episode start" => PromptEditEpisodeStart(episodeStart ?? 1),
            "Edit output filenames" => PromptEditFilenames(plans),
            "Select titles to include" => PromptSelectTitles(plans, selectedTitleIds),
            _ => PreviewResult.Abort()
        };
    }

    private void RenderPreview(IReadOnlyList<TitlePlan> plans, ContentMetadata metadata, bool isTv, string? discName, int? season, int? episodeStart, IReadOnlySet<int>? selectedTitleIds)
    {
        AnsiConsole.Clear();

        var metaGrid = new Grid();
        metaGrid.AddColumn(new GridColumn().PadRight(2));
        metaGrid.AddColumn();

        if (!string.IsNullOrWhiteSpace(discName))
            metaGrid.AddRow("[dim]Disc:[/]", Markup.Escape(discName));

        if (!string.IsNullOrWhiteSpace(metadata.Provider))
        {
            var searchInfo = metadata.SearchTitle != null && metadata.SearchTitle != metadata.DiscTitle
                ? $" (matched on [yellow]{Markup.Escape(metadata.SearchTitle)}[/])"
                : "";
            metaGrid.AddRow("[dim]Lookup:[/]", $"{Markup.Escape(metadata.Provider)} → [bold]{Markup.Escape(metadata.Title)}[/]{(metadata.Year.HasValue ? $" ({metadata.Year.Value})" : "")}{searchInfo}");
        }
        else
        {
            metaGrid.AddRow("[dim]Lookup:[/]", "[yellow]No provider match — using disc title as fallback[/]");
        }

        if (isTv)
        {
            var selectedPlans = selectedTitleIds != null
                ? plans.Where(p => selectedTitleIds.Contains(p.TitleId)).ToList()
                : plans.ToList();
            metaGrid.AddRow("[dim]Season:[/]", $"{season ?? 1}");
            metaGrid.AddRow("[dim]Episodes:[/]", $"E{episodeStart ?? 1:00}–E{(episodeStart ?? 1) + selectedPlans.Count - 1:00} ({selectedPlans.Count} title{(selectedPlans.Count != 1 ? "s" : "")})");
        }

        AnsiConsole.Write(metaGrid);
        AnsiConsole.WriteLine();

        var hasPartialSelection = selectedTitleIds != null && selectedTitleIds.Count < plans.Count;

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(_theme.AccentColor);
        var selectedCount = selectedTitleIds?.Count ?? plans.Count;
        var titleSuffix = hasPartialSelection ? $" [{selectedCount}/{plans.Count} selected]" : "";
        table.Title($"[bold]{Markup.Escape(metadata.Title)}[/]{(metadata.Year.HasValue ? $" ({metadata.Year.Value})" : "")} — {(isTv ? "TV Series" : "Movie")}{titleSuffix}");

        table.AddColumn(new TableColumn(" ").Centered());
        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn(new TableColumn("Title ID").Centered());
        table.AddColumn(new TableColumn("Duration").RightAligned());
        if (isTv)
        {
            table.AddColumn("Episode");
        }
        table.AddColumn("Output Filename");

        foreach (var plan in plans)
        {
            var isSelected = selectedTitleIds == null || selectedTitleIds.Contains(plan.TitleId);
            var checkmark = isSelected ? $"[{_theme.Colors.Success}]✓[/]" : $"[{_theme.Colors.Muted}]✗[/]";
            var duration = DurationFormatter.Format(plan.DurationSeconds);
            string Dim(string text) => isSelected ? text : $"[{_theme.Colors.Muted}]{Markup.Escape(text)}[/]";

            var row = new List<string>
            {
                checkmark,
                Dim((plan.Index + 1).ToString()),
                Dim(plan.TitleId.ToString()),
                Dim(duration)
            };
            if (isTv)
            {
                row.Add(Dim(plan.EpisodeNum.HasValue ? $"E{plan.EpisodeNum.Value:00}" : "-"));
            }
            row.Add(isSelected ? Markup.Escape(plan.FinalFileName) : $"[{_theme.Colors.Muted}]{Markup.Escape(plan.FinalFileName)}[/]");

            table.AddRow(row.Select(r => new Markup(r)).ToArray<IRenderable>());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static PreviewResult PromptEditTitle(ContentMetadata metadata)
    {
        var newTitle = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter new title:")
                .DefaultValue(metadata.Title));

        if (newTitle == metadata.Title)
            return new PreviewResult { Action = PreviewAction.EditTitle };

        return new PreviewResult { Action = PreviewAction.EditTitle, NewTitle = newTitle };
    }

    private static PreviewResult PromptEditEpisodeStart(int currentStart)
    {
        var newStart = AnsiConsole.Prompt(
            new TextPrompt<int>("Enter episode start number:")
                .DefaultValue(currentStart)
                .Validate(n => n > 0 ? ValidationResult.Success() : ValidationResult.Error("Must be a positive number")));

        return new PreviewResult { Action = PreviewAction.EditEpisodeStart, NewEpisodeStart = newStart };
    }

    private static PreviewResult PromptSelectTitles(IReadOnlyList<TitlePlan> plans, IReadOnlySet<int>? currentSelection)
    {
        static string BuildLabel(TitlePlan plan) =>
            Markup.Escape($"{plan.Index + 1}. {plan.FinalFileName} [{DurationFormatter.Format(plan.DurationSeconds)}]");

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select titles to include:")
            .PageSize(Math.Min(plans.Count + 3, 20))
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]");

        foreach (var plan in plans)
        {
            var label = BuildLabel(plan);
            prompt.AddChoice(label);
            if (currentSelection == null || currentSelection.Contains(plan.TitleId))
                prompt.Select(label);
        }

        var selected = AnsiConsole.Prompt(prompt);

        // Map selected labels back to title IDs by position
        var selectedIds = new HashSet<int>();
        for (int i = 0; i < plans.Count; i++)
        {
            var label = BuildLabel(plans[i]);
            if (selected.Contains(label))
                selectedIds.Add(plans[i].TitleId);
        }

        return new PreviewResult { Action = PreviewAction.SelectTitles, SelectedTitleIds = selectedIds };
    }

    private static PreviewResult PromptEditFilenames(IReadOnlyList<TitlePlan> plans)
    {
        var choices = plans.Select((p, i) => $"{i + 1}. {p.FinalFileName}").ToList();
        choices.Add("Done");

        var renamed = new Dictionary<int, string>();

        while (true)
        {
            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a file to rename (or Done):")
                    .PageSize(Math.Min(plans.Count + 2, 20))
                    .AddChoices(choices));

            if (selection == "Done")
                break;

            var idx = choices.IndexOf(selection);
            if (idx < 0 || idx >= plans.Count)
                break;

            var plan = plans[idx];
            var currentName = renamed.TryGetValue(plan.TitleId, out var alreadyRenamed)
                ? alreadyRenamed
                : plan.FinalFileName;

            var newName = AnsiConsole.Prompt(
                new TextPrompt<string>($"New filename for #{idx + 1}:")
                    .DefaultValue(currentName));

            if (!newName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                newName += ".mkv";

            renamed[plan.TitleId] = newName;
            choices[idx] = $"{idx + 1}. {newName} [yellow](edited)[/]";
        }

        return new PreviewResult { Action = PreviewAction.EditFilenames, RenamedFiles = renamed.Count > 0 ? renamed : null };
    }

    public List<TitleOutcome> ReviewFailures(IReadOnlyList<TitleOutcome> failures)
    {
        // Summary table
        AnsiConsole.WriteLine();
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(_theme.ErrorColor);
        table.Title($"[bold {_theme.Colors.Error}]{failures.Count} Title(s) Failed[/]");
        table.AddColumn(new TableColumn("#").Centered());
        table.AddColumn("Title");
        table.AddColumn("Phase");
        table.AddColumn("Error");

        for (int i = 0; i < failures.Count; i++)
        {
            var f = failures[i];
            var errorPreview = f.ErrorMessage?.Split('\n', 2)[0].Trim() ?? "Unknown error";
            if (errorPreview.Length > 60)
                errorPreview = errorPreview[..57] + "...";
            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(f.Plan.DisplayName),
                f.FailedPhase?.ToString() ?? "Unknown",
                Markup.Escape(errorPreview));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Selection loop
        var retryList = new List<TitleOutcome>();
        const string saveLogs = "Save Logs (.zip)";
        const string done = "Done";
        var choices = failures
            .Select((f, i) => $"{f.Plan.DisplayName} - {f.FailedPhase} failed")
            .Append(saveLogs)
            .Append(done)
            .ToList();

        while (true)
        {
            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a failure to inspect, or Done:")
                    .PageSize(Math.Min(failures.Count + 3, 20))
                    .AddChoices(choices));

            if (selection == done)
                break;

            if (selection == saveLogs)
            {
                SaveLogsAsZip(failures);
                continue;
            }

            var idx = choices.IndexOf(selection);
            if (idx < 0 || idx >= failures.Count)
                break;

            var failure = failures[idx];
            RenderFailureDetail(failure);

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Action:")
                    .AddChoices("Retry", "Skip"));

            if (action == "Retry")
            {
                retryList.Add(failure);
                choices[idx] = $"{failure.Plan.DisplayName} - [yellow]retry queued[/]";
            }
            else
            {
                choices[idx] = $"{failure.Plan.DisplayName} - [dim]skipped[/]";
            }
        }

        return retryList;
    }

    private void SaveLogsAsZip(IReadOnlyList<TitleOutcome> failures)
    {
        var logPaths = failures
            .SelectMany(f => new[] { f.RipLogPath, f.EncodeLogPath })
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Distinct()
            .ToList();

        if (logPaths.Count == 0)
        {
            _notifier.Warning("No log files found to save.");
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(
            Path.GetDirectoryName(logPaths[0])!,
            $"ripsharp-logs-{timestamp}.zip");

        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var logPath in logPaths)
            {
                zip.CreateEntryFromFile(logPath!, Path.GetFileName(logPath!));
            }

            AnsiConsole.MarkupLine($"[green]Saved {logPaths.Count} log file(s) to:[/] {Markup.Escape(zipPath)}");
        }
        catch (Exception ex)
        {
            _notifier.Warning($"Failed to create log archive: {ex.Message}");
        }
    }

    private void RenderFailureDetail(TitleOutcome failure)
    {
        AnsiConsole.WriteLine();
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        grid.AddRow("[dim]Title:[/]", Markup.Escape(failure.Plan.DisplayName));
        grid.AddRow("[dim]Phase:[/]", failure.FailedPhase?.ToString() ?? "Unknown");
        if (!string.IsNullOrWhiteSpace(failure.Command))
            grid.AddRow("[dim]Command:[/]", $"[yellow]{Markup.Escape(failure.Command)}[/]");
        if (!string.IsNullOrWhiteSpace(failure.RipLogPath))
            grid.AddRow("[dim]Rip log:[/]", Markup.Escape(failure.RipLogPath));
        if (!string.IsNullOrWhiteSpace(failure.EncodeLogPath))
            grid.AddRow("[dim]Encode log:[/]", Markup.Escape(failure.EncodeLogPath));
        AnsiConsole.Write(grid);

        if (failure.ErrorLines is { Count: > 0 })
        {
            AnsiConsole.WriteLine();
            var panel = new Panel(
                    string.Join(Environment.NewLine, failure.ErrorLines.TakeLast(15).Select(Markup.Escape)))
                .Header("[bold red]Error Details[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Red)
                .Padding(1, 0);
            AnsiConsole.Write(panel);
        }
        AnsiConsole.WriteLine();
    }
}
