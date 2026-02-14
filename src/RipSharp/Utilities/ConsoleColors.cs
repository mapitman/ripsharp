using Spectre.Console;

namespace BugZapperLabs.RipSharp.Utilities;

/// <summary>
/// Consistent color palette for Spectre.Console markup throughout the application.
/// Using Catppuccin Mocha color scheme.
/// </summary>
public static class ConsoleColors
{
    // Primary colors
    public const string Success = "#94e2d5";  // Teal - for success messages and progress
    public const string Error = "#f38ba8";    // Red - for errors and failures
    public const string Warning = "#f9e2af";  // Yellow - for warnings and alerts
    public const string Info = "#89b4fa";     // Blue - for informational messages
    public const string Accent = "#89dceb";   // Sky - for highlights and accents
    public const string Muted = "#6c7086";    // Overlay2 - for dim/secondary text
    public const string Highlight = "#cba6f7"; // Mauve - for special highlights

    // Utility
    public const string Bold = "bold";
}

public static class CustomColors
{
    public static readonly Color Success = new Color(148, 226, 213);   // #94e2d5
    public static readonly Color Error = new Color(243, 139, 168);     // #f38ba8
    public static readonly Color Warning = new Color(249, 226, 175);   // #f9e2af
    public static readonly Color Info = new Color(137, 180, 250);      // #89b4fa
    public static readonly Color Accent = new Color(137, 220, 235);    // #89dceb
    public static readonly Color Muted = new Color(108, 112, 134);     // #6c7086
    public static readonly Color Highlight = new Color(203, 166, 247); // #cba6f7
}
