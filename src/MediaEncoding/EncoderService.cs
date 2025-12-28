using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MediaEncoding;

public class EncoderService : IEncoderService
{
    private readonly IProcessRunner _runner;
    public EncoderService(IProcessRunner runner) => _runner = runner;

    public async Task<FileAnalysis?> AnalyzeAsync(string filePath)
    {
        var json = new System.Text.StringBuilder();
        var exit = await _runner.RunAsync("ffprobe", $"-v quiet -print_format json -show_streams -show_format \"{filePath}\"",
            onOutput: line => json.AppendLine(line));
        if (exit != 0) return null;
        var doc = JsonDocument.Parse(json.ToString());
        var streams = new List<StreamInfo>();
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var si = new StreamInfo
            {
                Index = s.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0,
                CodecType = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? string.Empty : string.Empty,
                Language = s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                Channels = s.TryGetProperty("channels", out var ch) ? ch.GetInt32() : (int?)null,
                Width = s.TryGetProperty("width", out var w) ? w.GetInt32() : (int?)null,
                Height = s.TryGetProperty("height", out var h) ? h.GetInt32() : (int?)null,
            };
            streams.Add(si);
        }
        return new FileAnalysis { Streams = streams };
    }

    public async Task<bool> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles)
    {
        // Build ffmpeg command: copy highest resolution video, include English stereo + surround, English subtitles.
        var analysis = await AnalyzeAsync(inputFile);
        if (analysis == null) return false;
        var streams = analysis.Streams;
        var video = ChooseBestVideo(streams);
        var englishAudios = streams.FindAll(s => s.CodecType == "audio" && (s.Language?.StartsWith("en") ?? false));
        var englishSubs = includeEnglishSubtitles ? streams.FindAll(s => s.CodecType == "subtitle" && (s.Language?.StartsWith("en") ?? false)) : new List<StreamInfo>();

        var args = new System.Text.StringBuilder();
        args.Append($"-i \"{inputFile}\" ");
        // Map streams explicitly
        if (video != null) args.Append($"-map 0:{video.Index} ");
        foreach (var a in englishAudios) args.Append($"-map 0:{a.Index} ");
        foreach (var sub in englishSubs) args.Append($"-map 0:{sub.Index} ");
        // Copy codecs for now to preserve quality
        args.Append("-c copy ");
        args.Append("-y ");
        args.Append($"\"{outputFile}\"");

        var exit = await _runner.RunAsync("ffmpeg", args.ToString(), onError: e => Console.Error.WriteLine(e));
        return exit == 0;
    }

    private static StreamInfo? ChooseBestVideo(List<StreamInfo> streams)
    {
        StreamInfo? best = null;
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
}
