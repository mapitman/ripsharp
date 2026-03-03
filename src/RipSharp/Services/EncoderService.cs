using System.Text.Json;

using BugZapperLabs.RipSharp.Models;

namespace BugZapperLabs.RipSharp.Services;

public class EncoderService : IEncoderService
{
    private readonly IProcessRunner _runner;

    public EncoderService(IProcessRunner runner) => _runner = runner;

    public async Task<MediaFileAnalysis?> AnalyzeAsync(string filePath)
    {
        var (analysis, _) = await AnalyzeDetailedAsync(filePath);
        return analysis;
    }

    private async Task<(MediaFileAnalysis? Analysis, List<string> Errors)> AnalyzeDetailedAsync(string filePath)
    {
        var errors = new List<string>();

        if (!File.Exists(filePath))
        {
            errors.Add($"Input file does not exist: {filePath}");
            return (null, errors);
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            errors.Add($"Input file is empty (0 bytes): {filePath}");
            return (null, errors);
        }

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            errors.Clear();

            var json = new System.Text.StringBuilder();
            var probeArgs = $"-v error -print_format json -show_streams -show_format \"{filePath}\"";
            var exit = await _runner.RunAsync("ffprobe", probeArgs,
                onOutput: line => json.AppendLine(line),
                onError: line => { if (!string.IsNullOrWhiteSpace(line)) errors.Add(line); });

            if (exit != 0)
            {
                errors.Insert(0, $"ffprobe exited with code {exit} (file size: {fileInfo.Length:N0} bytes)");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }
                return (null, errors);
            }

