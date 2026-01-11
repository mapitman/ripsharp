namespace RipSharp.Abstractions;

/// <summary>
/// Abstraction for writing styled output messages to the console.
/// </summary>
public interface IConsoleWriter
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
    void Muted(string message);
    void Accent(string message);
    void Highlight(string message);
    void Plain(string message);
}
