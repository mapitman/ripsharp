using Spectre.Console;

namespace BugZapperLabs.RipSharp.Utilities;

/// <summary>
/// Spectre.Console implementation for writing styled messages to the console.
/// </summary>
public class ConsoleWriter : IConsoleWriter
{
    private readonly IThemeProvider _theme;

    public ConsoleWriter(IThemeProvider theme)
    {
        _theme = theme;
    }

    private static void WriteColored(string color, string message)
        => AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");

    public void Info(string message) => WriteColored(_theme.Colors.Info, message);
    public void Success(string message) => WriteColored(_theme.Colors.Success, message);
    public void Warning(string message) => WriteColored(_theme.Colors.Warning, message);
    public void Error(string message) => WriteColored(_theme.Colors.Error, message);
    public void Muted(string message) => WriteColored(_theme.Colors.Muted, message);
    public void Accent(string message) => WriteColored(_theme.Colors.Accent, message);
    public void Highlight(string message) => WriteColored(_theme.Colors.Highlight, message);
    public void Plain(string message) => AnsiConsole.WriteLine(Markup.Escape(message));
}
