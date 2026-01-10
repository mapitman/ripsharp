namespace MediaEncoding;

public class StreamInfo
{
    public string CodecType { get; set; } = string.Empty; // video|audio|subtitle
    public int Index { get; set; }
    public string? Language { get; set; }
    public string? CodecName { get; set; }
    public int? Channels { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}
