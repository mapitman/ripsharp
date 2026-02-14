using System.Globalization;

using BugZapperLabs.RipSharp.Core;

using Microsoft.Extensions.Options;

using Spectre.Console;

namespace BugZapperLabs.RipSharp.Utilities;

public interface IThemeProvider
{
    ThemeColors Colors { get; }
    ThemeEmojis Emojis { get; }
    Color SuccessColor { get; }
    Color ErrorColor { get; }
    Color WarningColor { get; }
    Color InfoColor { get; }
    Color AccentColor { get; }
    Color MutedColor { get; }
    Color HighlightColor { get; }
}

public class ThemeProvider : IThemeProvider
{
    private readonly ThemeOptions _options;

    public ThemeProvider(IOptions<ThemeOptions> options)
    {
        _options = options.Value ?? new ThemeOptions();
    }

    public static IThemeProvider CreateDefault()
    {
        return new ThemeProvider(Options.Create(new ThemeOptions()));
    }

    public ThemeColors Colors => _options.Colors;
    public ThemeEmojis Emojis => _options.Emojis;

    public Color SuccessColor => ParseHexColor(Colors.Success, new Color(148, 226, 213));
    public Color ErrorColor => ParseHexColor(Colors.Error, new Color(243, 139, 168));
    public Color WarningColor => ParseHexColor(Colors.Warning, new Color(249, 226, 175));
    public Color InfoColor => ParseHexColor(Colors.Info, new Color(137, 180, 250));
    public Color AccentColor => ParseHexColor(Colors.Accent, new Color(137, 220, 235));
    public Color MutedColor => ParseHexColor(Colors.Muted, new Color(108, 112, 134));
    public Color HighlightColor => ParseHexColor(Colors.Highlight, new Color(203, 166, 247));

    private static Color ParseHexColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var trimmed = hex.Trim().TrimStart('#');
        if (trimmed.Length != 6)
        {
            return fallback;
        }

        if (!int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return fallback;
        }

        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        return new Color(r, g, b);
    }
}
