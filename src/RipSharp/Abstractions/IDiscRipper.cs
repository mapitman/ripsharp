namespace RipSharp.Abstractions;

public interface IDiscRipper
{
    Task<List<string>> ProcessDiscAsync(RipOptions options, CancellationToken cancellationToken = default);
}
