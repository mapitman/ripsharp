namespace MediaEncoding;

public interface IDiscScanner
{
    Task<DiscInfo?> ScanDiscAsync(string discPath);
    List<int> IdentifyMainContent(DiscInfo info, bool isTv);
}
