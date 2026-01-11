using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RipSharp.MakeMkv;

public class ScanOutputHandler
{
    private readonly IConsoleWriter _notifier;
    private readonly List<TitleInfo> _titles;
    private string? _discName;
    private string? _discType;

    private bool _discDetectedPrinted;
    private bool _scanningStarted;
    private int _titleAddedCount;
    private readonly HashSet<string> _printedTitles = new();

    public ScanOutputHandler(IConsoleWriter notifier, List<TitleInfo> titles)
    {
        _notifier = notifier;
        _titles = titles;
    }

    public string? DiscName => _discName;
    public string? DiscType => _discType;
    public int TitleAddedCount => _titleAddedCount;

    public void HandleLine(string line)
    {
        // Handle UI messages first
        HandleProgressMessages(line);

        // Then parse protocol data
        ParseProtocolData(line);
    }

    private void HandleProgressMessages(string line)
    {
        if (line.StartsWith("MSG:") && line.Contains("insert disc"))
        {
            _notifier.Warning("💿 Insert a disc into the drive...");
        }
        else if (line.StartsWith("DRV:0,") && !line.Contains(",256,") && !_discDetectedPrinted)
        {
            _notifier.Success("📀 Disc detected in drive...");
            _discDetectedPrinted = true;
        }
        else if (line.StartsWith("MSG:1005,"))
        {
            var msg = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrWhiteSpace(msg))
                _notifier.Muted($"▸ {msg}");
        }
        else if (line.StartsWith("MSG:1011,") || line.StartsWith("MSG:3007,"))
        {
            var msg = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrWhiteSpace(msg))
                _notifier.Muted($"▸ {msg}");
        }
        else if (line.StartsWith("MSG:5085,"))
        {
            _notifier.Muted("▸ Loaded content hash table");
        }
        else if (line.StartsWith("MSG:") && (line.Contains("Scanning") || line.Contains("scanning")) && !_scanningStarted)
        {
            _notifier.Info("🔍 Scanning disc structure...");
            _scanningStarted = true;
        }
        else if (line.StartsWith("MSG:3307,"))
        {
            _titleAddedCount++;
            var fileMatch = Regex.Match(line, @"""File ([^\s]+)");
            var titleMatch = Regex.Match(line, @"title #(\d+)");
            if (fileMatch.Success && titleMatch.Success)
            {
                _notifier.Success($"  ✓ Added title #{titleMatch.Groups[1].Value}: {fileMatch.Groups[1].Value}");
            }
        }
        else if (line.StartsWith("MSG:3309,"))
        {
            var match = Regex.Match(line, @"""Title ([^\s]+) is equal");
            if (match.Success)
            {
                _notifier.Muted($"  ~ Skipped duplicate: {match.Groups[1].Value}");
            }
        }
        else if (line.StartsWith("MSG:3025,"))
        {
            var match = Regex.Match(line, @"""Title #([^\s]+)");
            if (match.Success)
            {
                _notifier.Muted($"  - Skipped short: {match.Groups[1].Value}");
            }
        }
        else if (line.StartsWith("MSG:") && (line.Contains("error") || line.Contains("fail")))
        {
            var message = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrWhiteSpace(message))
            {
                _notifier.Error($"❌ {message}");
            }
            else
            {
                _notifier.Error($"❌ {line}");
            }
        }
        else if (line.StartsWith("CINFO:1,"))
        {
            var dtype = MakeMkvProtocol.ExtractQuoted(line);
            if (!string.IsNullOrWhiteSpace(dtype))
            {
                _notifier.Accent($"💽 Disc type: {dtype}");
            }
        }
        else if (line.StartsWith("TINFO:"))
        {
            var parts = line.Split(new[] { ',' }, 4);
            if (parts.Length >= 4 && parts[1] == "2")
            {
                var tname = MakeMkvProtocol.ExtractQuoted(line);
                if (!string.IsNullOrWhiteSpace(tname) && _printedTitles.Add(tname!))
                {
                    _notifier.Highlight($"🎞️ Title found: {tname}");
                }
            }
        }
    }

    private void ParseProtocolData(string line)
    {
        if (line.StartsWith("CINFO:1,"))
        {
            _discType = MakeMkvProtocol.ExtractQuoted(line) ?? _discType;
        }
        else if (line.StartsWith("CINFO:2,") && string.IsNullOrEmpty(_discName))
        {
            _discName = MakeMkvProtocol.ExtractQuoted(line);
        }
        else if (line.StartsWith("DRV:"))
        {
            // ignore
        }
        else if (line.StartsWith("TINFO:"))
        {
            ParseTitleInfo(line);
        }
    }

    private void ParseTitleInfo(string line)
    {
        var match = Regex.Match(line, @"TINFO:(\d+),(\d+),");
        if (!match.Success) return;

        var id = int.Parse(match.Groups[1].Value);
        var fieldId = int.Parse(match.Groups[2].Value);

        if (!_titles.Exists(t => t.Id == id))
            _titles.Add(new TitleInfo { Id = id });

        var title = _titles.Find(x => x.Id == id)!;

        switch (fieldId)
        {
            case 2: // Title name
                var name = MakeMkvProtocol.ExtractQuoted(line);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    title.Name = name;
                    if (_discName == null)
                        _discName = name;
                }
                break;
            case 9: // Duration (HH:MM:SS format)
                var durStr = MakeMkvProtocol.ExtractQuoted(line);
                title.DurationSeconds = ParseDurationToSeconds(durStr);
                break;
            case 11: // Size in bytes
                var sizeStr = MakeMkvProtocol.ExtractQuoted(line);
                if (long.TryParse(sizeStr, out var bytes))
                    title.ReportedSizeBytes = bytes;
                break;
        }
    }

    private static int ParseDurationToSeconds(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split(':');
        if (parts.Length == 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var sec))
            return h * 3600 + m * 60 + sec;
        return 0;
    }
}
