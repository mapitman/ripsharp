using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediaEncoding;

public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, string arguments, 
        System.Action<string>? onOutput = null, System.Action<string>? onError = null,
        System.Threading.CancellationToken ct = default);
}

public interface IDiscScanner
{
    Task<DiscInfo?> ScanDiscAsync(string discPath);
    List<int> IdentifyMainContent(DiscInfo info, bool isTv);
}

public interface IEncoderService
{
    Task<FileAnalysis?> AnalyzeAsync(string filePath);
    Task<bool> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles);
}

public interface IMetadataService
{
    Task<Metadata?> LookupAsync(string title, bool isTv, int? year);
}

public interface IDiscRipper
{
    Task<List<string>> ProcessDiscAsync(RipOptions options);
}

public class DiscInfo
{
    public string DiscName { get; set; } = string.Empty;
    public string DiscType { get; set; } = string.Empty; // dvd|bd|uhd
    public List<TitleInfo> Titles { get; set; } = new();
}

public class TitleInfo
{
    public int Id { get; set; }
    public int DurationSeconds { get; set; }
    public long? ReportedSizeBytes { get; set; }
}

public class FileAnalysis
{
    public required List<StreamInfo> Streams { get; init; }
}

public class StreamInfo
{
    public string CodecType { get; set; } = string.Empty; // video|audio|subtitle
    public int Index { get; set; }
    public string? Language { get; set; }
    public int? Channels { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

public class Metadata
{
    public string Title { get; set; } = "Unknown";
    public int? Year { get; set; }
    public string Type { get; set; } = "movie"; // movie|tv
}
