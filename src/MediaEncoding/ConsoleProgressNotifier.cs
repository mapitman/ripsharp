using Spectre.Console;

namespace MediaEncoding;

public class ConsoleProgressNotifier : IProgressNotifier
{
    private static void WriteColored(string color, string message)
        => AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");

    public void Info(string message) => WriteColored(ConsoleColors.Info, message);
    public void Success(string message) => WriteColored(ConsoleColors.Success, message);
    public void Warning(string message) => WriteColored(ConsoleColors.Warning, message);
    public void Error(string message) => WriteColored(ConsoleColors.Error, message);
    public void Muted(string message) => WriteColored(ConsoleColors.Muted, message);
    public void Accent(string message) => WriteColored(ConsoleColors.Accent, message);
    public void Highlight(string message) => WriteColored(ConsoleColors.Highlight, message);
    public void Plain(string message) => AnsiConsole.WriteLine(Markup.Escape(message));
}
