namespace BugZapperLabs.RipSharp.Models;

public record DiscProcessingResult(
    List<string> SuccessFiles,
    List<TitleOutcome> Failures,
    int TotalTitles)
{
    public bool AllSucceeded => Failures.Count == 0;
    public bool AnySucceeded => SuccessFiles.Count > 0;
}
