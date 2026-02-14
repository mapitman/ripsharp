namespace BugZapperLabs.RipSharp.Models;

public class DiscInfo
{
    public string DiscName { get; set; } = string.Empty;
    public string DiscType { get; set; } = string.Empty; // dvd|bd|uhd
    public List<TitleInfo> Titles { get; set; } = new();

    /// <summary>
    /// Auto-detected content type: true for TV series, false for movie, null if detection uncertain.
    /// </summary>
    public bool? DetectedContentType { get; set; }

    /// <summary>
    /// Confidence score of the content type detection (0.0 to 1.0).
    /// </summary>
    public double DetectionConfidence { get; set; }
}
