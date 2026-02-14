namespace BugZapperLabs.RipSharp.Abstractions;

public interface IEncoderService
{
    Task<MediaFileAnalysis?> AnalyzeAsync(string filePath);
    Task<bool> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles, int ordinal, int total, IProgressTask? progressTask = null);
}
