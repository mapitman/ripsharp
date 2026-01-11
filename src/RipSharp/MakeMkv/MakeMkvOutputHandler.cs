using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RipSharp.MakeMkv;

public class MakeMkvOutputHandler
{
    private readonly long _expectedBytes;
    private readonly int _index;
    private readonly int _totalTitles;
    private readonly IProgressTask _task;
    private readonly string _progressLogPath;
    private readonly string _rawLogPath;
    private readonly IConsoleWriter _writer;

    public double LastBytesProcessed { get; private set; }

    public MakeMkvOutputHandler(long expectedBytes, int index, int totalTitles, IProgressTask task, string progressLogPath, string rawLogPath, IConsoleWriter writer)
    {
        _expectedBytes = expectedBytes;
        _index = index;
        _totalTitles = totalTitles;
        _task = task;
        _progressLogPath = progressLogPath;
        _rawLogPath = rawLogPath;
        _writer = writer;
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
                _task.Description = $"[{ConsoleColors.Success}]{caption} ({_index + 1}/{_totalTitles})[/]";
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
