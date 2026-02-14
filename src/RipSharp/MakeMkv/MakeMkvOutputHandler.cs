using System.Text.RegularExpressions;

namespace BugZapperLabs.RipSharp.MakeMkv;

public class MakeMkvOutputHandler
{
    private readonly long _expectedBytes;
    private readonly int _index;
    private readonly int _totalTitles;
    private readonly IProgressTask? _task;
    private readonly string _progressLogPath;
    private readonly string _rawLogPath;
    private readonly IConsoleWriter _writer;
    private readonly IThemeProvider _theme;

    public double LastBytesProcessed { get; private set; }
    public double LastProgressFraction { get; private set; }

    public MakeMkvOutputHandler(long expectedBytes, int index, int totalTitles, IProgressTask? task, string progressLogPath, string rawLogPath, IConsoleWriter writer, IThemeProvider theme)
    {
        _expectedBytes = expectedBytes;
        _index = index;
        _totalTitles = totalTitles;
        _task = task;
        _progressLogPath = progressLogPath;
        _rawLogPath = rawLogPath;
        _writer = writer;
        _theme = theme;
    }

    public void HandleLine(string line)
    {
        TryAppend(_rawLogPath, line + "\n");
        if (line.StartsWith("PRGV:"))
        {
            var m = Regex.Match(line, @"PRGV:\s*([0-9]+(?:\.[0-9]+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value, out var raw))
            {
                double bytesProcessed = raw;
                double fraction = 0;

                if (_expectedBytes > 0)
                {
                    if (bytesProcessed <= 1.0)
                    {
                        fraction = Math.Clamp(bytesProcessed, 0, 1);
                        bytesProcessed = fraction * _expectedBytes; // fraction -> bytes
                    }
                    else if (bytesProcessed <= 100.0 && _expectedBytes >= 1024 * 1024)
                    {
                        fraction = Math.Clamp(bytesProcessed / 100.0, 0, 1);
                        bytesProcessed = fraction * _expectedBytes; // percent -> bytes
                    }
                    else
                    {
                        bytesProcessed = Math.Clamp(bytesProcessed, 0, _expectedBytes);
                        fraction = _expectedBytes > 0 ? bytesProcessed / _expectedBytes : 0;
                    }
                }
                else
                {
                    // When size is unknown, PRGV can be 0..1 (fraction) or 0..100 (percent)
                    if (bytesProcessed <= 1.0)
                    {
                        fraction = Math.Clamp(bytesProcessed, 0, 1);
                    }
                    else if (bytesProcessed <= 100.0)
                    {
                        fraction = Math.Clamp(bytesProcessed / 100.0, 0, 1);
                    }
                }

                LastProgressFraction = fraction;
                if (_task != null)
                    _task.Value = (long)bytesProcessed;
                LastBytesProcessed = bytesProcessed;
                TryAppend(_progressLogPath, $"PRGV {bytesProcessed:F0}\n");
            }
        }
        else if (line.StartsWith("PRGC:"))
        {
            var caption = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrEmpty(caption))
            {
                if (_task != null)
                    _task.Description = $"[{_theme.Colors.Success}]{caption} ({_index + 1}/{_totalTitles})[/]";
            }
            TryAppend(_progressLogPath, $"PRGC {caption}\n");
        }
    }

    private void TryAppend(string path, string content)
    {
        try
        {
            File.AppendAllText(path, content);
        }
        catch (Exception ex)
        {
            // Best-effort logging: do not rethrow, but make failures visible.
            _writer.Error($"Failed to append to log file '{path}': {ex.Message}");
        }
    }
}
