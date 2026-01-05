namespace MediaEncoding;

public interface IDiscRipper
{
    Task<List<string>> ProcessDiscAsync(RipOptions options);
}