            var jsonStr = json.ToString();
            if (string.IsNullOrWhiteSpace(jsonStr))
            {
                errors.Add($"ffprobe produced no output for file (size: {fileInfo.Length:N0} bytes). The file may be corrupt or unreadable.");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }
                return (null, errors);
            }

            try
            {
                var doc = JsonDocument.Parse(jsonStr);
                var streams = new List<MediaStream>();
                double? durationSeconds = null;

                if (doc.RootElement.TryGetProperty("format", out var format) &&
                    format.TryGetProperty("duration", out var dur) &&
                    double.TryParse(dur.GetString(), out var durVal))
                {
                    durationSeconds = durVal;
                }

                if (!doc.RootElement.TryGetProperty("streams", out var streamsElement) ||
                    streamsElement.GetArrayLength() == 0)
                {
                    errors.Add($"ffprobe found no streams in file (file size: {fileInfo.Length:N0} bytes)");
                    return (null, errors);
                }

                foreach (var s in streamsElement.EnumerateArray())
                {
                    var si = new MediaStream
                    {
                        Index = s.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0,
                        CodecType = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? string.Empty : string.Empty,
                        CodecName = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null,
                        Language = s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                        Channels = s.TryGetProperty("channels", out var ch) ? ch.GetInt32() : (int?)null,
                        Width = s.TryGetProperty("width", out var w) ? w.GetInt32() : (int?)null,
                        Height = s.TryGetProperty("height", out var h) ? h.GetInt32() : (int?)null,
                    };
                    streams.Add(si);
                }
                return (new MediaFileAnalysis { Streams = streams, DurationSeconds = durationSeconds }, errors);
            }
            catch (JsonException ex)
            {
                var preview = jsonStr.Length > 200 ? jsonStr[..200] + "..." : jsonStr;
                errors.Add($"Failed to parse ffprobe output (attempt {attempt}/{maxAttempts}): {ex.Message}");
                errors.Add($"Raw output (first {Math.Min(jsonStr.Length, 200)} chars): {preview}");
                if (attempt < maxAttempts)
                {
                    errors.Clear();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }
                return (null, errors);
            }
        }

        // Unreachable, but satisfies the compiler
        errors.Add("ffprobe analysis failed after all attempts");
        return (null, errors);
    }

    public async Task<ProcessResult> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles, int ordinal, int total, IProgressTask? progressTask = null, string? logDirectory = null)
    {
        var (analysis, probeErrors) = await AnalyzeDetailedAsync(inputFile);
        if (analysis == null)
        {
            probeErrors.Insert(0, $"Pre-encoding analysis failed for input file: {Path.GetFileName(inputFile)}");
            return new ProcessResult(false, -1, probeErrors, $"ffprobe -v error -print_format json -show_streams -show_format \"{inputFile}\"");
        }

        var selected = SelectStreams(analysis, includeEnglishSubtitles);
        var ffmpegArgs = BuildFfmpegArguments(inputFile, outputFile, selected);

        var durationSeconds = analysis.DurationSeconds ?? 0;
        var durationTicks = (long)(durationSeconds * TimeSpan.TicksPerSecond);

        var errorLines = new List<string>();
        StreamWriter? logWriter = null;
        string? logPath = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
                var safeOutputName = Path.GetFileNameWithoutExtension(outputFile);
                logPath = Path.Combine(logDirectory, $"ffmpeg_{safeOutputName}.log");
                logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
                logWriter.WriteLine($"# ffmpeg {ffmpegArgs}");
                logWriter.WriteLine($"# Started: {DateTime.Now:O}");
                logWriter.WriteLine($"# Input: {inputFile} ({new FileInfo(inputFile).Length:N0} bytes)");
                logWriter.WriteLine($"# Duration: {durationSeconds:F1}s");
                logWriter.WriteLine($"# Streams: video={selected.Video?.CodecName ?? "none"} ({selected.Video?.Width}x{selected.Video?.Height})" +
                    $", audio=[{string.Join(", ", selected.Audio.Select(a => $"{a.CodecName}/{a.Language ?? "?"}/{a.Channels ?? 0}ch"))}]" +
                    $", subs=[{string.Join(", ", selected.Subtitles.Select(s => $"{s.CodecName}/{s.Language ?? "?"}"))}" +
                    $"]");
                logWriter.WriteLine();
            }

            var exit = await _runner.RunAsync("ffmpeg", ffmpegArgs,
                onOutput: _ => { },
                onError: line =>
                {
                    logWriter?.WriteLine(line);
                    HandleEncodingProgress(line, progressTask, durationTicks);
                    if (IsErrorLine(line))
                        errorLines.Add(line);
                });

            logWriter?.WriteLine();
            logWriter?.WriteLine($"# Exit code: {exit}");
            if (File.Exists(outputFile))
                logWriter?.WriteLine($"# Output: {new FileInfo(outputFile).Length:N0} bytes");
            else
                logWriter?.WriteLine("# Output: file not created");

            return new ProcessResult(exit == 0, exit, errorLines, $"ffmpeg {ffmpegArgs}", logPath);
        }
        finally
        {
            logWriter?.Dispose();
        }
    }

    private static bool IsErrorLine(string line)
    {
        if (line.Length == 0) return false;
        // Skip progress key=value lines (out_time_us=, speed=, bitrate=, etc.)
        if (line.Contains('=') && !line.Contains(' ')) return false;
        // Skip "frame=" status lines
        if (line.StartsWith("frame=")) return false;
        // Skip common ffmpeg informational/warning lines that aren't actionable errors
        if (line.StartsWith("  ")) return false; // continuation/indented lines from headers
        if (line.StartsWith("[") && line.Contains("] "))
        {
            // Codec context warnings like [hevc @ 0x...] are common and usually harmless
            // Only keep lines that contain actual error keywords
            var msg = line.Substring(line.IndexOf("] ") + 2);
            return msg.Contains("error", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("fatal", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("no such", StringComparison.OrdinalIgnoreCase);
        }
        if (line.StartsWith("Past duration")) return false;
        if (line.StartsWith("Avi duration") || line.StartsWith("avi duration")) return false;
        return true;
    }

    private void HandleEncodingProgress(string line, IProgressTask? task, long durationTicks)
    {
        if (task == null) return;

        if (line.StartsWith("out_time_us="))
        {
            var timeStr = line.Substring("out_time_us=".Length).Trim();
            if (double.TryParse(timeStr, out var timeUs))
            {
                var currentTimeMs = timeUs / 1000.0;
                var timeTicks = (long)(currentTimeMs * TimeSpan.TicksPerMillisecond);
                task.Value = Math.Min(100, (long)((double)timeTicks / durationTicks * 100));
            }
        }
    }

    private static MediaStream? ChooseBestVideo(List<MediaStream> streams)
    {
        MediaStream? best = null;
        foreach (var v in streams)
        {
            if (v.CodecType != "video") continue;
            if (best == null) best = v;
            else
            {
                int bestPixels = (best.Width ?? 0) * (best.Height ?? 0);
                int curPixels = (v.Width ?? 0) * (v.Height ?? 0);
                if (curPixels > bestPixels) best = v;
            }
        }
        return best;
    }

    private record SelectedStreams(
        MediaStream? Video,
        List<MediaStream> Audio,
        List<MediaStream> Subtitles);

    private static SelectedStreams SelectStreams(MediaFileAnalysis analysis, bool includeEnglishSubtitles)
    {
        var streams = analysis.Streams;
        var video = ChooseBestVideo(streams);

        // Keep English/unspecified audio tracks, plus the first audio track (original language).
        // The first track is included so that foreign-language discs (e.g. Japanese anime) retain
        // the original audio alongside any English dub, without pulling in every dubbed language.
        var allAudio = streams.FindAll(s => s.CodecType == "audio");
        var firstAudioIndex = allAudio.Count > 0 ? allAudio[0].Index : -1;
        var audioStreams = allAudio
            .Where(s => s.Language == null || s.Language == "eng" || s.Language == "en" || s.Index == firstAudioIndex)
            .ToList();

        // When English subtitles are requested, filter to English (or unspecified) subtitle tracks
        var subtitleStreams = includeEnglishSubtitles
            ? streams.FindAll(s => s.CodecType == "subtitle" &&
                (s.Language == null || s.Language == "eng" || s.Language == "en"))
            : new List<MediaStream>();

        return new SelectedStreams(video, audioStreams, subtitleStreams);
    }

    private static string BuildFfmpegArguments(string inputFile, string outputFile, SelectedStreams selected)
    {
        var args = new System.Text.StringBuilder();

        // Input probe settings first to satisfy ffmpeg recommendation for PGS subs
        args.Append("-probesize 400M -analyzeduration 400M ");
        args.Append($"-i \"{inputFile}\" ");

        // Map the selected video first
        if (selected.Video != null)
            args.Append($"-map 0:{selected.Video.Index} ");

        // Map every audio stream to preserve languages and commentaries
        foreach (var a in selected.Audio)
            args.Append($"-map 0:{a.Index} ");

        // Map subtitles when requested
        foreach (var s in selected.Subtitles)
            args.Append($"-map 0:{s.Index} ");

        args.Append("-map_chapters 0 ");

        // Video encoding per HandBrake mkv preset (x264, slow, CRF 22, decomb equivalent)
        args.Append("-c:v libx264 -preset slow -crf 22 -pix_fmt yuv420p -vf bwdif=mode=send_frame:parity=auto:deint=interlaced ");

        // Audio: copy AAC/AC3/EAC3, otherwise transcode to AAC 512k
        int audioOut = 0;
        foreach (var a in selected.Audio)
        {
            var codec = a.CodecName?.ToLowerInvariant();
            var canCopy = codec == "aac" || codec == "ac3" || codec == "eac3";
            if (canCopy)
            {
                args.Append($"-c:a:{audioOut} copy ");
            }
            else
            {
                args.Append($"-c:a:{audioOut} aac -b:a:{audioOut} 160k ");
            }
            audioOut++;
        }
        if (selected.Audio.Count == 0) args.Append("-an ");

        // Subtitles: keep all mapped subtitles (soft subs, no burn-in)
        int subOut = 0;
        foreach (var _ in selected.Subtitles)
        {
            args.Append($"-c:s:{subOut} copy ");
            subOut++;
        }
        if (selected.Subtitles.Count == 0) args.Append("-sn ");

        args.Append("-y ");
        args.Append($"\"{outputFile}\"");

        // Add progress and logging options
        return args + " -progress pipe:2 -loglevel warning";
    }
}
