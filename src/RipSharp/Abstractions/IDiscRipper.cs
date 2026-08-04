using BugZapperLabs.RipSharp.Models;

namespace BugZapperLabs.RipSharp.Abstractions;

public interface IDiscRipper
{
    Task<DiscProcessingResult> ProcessDiscAsync(RipOptions options, CancellationToken cancellationToken = default);
}
