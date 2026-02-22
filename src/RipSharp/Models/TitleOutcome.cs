namespace BugZapperLabs.RipSharp.Models;

public enum ProcessingPhase
{
    Rip,
    Encode
}

public record TitleOutcome(
    TitlePlan Plan,
    bool Success,
    ProcessingPhase? FailedPhase = null,
    string? FinalPath = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? ErrorLines = null,
    string? Command = null,
    string? RipLogPath = null,
    string? EncodeLogPath = null,
    string? RippedFilePath = null);
