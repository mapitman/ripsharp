namespace BugZapperLabs.RipSharp.Models;

public class ContentMetadata
{
    public string Title { get; set; } = "Unknown";
    public int? Year { get; set; }
    public string Type { get; set; } = "movie"; // movie|tv

    public string? Provider { get; set; }
    public string? SearchTitle { get; set; }
    public string? DiscTitle { get; set; }
}
