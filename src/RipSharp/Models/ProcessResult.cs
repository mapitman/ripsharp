namespace BugZapperLabs.RipSharp.Models;

public record ProcessResult(
    bool Success,
    int ExitCode,
    IReadOnlyList<string> ErrorLines,
    string? Command = null,
    string? LogPath = null)
{
    public string ErrorSummary
    {
        get
        {
            var summary = ErrorLines.Count > 0
                ? string.Join(Environment.NewLine, ErrorLines.TakeLast(10))
                : "No error details captured";
            return $"{GetProcessLabel()} exited with code {ExitCode}{(LogPath != null ? $" (log: {LogPath})" : "")}{Environment.NewLine}{summary}";
        }
    }

    private string GetProcessLabel()
    {
        if (string.IsNullOrWhiteSpace(Command))
            return "Process";

        var trimmed = Command.TrimStart();
        if (trimmed.Length == 0)
            return "Process";

        if (trimmed[0] is '"' or '\'')
        {
            var quote = trimmed[0];
            var endQuote = trimmed.IndexOf(quote, 1);
            if (endQuote > 1)
                return trimmed.Substring(1, endQuote - 1);

            return trimmed.Trim(quote);
        }

        var firstWhitespace = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        return firstWhitespace > 0 ? trimmed[..firstWhitespace] : trimmed;
    }
}
