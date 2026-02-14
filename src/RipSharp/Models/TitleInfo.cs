namespace BugZapperLabs.RipSharp.Models;

public class TitleInfo
{
    public int Id { get; set; }
    public int DurationSeconds { get; set; }
    public long? ReportedSizeBytes { get; set; }
    public string? Name { get; set; } // Disc-provided title name (e.g., Theatrical, Director's Cut)
}
