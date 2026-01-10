using System;
using System.IO;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace MediaEncoding;

public class MakeMkvOutputHandler
{
    private readonly long _expectedBytes;
    private readonly int _index;
    private readonly int _totalTitles;
    private readonly ProgressTask _task;
    private readonly string _progressLogPath;
    private readonly string _rawLogPath;

    public double LastBytesProcessed { get; private set; }

    public MakeMkvOutputHandler(long expectedBytes, int index, int totalTitles, ProgressTask task, string progressLogPath, string rawLogPath)
    {
        _expectedBytes = expectedBytes;
        _index = index;
        _totalTitles = totalTitles;
        _task = task;
        _progressLogPath = progressLogPath;
        _rawLogPath = rawLogPath;
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
                if (_expectedBytes > 0 && bytesProcessed <= 1.0)
                    bytesProcessed *= _expectedBytes; // fraction -> bytes
                bytesProcessed = Math.Max(0, _expectedBytes > 0 ? Math.Min(_expectedBytes, bytesProcessed) : bytesProcessed);
                _task.Value = bytesProcessed;
                LastBytesProcessed = bytesProcessed;
                TryAppend(_progressLogPath, $"PRGV {bytesProcessed:F0}\n");
            }
        }
        else if (line.StartsWith("PRGC:"))
        {
            var caption = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrEmpty(caption))
            {
                _task.Description = $"[{ConsoleColors.Success}]{caption} ({_index + 1}/{_totalTitles})[/]";
            }
            TryAppend(_progressLogPath, $"PRGC {caption}\n");
        }
    }

    private static void TryAppend(string path, string content)
    {
        try
        {
            File.AppendAllText(path, content);
        }
        catch (Exception ex)
        {
            // Best-effort logging: do not rethrow, but make failures visible.
            AnsiConsole.MarkupLine(
                $"[red]Failed to append to log file '{Markup.Escape(path)}': {Markup.Escape(ex.Message)}[/]");
        }
    }
}
