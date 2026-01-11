using Spectre.Console;

namespace RipSharp;

/// <summary>
/// Prompts users for input using Spectre.Console for rich terminal interactions.
/// </summary>
public class ConsoleUserPrompt : IUserPrompt
{
    private readonly IProgressNotifier _notifier;

    public ConsoleUserPrompt(IProgressNotifier notifier)
    {
        _notifier = notifier;
    }

    /// <summary>
    /// Prompts the user to select between movie or TV series mode.
    /// </summary>
    /// <param name="detectionHint">Optional hint about what was detected</param>
    /// <returns>True for TV series, false for movie</returns>
    public bool PromptForContentType(string? detectionHint = null)
    {
        var message = "Unable to confidently detect disc type";
        if (!string.IsNullOrEmpty(detectionHint))
        {
            message += $" ({detectionHint})";
        }
        
        _notifier.Warning(message);
        
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What type of content is this?")
                .AddChoices(new[] { "Movie", "TV Series" })
        );

        var isTv = selection == "TV Series";
        var mode = isTv ? "TV series" : "movie";
        _notifier.Info($"Using {mode} mode");
        
        return isTv;
    }
}
