namespace MediaEncoding;

public class FileAnalysis
{
    public required List<StreamInfo> Streams { get; init; }
    public double? DurationSeconds { get; init; }
}
