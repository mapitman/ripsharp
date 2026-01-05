namespace MediaEncoding;

public class DiscInfo
{
    public string DiscName { get; set; } = string.Empty;
    public string DiscType { get; set; } = string.Empty; // dvd|bd|uhd
    public List<TitleInfo> Titles { get; set; } = new();
}
