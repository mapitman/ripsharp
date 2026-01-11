using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;


namespace RipSharp.Services;

public class EncoderService : IEncoderService
{
    private readonly IProcessRunner _runner;
    private readonly IConsoleWriter _notifier;
    private readonly IProgressDisplay _progressDisplay;

    public EncoderService(IProcessRunner runner, IConsoleWriter notifier, IProgressDisplay progressDisplay)
    {
        _runner = runner;
        _notifier = notifier;
        _progressDisplay = progressDisplay;
    }

    public async Task<MediaFileAnalysis?> AnalyzeAsync(string filePath)
    {
        var json = new System.Text.StringBuilder();
        var exit = await _runner.RunAsync("ffprobe", $"-v quiet -print_format json -show_streams -show_format \"{filePath}\"",
            onOutput: line => json.AppendLine(line));
        if (exit != 0) return null;
        var doc = JsonDocument.Parse(json.ToString());
        var streams = new List<MediaStream>();
        double? durationSeconds = null;

        // Extract duration from format section
        if (doc.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var dur) &&
            double.TryParse(dur.GetString(), out var durVal))
        {
            durationSeconds = durVal;
        }

        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
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
        return new MediaFileAnalysis { Streams = streams, DurationSeconds = durationSeconds };
    }

    public async Task<bool> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles, int ordinal, int total)
    {
        var analysis = await AnalyzeAsync(inputFile);
        if (analysis == null) return false;

        var selected = SelectStreams(analysis, includeEnglishSubtitles);
        var ffmpegArgs = BuildFfmpegArguments(inputFile, outputFile, selected);

        DisplayEncodingInfo(inputFile, outputFile, ordinal, total);

        var durationTicks = (long)((analysis.DurationSeconds ?? 0) * 1000.0 * TimeSpan.TicksPerMillisecond);
        var exit = await RunEncodingWithProgress(ffmpegArgs, durationTicks, ordinal, total);

        return exit == 0;
    }

    private void DisplayEncodingInfo(string inputFile, string outputFile, int ordinal, int total)
    {
        var inputFileName = System.IO.Path.GetFileName(inputFile);
        var outputFileName = System.IO.Path.GetFileName(outputFile);
        _notifier.Info($"🎬 Encoding ({ordinal}/{total}): {inputFileName}");
        _notifier.Info($"   → Output: {outputFileName}");
        _notifier.Muted("   Settings: x264 preset=slow CRF=22");
    }

    private async Task<int> RunEncodingWithProgress(string ffmpegArgs, long durationTicks, int ordinal, int total)
    {
        var exit = 0;

        await _progressDisplay.ExecuteAsync(async ctx =>
        {
            var task = ctx.AddTask($"[{ConsoleColors.Success}]Encoding ({ordinal}/{total})[/]", durationTicks);

            exit = await _runner.RunAsync("ffmpeg", ffmpegArgs,
                onOutput: _ => { },  // ffmpeg stdout - not used with -progress pipe:2
                onError: line => HandleEncodingProgress(line, task, durationTicks, ordinal, total));

            if (exit == 0 && task.Value < durationTicks)
            {
                task.Value = durationTicks;
            }
            task.StopTask();
        });

        return exit;
    }

    private void HandleEncodingProgress(string line, IProgressTask task, long durationTicks, int ordinal, int total)
    {
        // Progress lines come in key=value format on stderr
        // Use out_time_us (microseconds) for accurate progress tracking
        if (line.StartsWith("out_time_us="))
        {
            var timeStr = line.Substring("out_time_us=".Length).Trim();
            if (double.TryParse(timeStr, out var timeUs))
            {
                var currentTimeMs = timeUs / 1000.0;  // Convert microseconds to milliseconds
                var timeTicks = (long)(currentTimeMs * TimeSpan.TicksPerMillisecond);
                task.Value = Math.Min(durationTicks, timeTicks);
            }
        }
        else if (line.StartsWith("speed="))
        {
            var speed = line.Substring("speed=".Length).TrimEnd('x');
            if (!string.IsNullOrEmpty(speed) && speed != "0.00" && speed != "N/A")
            {
                task.Description = $"[{ConsoleColors.Success}]Encoding ({ordinal}/{total}, {speed}x)[/]";
            }
        }
        else
        {
            // Filter out other progress lines
            static bool IsProgressLine(string s) =>
                s.StartsWith("frame=") || s.StartsWith("fps=") || s.StartsWith("stream_") ||
                s.StartsWith("bitrate=") || s.StartsWith("total_size=") || s.StartsWith("out_time") ||
                s.StartsWith("dup_frames=") || s.StartsWith("drop_frames=") || s.StartsWith("progress=");

            if (string.IsNullOrWhiteSpace(line) || IsProgressLine(line)) return;
            _notifier.Error($"❌ ffmpeg: {line}");
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

        // Filter to only English audio tracks
        var audioStreams = streams.FindAll(s => s.CodecType == "audio" &&
            (s.Language == null || s.Language == "eng" || s.Language == "en"));

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
