namespace MediaEncoding;

public interface IEncoderService
{
    Task<FileAnalysis?> AnalyzeAsync(string filePath);
    Task<bool> EncodeAsync(string inputFile, string outputFile, bool includeEnglishSubtitles);
}
