using BugZapperLabs.RipSharp.Models;

namespace BugZapperLabs.RipSharp.Abstractions;

/// <summary>
/// Interface for prompting users for input during interactive operations.
/// </summary>
public interface IUserPrompt
{
    /// <summary>
    /// Prompts the user to select between movie or TV series mode.
    /// </summary>
    /// <param name="detectionHint">Optional hint about what was detected (e.g., "detected as movie with 65% confidence")</param>
    /// <returns>True for TV series, false for movie</returns>
    bool PromptForContentType(string? detectionHint = null);

    /// <summary>
    /// Displays the rip plan and asks the user to confirm, edit, or abort.
    /// </summary>
    PreviewResult ConfirmRipPlan(IReadOnlyList<TitlePlan> plans, ContentMetadata metadata, bool isTv, string? discName = null, int? season = null, int? episodeStart = null, IReadOnlySet<int>? selectedTitleIds = null);

    /// <summary>
    /// Displays an interactive review of failed titles and lets the user inspect, retry, or skip each one.
    /// Returns the list of TitleOutcomes the user wants to retry.
    /// </summary>
    List<TitleOutcome> ReviewFailures(IReadOnlyList<TitleOutcome> failures);
}

public enum PreviewAction
{
    Proceed,
    Abort,
    EditTitle,
    EditEpisodeStart,
    EditFilenames,
    SelectTitles
}

public class PreviewResult
{
    public PreviewAction Action { get; init; }
    public string? NewTitle { get; init; }
    public int? NewEpisodeStart { get; init; }
    public Dictionary<int, string>? RenamedFiles { get; init; }
    public HashSet<int>? SelectedTitleIds { get; init; }

    public static PreviewResult Proceed() => new() { Action = PreviewAction.Proceed };
    public static PreviewResult Abort() => new() { Action = PreviewAction.Abort };
}
