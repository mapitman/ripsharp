using Spectre.Console;

namespace RipSharp.Utilities;

/// <summary>
/// Manages terminal cursor visibility, ensuring it's restored on application exit.
/// </summary>
public class CursorManager : IDisposable
{
    private bool _disposed = false;

    public CursorManager()
    {
        // Hide cursor when initialized
        try
        {
            AnsiConsole.Cursor.Hide();
            Console.CursorVisible = false;
        }
        catch
        {
            // Silently fail if cursor operations aren't supported
        }
    }

    /// <summary>
    /// Restores cursor visibility. Safe to call multiple times.
    /// </summary>
    public void RestoreCursor()
    {
        if (_disposed) return;

        try
        {
            AnsiConsole.Cursor.Show();
            Console.CursorVisible = true;
        }
        catch
        {
            // Silently fail if cursor operations aren't supported
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        RestoreCursor();
        _disposed = true;
    }
}
