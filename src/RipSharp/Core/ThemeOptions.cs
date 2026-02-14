namespace BugZapperLabs.RipSharp.Core;

public class ThemeOptions
{
    public string? Path { get; set; }
    public ThemeColors Colors { get; set; } = new();
    public ThemeEmojis Emojis { get; set; } = new();
}

public class ThemeColors
{
    public string Success { get; set; } = "#94e2d5";
    public string Error { get; set; } = "#f38ba8";
    public string Warning { get; set; } = "#f9e2af";
    public string Info { get; set; } = "#89b4fa";
    public string Accent { get; set; } = "#89dceb";
    public string Muted { get; set; } = "#6c7086";
    public string Highlight { get; set; } = "#cba6f7";
}

public class ThemeEmojis
{
    public string Success { get; set; } = "✓";
    public string Error { get; set; } = "❌";
    public string Warning { get; set; } = "⚠️";
    public string InsertDisc { get; set; } = "💿";
    public string DiscDetected { get; set; } = "📀";
    public string Scan { get; set; } = "🔍";
    public string DiscType { get; set; } = "💽";
    public string TitleFound { get; set; } = "🎞️";
    public string Tv { get; set; } = "📺";
    public string Movie { get; set; } = "🎬";
}
