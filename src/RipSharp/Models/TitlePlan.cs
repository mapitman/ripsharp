namespace BugZapperLabs.RipSharp.Models;

public record TitlePlan(
    int TitleId,
    int Index,
    int? EpisodeNum,
    string? EpisodeTitle,
    string TempOutputPath,
    string FinalFileName,
    string? VersionSuffix,
    string DisplayName,
    int DurationSeconds = 0);
