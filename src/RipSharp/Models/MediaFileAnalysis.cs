namespace BugZapperLabs.RipSharp.Models;

public class MediaFileAnalysis
{
    public required List<MediaStream> Streams { get; init; }
    public double? DurationSeconds { get; init; }
}
